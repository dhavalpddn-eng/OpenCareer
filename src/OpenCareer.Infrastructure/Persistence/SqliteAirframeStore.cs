using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Persistence;

public sealed partial class SqliteAirframeStore : IAirframeStore
{
    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteAirframeStore> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private int _initialized;

    public SqliteAirframeStore(OpenCareerDatabaseOptions options, ILogger<SqliteAirframeStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AirframeStoreRecord?> FindAsync(AirframeId airframeId, CancellationToken cancellationToken = default)
    {
        airframeId.Validate();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, airframeId, cancellationToken).ConfigureAwait(false);
    }

    public Task<AirframeStoreRecord> CreateAsync(
        Airframe airframe,
        AirframeCondition condition,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default) =>
        SaveAsync(airframe, condition, null, savedAt, cancellationToken);

    public Task<AirframeStoreRecord> UpdateConditionAsync(
        Airframe airframe,
        AirframeCondition condition,
        long expectedRevision,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        return SaveAsync(airframe, condition, expectedRevision, savedAt, cancellationToken);
    }

    private async Task<AirframeStoreRecord> SaveAsync(
        Airframe airframe,
        AirframeCondition condition,
        long? expectedRevision,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken)
    {
        long revision = expectedRevision is { } previous ? checked(previous + 1) : 1;
        var result = new AirframeStoreRecord(airframe, condition, revision, savedAt.ToUniversalTime());
        result.Validate();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // SQLite serializes writers across store instances; the revision predicate protects stale callers.
        using SqliteTransaction transaction = connection.BeginTransaction();

        if (expectedRevision is not null)
        {
            AirframeStoreRecord current = await ReadAsync(connection, transaction, airframe.AirframeId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new AirframeConcurrencyException("The physical airframe does not exist.");
            if (current.Airframe != airframe || current.Revision != expectedRevision)
                throw new AirframeConcurrencyException("Airframe identity or condition revision does not match the retained record.");
            if (result.SavedAt < current.SavedAt)
                throw new ArgumentOutOfRangeException(nameof(savedAt), "Condition save time cannot move backwards.");
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = expectedRevision is null
            ? """
              INSERT INTO airframes (
                  airframe_id, canonical_aircraft_id, created_at_utc_ticks,
                  wear_fraction, damage_state, revision, saved_at_utc_ticks)
              VALUES ($id, $aircraft, $created, $wear, $damage, $revision, $saved)
              ON CONFLICT(airframe_id) DO NOTHING;
              """
            : """
              UPDATE airframes
              SET wear_fraction = $wear, damage_state = $damage,
                  revision = $revision, saved_at_utc_ticks = $saved
              WHERE airframe_id = $id AND canonical_aircraft_id = $aircraft
                  AND created_at_utc_ticks = $created AND revision = $expected;
              """;
        command.Parameters.AddWithValue("$id", airframe.AirframeId.ToString());
        command.Parameters.AddWithValue("$aircraft", airframe.CanonicalAircraftId);
        command.Parameters.AddWithValue("$created", airframe.CreatedAt.UtcTicks);
        command.Parameters.AddWithValue("$wear", condition.WearFraction);
        command.Parameters.AddWithValue("$damage", (int)condition.Damage);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$saved", result.SavedAt.UtcTicks);
        if (expectedRevision is not null)
            command.Parameters.AddWithValue("$expected", expectedRevision.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new AirframeConcurrencyException("Airframe already exists or the condition revision changed.");

        transaction.Commit();
        _logger.LogInformation("Saved physical airframe {AirframeId} ({AircraftId}) at condition revision {Revision}.",
            airframe.AirframeId, airframe.CanonicalAircraftId, revision);
        return result;
    }

    private static async Task<AirframeStoreRecord?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AirframeId requestedId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT airframe_id, canonical_aircraft_id, created_at_utc_ticks,
                   wear_fraction, damage_state, revision, saved_at_utc_ticks
            FROM airframes WHERE airframe_id = $id;
            """;
        command.Parameters.AddWithValue("$id", requestedId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        try
        {
            // SQLite affinities permit malformed values in externally damaged saves. Never
            // accept coercion (for example, a text wear value converted to zero) as valid state.
            if (reader.GetValue(0) is not string || reader.GetValue(1) is not string
                || reader.GetValue(2) is not long || reader.GetValue(3) is not double
                || reader.GetValue(4) is not long || reader.GetValue(5) is not long || reader.GetValue(6) is not long)
                throw new InvalidDataException("Stored airframe column types are invalid.");

            AirframeId storedId = AirframeId.Parse(reader.GetString(0));
            if (storedId != requestedId || reader.GetString(0) != storedId.ToString())
                throw new InvalidDataException("Stored airframe identity does not match the requested identity.");

            var airframe = new Airframe(storedId, reader.GetString(1), new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero));
            var condition = new AirframeCondition(reader.GetDouble(3), (AirframeDamageState)checked((int)reader.GetInt64(4)));
            var result = new AirframeStoreRecord(airframe, condition, reader.GetInt64(5), new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero));
            result.Validate();
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidCastException)
        {
            throw new InvalidDataException("Stored airframe identity or condition is invalid.", ex);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) == 1)
            return;
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized == 1)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(_options.DatabasePath)!);
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await OpenCareerDatabaseMigrator.MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
