using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Persistence;

public sealed partial class SqliteAirframeStore : IAirframeMaintenanceStore
{
    private static readonly JsonSerializerOptions MaintenanceJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<AirframeServiceEvent?> FindMaintenanceActionAsync(Guid maintenanceActionId, CancellationToken cancellationToken = default)
    {
        if (maintenanceActionId == Guid.Empty) throw new ArgumentException("Maintenance action ID is required.", nameof(maintenanceActionId));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadMaintenanceActionAsync(connection, null, maintenanceActionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AirframeRepairResult> RepairDiscreteDamageAsync(AirframeRepairRequest request,
        AirframeStoreRecord expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(expected);
        request.Validate(); expected.Validate();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var retained = await ReadMaintenanceActionAsync(connection, transaction, request.MaintenanceActionId, cancellationToken).ConfigureAwait(false);
        if (retained is not null)
        {
            var replay = retained.Replay(request);
            if (expected != retained.Before)
                throw new InvalidOperationException("Maintenance action ID already has a different expected before state.");
            return replay;
        }

        var current = await ReadAsync(connection, transaction, request.AirframeId, cancellationToken).ConfigureAwait(false)
            ?? throw new AirframeConcurrencyException("The repair's physical airframe no longer exists.");
        if (current != expected) throw new AirframeConcurrencyException("Airframe identity/condition changed before repair.");
        request.ValidateBefore(current);
        if (!current.Condition.HasDamage) return new(AirframeRepairStatus.NoRepairRequired, current, null, false);

        request = request with { PerformedAt = request.PerformedAt.ToUniversalTime() };
        var after = new AirframeStoreRecord(current.Airframe,
            new(current.Condition.WearFraction, AirframeDamageState.None), checked(current.Revision + 1), request.PerformedAt);
        var serviceEvent = new AirframeMaintenanceEvent(request, AirframeMaintenanceEventKind.DiscreteDamageRepair,
            current, after, AirframeMaintenanceEvent.DiscreteRepairRationale);
        serviceEvent.Validate();

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        // Wear and physical identity columns are deliberately not assigned by this action.
        update.CommandText = """
            UPDATE airframes SET damage_state=$damage, revision=$revision, saved_at_utc_ticks=$saved
            WHERE airframe_id=$airframe AND canonical_aircraft_id=$model
                AND created_at_utc_ticks=$created AND revision=$expected;
            """;
        update.Parameters.AddWithValue("$damage", (int)after.Condition.Damage);
        update.Parameters.AddWithValue("$revision", after.Revision);
        update.Parameters.AddWithValue("$saved", after.SavedAt.UtcTicks);
        update.Parameters.AddWithValue("$airframe", request.AirframeId.ToString());
        update.Parameters.AddWithValue("$model", current.Airframe.CanonicalAircraftId);
        update.Parameters.AddWithValue("$created", current.Airframe.CreatedAt.UtcTicks);
        update.Parameters.AddWithValue("$expected", current.Revision);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new AirframeConcurrencyException("Airframe revision changed during repair.");

        await InsertMaintenanceEventAsync(connection, transaction, serviceEvent, cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        _logger.LogInformation("Repaired discrete damage for airframe {AirframeId}, action {MaintenanceActionId}, damage {BeforeDamage}->{AfterDamage}, revision {BeforeRevision}->{AfterRevision}; wear unchanged.",
            request.AirframeId, request.MaintenanceActionId, current.Condition.Damage, after.Condition.Damage, current.Revision, after.Revision);
        return new(AirframeRepairStatus.Repaired, after, serviceEvent, WasNewlyApplied: true);
    }

    private static async Task InsertMaintenanceEventAsync(SqliteConnection connection, SqliteTransaction transaction,
        AirframeServiceEvent serviceEvent, CancellationToken cancellationToken)
    {
        serviceEvent.Validate();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO airframe_maintenance_events
                (maintenance_action_id, airframe_id, event_kind, performed_at_utc_ticks, payload_schema_version, payload_json)
            VALUES ($action, $airframe, $kind, $performed, $schema, $payload);
            """;
        insert.Parameters.AddWithValue("$action", serviceEvent.MaintenanceActionId.ToString("D"));
        insert.Parameters.AddWithValue("$airframe", serviceEvent.AirframeId.ToString());
        insert.Parameters.AddWithValue("$kind", (int)serviceEvent.Kind);
        insert.Parameters.AddWithValue("$performed", serviceEvent.PerformedAt.UtcTicks);
        insert.Parameters.AddWithValue("$schema", serviceEvent.Kind == AirframeMaintenanceEventKind.DiscreteDamageRepair ? 1 : 3);
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(serviceEvent, serviceEvent.GetType(), MaintenanceJson));
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AirframeServiceHistoryPage> ReadServiceHistoryAsync(AirframeServiceHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT maintenance_action_id, airframe_id, event_kind, performed_at_utc_ticks, payload_schema_version, payload_json
            FROM airframe_maintenance_events WHERE airframe_id=$airframe
            """ + (query.Before is null ? "" : " AND (performed_at_utc_ticks, maintenance_action_id) < ($before, $action)") + """
             ORDER BY performed_at_utc_ticks DESC, maintenance_action_id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$airframe", query.AirframeId.ToString());
        command.Parameters.AddWithValue("$limit", query.Limit + 1);
        if (query.Before is { } cursor)
        {
            command.Parameters.AddWithValue("$before", cursor.PerformedAt.UtcTicks);
            command.Parameters.AddWithValue("$action", cursor.MaintenanceActionId.ToString("D"));
        }
        var events = ImmutableList.CreateBuilder<AirframeServiceEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var serviceEvent = ReadMaintenanceEvent(reader);
            if (serviceEvent.AirframeId != query.AirframeId)
                throw new InvalidDataException("Service history belongs to a different physical airframe.");
            events.Add(serviceEvent);
        }
        AirframeServiceHistoryCursor? next = null;
        if (events.Count > query.Limit)
        {
            events.RemoveAt(query.Limit);
            var last = events[^1];
            next = new(last.PerformedAt, last.MaintenanceActionId);
        }
        return new(events.ToImmutable(), next);
    }

    private static async Task<AirframeServiceEvent?> ReadMaintenanceActionAsync(SqliteConnection connection,
        SqliteTransaction? transaction, Guid actionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT maintenance_action_id, airframe_id, event_kind, performed_at_utc_ticks, payload_schema_version, payload_json
            FROM airframe_maintenance_events WHERE maintenance_action_id=$action;
            """;
        command.Parameters.AddWithValue("$action", actionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var serviceEvent = ReadMaintenanceEvent(reader);
        if (serviceEvent.MaintenanceActionId != actionId)
            throw new InvalidDataException("Retained service event has a different action identity.");
        return serviceEvent;
    }

    private static AirframeServiceEvent ReadMaintenanceEvent(SqliteDataReader reader)
    {
        if (reader.GetValue(0) is not string || reader.GetValue(1) is not string || reader.GetValue(2) is not long
            || reader.GetValue(3) is not long || reader.GetValue(4) is not long || reader.GetValue(5) is not string)
            throw new InvalidDataException("Invalid maintenance event column types.");
        AirframeServiceEvent result = (reader.GetInt64(2), reader.GetInt64(4)) switch
        {
            (1, 1) => JsonSerializer.Deserialize<AirframeMaintenanceEvent>(reader.GetString(5), MaintenanceJson)
                ?? throw new InvalidDataException("Missing repair event payload."),
            (2, 2) => DeserializeLegacyInspection(reader.GetString(5)),
            (2, 3) => DeserializeCurrentInspection(reader.GetString(5)),
            _ => throw new NotSupportedException("Unsupported maintenance event kind/payload schema.")
        };
        result.Validate();
        if (result.MaintenanceActionId.ToString("D") != reader.GetString(0) || result.AirframeId.ToString() != reader.GetString(1)
            || (int)result.Kind != reader.GetInt64(2) || result.PerformedAt.UtcTicks != reader.GetInt64(3))
            throw new InvalidDataException("Maintenance event metadata does not match retained payload.");
        return result;
    }

    private static AirframeRoutineInspectionEvent DeserializeLegacyInspection(string payload)
    {
        var result = JsonSerializer.Deserialize<AirframeRoutineInspectionEvent>(payload, MaintenanceJson)
            ?? throw new InvalidDataException("Missing inspection event payload.");
        if (result.ServiceBefore is null || result.ServiceAfter is null)
            throw new InvalidDataException("Legacy inspection payload is missing service-state evidence.");
        return result with
        {
            ServiceBefore = result.ServiceBefore with
            {
                TotalTrackedLandingCycles = 0,
                LandingCycleOrigin = AirframeUsageOrigin.TrackingFromMigrationBaseline
            },
            ServiceAfter = result.ServiceAfter with
            {
                TotalTrackedLandingCycles = 0,
                LandingCycleOrigin = AirframeUsageOrigin.TrackingFromMigrationBaseline
            }
        };
    }

    private static AirframeRoutineInspectionEvent DeserializeCurrentInspection(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (!HasCurrentLandingCycleEvidence(document.RootElement, "serviceBefore")
            || !HasCurrentLandingCycleEvidence(document.RootElement, "serviceAfter"))
            throw new InvalidDataException("Current inspection payload is missing required landing-cycle evidence.");
        return JsonSerializer.Deserialize<AirframeRoutineInspectionEvent>(payload, MaintenanceJson)
            ?? throw new InvalidDataException("Missing inspection event payload.");
    }

    private static bool HasCurrentLandingCycleEvidence(JsonElement root, string serviceProperty) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(serviceProperty, out var service)
        && service.ValueKind == JsonValueKind.Object
        && service.TryGetProperty("totalTrackedLandingCycles", out _)
        && service.TryGetProperty("landingCycleOrigin", out _);
}
