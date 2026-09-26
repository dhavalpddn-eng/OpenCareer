using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Infrastructure.Persistence;

public sealed partial class SqliteAirframeStore : IFlightAirframeConsequenceStore
{
    private static readonly JsonSerializerOptions ConsequenceJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<FlightAirframeApplication?> FindBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Session ID is required.", nameof(sessionId));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadConsequenceAsync(connection, null, sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FlightAirframeApplyResult> ApplyAsync(FlightAirframeConsequence consequence, AirframeStoreRecord expected,
        DateTimeOffset appliedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consequence); ArgumentNullException.ThrowIfNull(expected);
        consequence.Validate(); expected.Validate();
        if (expected.Airframe.AirframeId != consequence.Summary.AirframeId
            || expected.Airframe.CanonicalAircraftId != consequence.Summary.CanonicalAircraftId)
            throw new InvalidOperationException("Consequence targets a different physical airframe or canonical model.");
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var retained = await ReadConsequenceAsync(connection, transaction, consequence.Summary.SessionId, cancellationToken).ConfigureAwait(false);
        if (retained is not null)
        {
            // Canonical JSON includes all retained contacts, counters, timestamps, terminal status and
            // calibration, not just the strongest contact. A different attempt cannot rewrite history.
            if (JsonSerializer.Serialize(retained.Consequence, ConsequenceJson) != JsonSerializer.Serialize(consequence, ConsequenceJson))
                throw new InvalidOperationException("This FlightSession already has different physical-airframe consequence evidence.");
            return new(retained, WasNewlyApplied: false);
        }

        var current = await ReadAsync(connection, transaction, expected.Airframe.AirframeId, cancellationToken).ConfigureAwait(false)
            ?? throw new AirframeConcurrencyException("The consequence's physical airframe no longer exists.");
        if (current != expected) throw new AirframeConcurrencyException("Airframe condition changed before consequence application; retry with current state.");
        appliedAt = appliedAt.ToUniversalTime();
        var after = consequence.ApplyCondition
            ? new AirframeStoreRecord(current.Airframe, consequence.ApplyTo(current.Condition), checked(current.Revision + 1), appliedAt)
            : current;
        var application = new FlightAirframeApplication(consequence, current, after, appliedAt);
        application.Validate();

        if (consequence.ApplyCondition)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE airframes SET wear_fraction=$wear, damage_state=$damage, revision=$revision, saved_at_utc_ticks=$saved
                WHERE airframe_id=$id AND canonical_aircraft_id=$model AND created_at_utc_ticks=$created AND revision=$expected;
                """;
            update.Parameters.AddWithValue("$wear", after.Condition.WearFraction);
            update.Parameters.AddWithValue("$damage", (int)after.Condition.Damage);
            update.Parameters.AddWithValue("$revision", after.Revision);
            update.Parameters.AddWithValue("$saved", after.SavedAt.UtcTicks);
            update.Parameters.AddWithValue("$id", after.Airframe.AirframeId.ToString());
            update.Parameters.AddWithValue("$model", after.Airframe.CanonicalAircraftId);
            update.Parameters.AddWithValue("$created", after.Airframe.CreatedAt.UtcTicks);
            update.Parameters.AddWithValue("$expected", current.Revision);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new AirframeConcurrencyException("Airframe revision changed during consequence application.");
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO flight_airframe_consequences (session_id, airframe_id, payload_schema_version, applied_at_utc_ticks, payload_json)
            VALUES ($session, $airframe, 1, $applied, $payload);
            """;
        insert.Parameters.AddWithValue("$session", consequence.Summary.SessionId.ToString("D"));
        insert.Parameters.AddWithValue("$airframe", consequence.Summary.AirframeId.ToString());
        insert.Parameters.AddWithValue("$applied", appliedAt.UtcTicks);
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(application, ConsequenceJson));
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        _logger.LogInformation("Applied FlightSession {SessionId} consequence to airframe {AirframeId}: severity {Severity}, condition applied {ApplyCondition}, revision {BeforeRevision}->{AfterRevision}, strongest contact {ContactAt}, vertical speed {VerticalSpeedFeetPerMinute} ft/min, normal acceleration {NormalAccelerationG} G. {Rationale}",
            consequence.Summary.SessionId, consequence.Summary.AirframeId, consequence.Severity, consequence.ApplyCondition,
            current.Revision, after.Revision, consequence.StrongestContact?.Timestamp,
            consequence.StrongestContact?.VerticalSpeedFeetPerMinute, consequence.StrongestContact?.NormalAccelerationG, consequence.Rationale);
        return new(application, WasNewlyApplied: true);
    }

    private static async Task<FlightAirframeApplication?> ReadConsequenceAsync(SqliteConnection connection, SqliteTransaction? transaction,
        Guid sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT airframe_id, payload_schema_version, applied_at_utc_ticks, payload_json FROM flight_airframe_consequences WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.GetValue(0) is not string || reader.GetValue(1) is not long || reader.GetValue(2) is not long || reader.GetValue(3) is not string)
            throw new InvalidDataException("Invalid airframe consequence column types.");
        if (reader.GetInt64(1) != 1) throw new NotSupportedException("Unsupported airframe consequence schema.");
        var result = JsonSerializer.Deserialize<FlightAirframeApplication>(reader.GetString(3), ConsequenceJson)
            ?? throw new InvalidDataException("Missing airframe consequence payload.");
        result.Validate();
        if (result.Consequence.Summary.SessionId != sessionId || result.Consequence.Summary.AirframeId.ToString() != reader.GetString(0)
            || result.AppliedAt.UtcTicks != reader.GetInt64(2))
            throw new InvalidDataException("Airframe consequence metadata does not match its payload.");
        return result;
    }
}
