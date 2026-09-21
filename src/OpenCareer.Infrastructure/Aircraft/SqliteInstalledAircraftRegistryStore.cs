using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

public sealed class SqliteInstalledAircraftRegistryStore :
    IInstalledAircraftRegistryStore
{
    private const int CurrentDatabaseSchemaVersion = 1;
    private const int CurrentPayloadSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _initialized;

    public SqliteInstalledAircraftRegistryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString();
    }

    public async Task ReplaceAllAsync(
        IReadOnlyList<AircraftRegistryObservation> observations,
        CancellationToken cancellationToken = default)
    {
        AircraftRegistryObservation[] snapshot = ValidateSnapshot(observations);

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            using SqliteTransaction transaction = connection.BeginTransaction();

            await using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM installed_aircraft_observations;";
                await delete
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (AircraftRegistryObservation observation in snapshot)
            {
                await using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO installed_aircraft_observations (
                        canonical_aircraft_id,
                        provider_id,
                        provider_record_id,
                        payload_schema_version,
                        payload_json
                    )
                    VALUES (
                        $canonicalAircraftId,
                        $providerId,
                        $providerRecordId,
                        $payloadSchemaVersion,
                        $payloadJson
                    );
                    """;

                insert.Parameters.AddWithValue(
                    "$canonicalAircraftId",
                    observation.CanonicalAircraftId.Trim());
                insert.Parameters.AddWithValue(
                    "$providerId",
                    observation.ProviderId.Trim());
                insert.Parameters.AddWithValue(
                    "$providerRecordId",
                    observation.ProviderRecordId.Trim());
                insert.Parameters.AddWithValue(
                    "$payloadSchemaVersion",
                    CurrentPayloadSchemaVersion);
                insert.Parameters.AddWithValue(
                    "$payloadJson",
                    JsonSerializer.Serialize(observation, JsonOptions));

                await insert
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<AircraftRegistryObservation>> FindAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                canonical_aircraft_id,
                provider_id,
                provider_record_id,
                payload_schema_version,
                payload_json
            FROM installed_aircraft_observations
            WHERE canonical_aircraft_id = $canonicalAircraftId COLLATE NOCASE
            ORDER BY provider_id ASC, provider_record_id ASC;
            """;
        command.Parameters.AddWithValue(
            "$canonicalAircraftId",
            canonicalAircraftId.Trim());

        var result = new List<AircraftRegistryObservation>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader
                   .ReadAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            string persistedCanonicalId = reader.GetString(0);
            string persistedProviderId = reader.GetString(1);
            string persistedProviderRecordId = reader.GetString(2);
            int payloadSchemaVersion = reader.GetInt32(3);
            string payloadJson = reader.GetString(4);

            if (payloadSchemaVersion != CurrentPayloadSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Installed-aircraft registry payload schema {payloadSchemaVersion} is not supported.");
            }

            AircraftRegistryObservation observation =
                JsonSerializer.Deserialize<AircraftRegistryObservation>(
                    payloadJson,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    "Installed-aircraft registry payload is empty.");

            observation.Validate();

            if (!observation.IsInstalled
                || !string.Equals(
                    observation.CanonicalAircraftId.Trim(),
                    persistedCanonicalId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    observation.ProviderId.Trim(),
                    persistedProviderId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    observation.ProviderRecordId.Trim(),
                    persistedProviderRecordId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Installed-aircraft registry metadata does not match its payload.");
            }

            result.Add(observation);
        }

        return result;
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    "PRAGMA journal_mode = WAL;",
                    cancellationToken)
                .ConfigureAwait(false);

            int version = await GetDatabaseSchemaVersionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            if (version > CurrentDatabaseSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Installed-aircraft registry database schema {version} is newer than supported schema {CurrentDatabaseSchemaVersion}.");
            }

            if (version < 1)
            {
                using SqliteTransaction transaction = connection.BeginTransaction();

                await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        CREATE TABLE IF NOT EXISTS installed_aircraft_observations (
                            canonical_aircraft_id TEXT NOT NULL,
                            provider_id TEXT NOT NULL,
                            provider_record_id TEXT NOT NULL,
                            payload_schema_version INTEGER NOT NULL,
                            payload_json TEXT NOT NULL,
                            PRIMARY KEY (provider_id, provider_record_id)
                        );
                        """,
                        cancellationToken)
                    .ConfigureAwait(false);

                await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        CREATE INDEX IF NOT EXISTS ix_installed_aircraft_observations_canonical
                            ON installed_aircraft_observations (
                                canonical_aircraft_id COLLATE NOCASE,
                                provider_id,
                                provider_record_id
                            );
                        """,
                        cancellationToken)
                    .ConfigureAwait(false);

                await ExecuteAsync(
                        connection,
                        transaction,
                        "PRAGMA user_version = 1;",
                        cancellationToken)
                    .ConfigureAwait(false);

                transaction.Commit();
                version = 1;
            }

            if (version != CurrentDatabaseSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Installed-aircraft registry migration ended at schema {version}; expected {CurrentDatabaseSchemaVersion}.");
            }

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static AircraftRegistryObservation[] ValidateSnapshot(
        IReadOnlyList<AircraftRegistryObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var providerRecords = new HashSet<(string ProviderId, string ProviderRecordId)>();
        var snapshot = new AircraftRegistryObservation[observations.Count];

        for (int i = 0; i < observations.Count; i++)
        {
            AircraftRegistryObservation observation =
                observations[i]
                ?? throw new ArgumentException(
                    "Installed-aircraft snapshot cannot contain null observations.",
                    nameof(observations));

            observation.Validate();

            if (!observation.IsInstalled)
            {
                throw new ArgumentException(
                    "Installed-aircraft snapshot cannot contain non-installed observations.",
                    nameof(observations));
            }

            var key = (
                observation.ProviderId.Trim(),
                observation.ProviderRecordId.Trim());

            if (!providerRecords.Add(key))
            {
                throw new ArgumentException(
                    "Installed-aircraft snapshot contains duplicate provider record identities.",
                    nameof(observations));
            }

            snapshot[i] = observation;
        }

        return snapshot;
    }

    private static async Task<int> GetDatabaseSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        object? result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt32(
            result,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                "PRAGMA busy_timeout = 5000;",
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                "PRAGMA synchronous = NORMAL;",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
