using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Persistence;

/// <summary>
/// Immutable physical-airframe configuration evidence and its captured service baseline.
/// Nothing in this store derives component identity from simulator identity or canonical model.
/// </summary>
public sealed partial class SqliteAirframeStore : IAirframeMaintenanceScheduleEvidenceStore
{
    private const int ScheduleEvidencePayloadSchemaVersion = 1;
    private static readonly JsonSerializerOptions ScheduleEvidenceJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<AirframeMaintenanceScheduleEvidenceResult> RegisterAsync(
        AirframeMaintenanceScheduleEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        request = request with { RecordedAt = request.RecordedAt.ToUniversalTime() };

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();

        AirframeMaintenanceScheduleEvidenceRecord? retained =
            await ReadScheduleEvidenceByIdAsync(connection, transaction, request.EvidenceId, cancellationToken)
                .ConfigureAwait(false);
        if (retained is not null)
        {
            await ValidateRetainedScheduleEvidenceAsync(connection, transaction, retained, cancellationToken)
                .ConfigureAwait(false);
            if (!request.Matches(retained))
                throw new InvalidOperationException("Maintenance schedule evidence ID is already bound to different evidence.");
            return new(AirframeMaintenanceScheduleEvidenceStatus.Registered, retained, WasNewlyApplied: false);
        }

        // A clock correction after a successful commit must not invalidate an identical durable
        // replay. Only a new event is subject to the current-clock admission check.
        if (request.RecordedAt > _clock.GetUtcNow())
            throw new ArgumentOutOfRangeException(nameof(request), "Evidence time cannot be in the future.");

        AirframeStoreRecord? airframe = await ReadAsync(
                connection,
                transaction,
                request.AirframeId,
                cancellationToken)
            .ConfigureAwait(false);
        if (airframe is null)
            return new(AirframeMaintenanceScheduleEvidenceStatus.NotFound, null, WasNewlyApplied: false);

        AirframeServiceState serviceState = await ReadServiceStateAsync(
                connection,
                transaction,
                request.AirframeId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Physical airframe has no authoritative service state.");

        AirframeMaintenanceScheduleDefinition definition = AirframeMaintenanceScheduleCatalog
            .FindCandidates(airframe.Airframe.CanonicalAircraftId)
            .SingleOrDefault(candidate =>
                candidate.ScheduleId == request.ScheduleId
                && candidate.Version == request.ScheduleVersion)
            ?? throw new NotSupportedException("The requested verified maintenance schedule does not apply to this physical airframe model.");
        if (definition.Component != request.Component)
            throw new InvalidDataException("Maintenance component does not match the requested verified schedule.");
        if (serviceState.Revision != request.ExpectedServiceRevision)
            throw new AirframeConcurrencyException("Airframe service revision changed before schedule evidence registration.");
        if (request.RecordedAt < airframe.Airframe.CreatedAt || request.RecordedAt < serviceState.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(request), "Evidence time cannot predate the retained physical-airframe service baseline.");

        var applicability = new AirframeComponentApplicabilityEvidence(
            request.AirframeId,
            airframe.Airframe.CanonicalAircraftId,
            request.Component,
            request.CertifiedAircraftModel,
            request.InstalledComponentModel,
            request.Authority,
            request.SourceReference,
            Revision: 1,
            RecordedAt: request.RecordedAt);
        var baseline = new AirframeMaintenanceServiceBaseline(
            request.AirframeId,
            request.ScheduleId,
            request.ScheduleVersion,
            request.Component,
            ApplicabilityRevision: applicability.Revision,
            TrackedAirborneTime: serviceState.TotalTrackedAirborneTime,
            TrackedLandingCycles: serviceState.TotalTrackedLandingCycles,
            AirborneTimeOrigin: serviceState.UsageOrigin,
            LandingCycleOrigin: serviceState.LandingCycleOrigin,
            Authority: request.BaselineAuthority,
            SourceReference: request.BaselineSourceReference,
            SourceServiceRevision: serviceState.Revision,
            SourceServiceUpdatedAt: serviceState.UpdatedAt,
            Revision: 1,
            EstablishedAt: request.RecordedAt);
        var record = new AirframeMaintenanceScheduleEvidenceRecord(
            request.EvidenceId,
            applicability,
            baseline,
            ScheduleEvidencePayloadSchemaVersion);
        record.Validate(airframe.Airframe, serviceState);

        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO airframe_maintenance_schedule_evidence (
                evidence_id, airframe_id, canonical_aircraft_id, component,
                certified_aircraft_model, installed_component_model,
                applicability_authority, source_reference, applicability_revision,
                schedule_id, schedule_version, baseline_airborne_ticks,
                baseline_landing_cycles, airborne_time_origin, landing_cycle_origin,
                baseline_authority, baseline_source_reference,
                source_service_revision, source_service_updated_at_utc_ticks,
                baseline_revision, recorded_at_utc_ticks, established_at_utc_ticks,
                payload_schema_version, payload_json)
            VALUES ($evidence, $airframe, $aircraft, $component,
                $certified, $installed, $authority, $source, $applicabilityRevision,
                $schedule, $version, $airborne, $cycles, $airborneOrigin, $cycleOrigin,
                $baselineAuthority, $baselineSource,
                $serviceRevision, $serviceUpdated, $baselineRevision, $recorded, $established,
                $schema, $payload)
            ON CONFLICT DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$evidence", record.EvidenceId.ToString("D"));
        insert.Parameters.AddWithValue("$airframe", record.Applicability.AirframeId.ToString());
        insert.Parameters.AddWithValue("$aircraft", record.Applicability.CanonicalAircraftId);
        insert.Parameters.AddWithValue("$component", (int)record.Applicability.Component);
        insert.Parameters.AddWithValue("$certified", record.Applicability.CertifiedAircraftModel);
        insert.Parameters.AddWithValue("$installed", record.Applicability.InstalledComponentModel);
        insert.Parameters.AddWithValue("$authority", (int)record.Applicability.Authority);
        insert.Parameters.AddWithValue("$source", record.Applicability.SourceReference);
        insert.Parameters.AddWithValue("$applicabilityRevision", record.Applicability.Revision);
        insert.Parameters.AddWithValue("$schedule", record.Baseline.ScheduleId);
        insert.Parameters.AddWithValue("$version", record.Baseline.ScheduleVersion);
        insert.Parameters.AddWithValue("$airborne", record.Baseline.TrackedAirborneTime.Ticks);
        insert.Parameters.AddWithValue("$cycles", record.Baseline.TrackedLandingCycles);
        insert.Parameters.AddWithValue("$airborneOrigin", (int)record.Baseline.AirborneTimeOrigin);
        insert.Parameters.AddWithValue("$cycleOrigin", (int)record.Baseline.LandingCycleOrigin);
        insert.Parameters.AddWithValue("$baselineAuthority", (int)record.Baseline.Authority);
        insert.Parameters.AddWithValue("$baselineSource", record.Baseline.SourceReference);
        insert.Parameters.AddWithValue("$serviceRevision", record.Baseline.SourceServiceRevision);
        insert.Parameters.AddWithValue("$serviceUpdated", record.Baseline.SourceServiceUpdatedAt.UtcTicks);
        insert.Parameters.AddWithValue("$baselineRevision", record.Baseline.Revision);
        insert.Parameters.AddWithValue("$recorded", record.Applicability.RecordedAt.UtcTicks);
        insert.Parameters.AddWithValue("$established", record.Baseline.EstablishedAt.UtcTicks);
        insert.Parameters.AddWithValue("$schema", record.PayloadSchemaVersion);
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(record, ScheduleEvidenceJson));

        if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            AirframeMaintenanceScheduleEvidenceRecord? conflict =
                await ReadScheduleEvidenceByIdAsync(connection, transaction, request.EvidenceId, cancellationToken)
                    .ConfigureAwait(false)
                ?? await ReadScheduleEvidenceByScheduleAsync(
                        connection,
                        transaction,
                        request.AirframeId,
                        request.ScheduleId,
                        request.ScheduleVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (conflict is null)
                throw new InvalidOperationException("Physical airframe already has different evidence for this verified schedule.");
            await ValidateRetainedScheduleEvidenceAsync(connection, transaction, conflict, cancellationToken)
                .ConfigureAwait(false);
            if (!request.Matches(conflict))
                throw new InvalidOperationException("Physical airframe already has different evidence for this verified schedule.");
            return new(AirframeMaintenanceScheduleEvidenceStatus.Registered, conflict, WasNewlyApplied: false);
        }

        transaction.Commit();
        _logger.LogInformation(
            "Registered component applicability and service baseline {EvidenceId} for airframe {AirframeId}, component {Component}, schedule {ScheduleId}/{ScheduleVersion}, service revision {ServiceRevision}.",
            record.EvidenceId,
            record.Applicability.AirframeId,
            record.Applicability.Component,
            record.Baseline.ScheduleId,
            record.Baseline.ScheduleVersion,
            record.Baseline.SourceServiceRevision);
        return new(AirframeMaintenanceScheduleEvidenceStatus.Registered, record, WasNewlyApplied: true);
    }

    public async Task<ImmutableArray<AirframeMaintenanceScheduleEvidenceRecord>> ReadForAirframeAsync(
        AirframeId airframeId,
        CancellationToken cancellationToken = default)
    {
        airframeId.Validate();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();

        AirframeStoreRecord? airframe = await ReadAsync(connection, transaction, airframeId, cancellationToken)
            .ConfigureAwait(false);
        if (airframe is null)
            return ImmutableArray<AirframeMaintenanceScheduleEvidenceRecord>.Empty;
        AirframeServiceState serviceState = await ReadServiceStateAsync(connection, transaction, airframeId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Physical airframe has no authoritative service state.");

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ScheduleEvidenceSelect + """
            WHERE airframe_id = $airframe
            ORDER BY component, schedule_id, schedule_version, evidence_id;
            """;
        command.Parameters.AddWithValue("$airframe", airframeId.ToString());
        var results = ImmutableArray.CreateBuilder<AirframeMaintenanceScheduleEvidenceRecord>();
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                AirframeMaintenanceScheduleEvidenceRecord record = ReadScheduleEvidence(reader);
                if (record.Applicability.AirframeId != airframeId)
                    throw new InvalidDataException("Maintenance schedule evidence belongs to a different physical airframe.");
                record.Validate(airframe.Airframe, serviceState);
                results.Add(record);
            }
        }
        transaction.Commit();
        return results.ToImmutable();
    }

    private const string ScheduleEvidenceSelect = """
        SELECT evidence_id, airframe_id, canonical_aircraft_id, component,
               certified_aircraft_model, installed_component_model,
               applicability_authority, source_reference, applicability_revision,
               schedule_id, schedule_version, baseline_airborne_ticks,
               baseline_landing_cycles, airborne_time_origin, landing_cycle_origin,
               baseline_authority, baseline_source_reference,
               source_service_revision, source_service_updated_at_utc_ticks,
               baseline_revision, recorded_at_utc_ticks, established_at_utc_ticks,
               payload_schema_version, payload_json
        FROM airframe_maintenance_schedule_evidence
        """;

    private static async Task<AirframeMaintenanceScheduleEvidenceRecord?> ReadScheduleEvidenceByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ScheduleEvidenceSelect + " WHERE evidence_id = $evidence;";
        command.Parameters.AddWithValue("$evidence", evidenceId.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadScheduleEvidence(reader) : null;
    }

    private static async Task<AirframeMaintenanceScheduleEvidenceRecord?> ReadScheduleEvidenceByScheduleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AirframeId airframeId,
        string scheduleId,
        int scheduleVersion,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ScheduleEvidenceSelect + """
            WHERE airframe_id = $airframe AND schedule_id = $schedule AND schedule_version = $version;
            """;
        command.Parameters.AddWithValue("$airframe", airframeId.ToString());
        command.Parameters.AddWithValue("$schedule", scheduleId);
        command.Parameters.AddWithValue("$version", scheduleVersion);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadScheduleEvidence(reader) : null;
    }

    private static async Task ValidateRetainedScheduleEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AirframeMaintenanceScheduleEvidenceRecord record,
        CancellationToken cancellationToken)
    {
        AirframeStoreRecord airframe = await ReadAsync(
                connection,
                transaction,
                record.Applicability.AirframeId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Maintenance schedule evidence references a missing physical airframe.");
        AirframeServiceState serviceState = await ReadServiceStateAsync(
                connection,
                transaction,
                record.Applicability.AirframeId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Maintenance schedule evidence references missing service state.");
        record.Validate(airframe.Airframe, serviceState);
    }

    private static AirframeMaintenanceScheduleEvidenceRecord ReadScheduleEvidence(SqliteDataReader reader)
    {
        if (reader.GetValue(0) is not string || reader.GetValue(1) is not string
            || reader.GetValue(2) is not string || reader.GetValue(3) is not long
            || reader.GetValue(4) is not string || reader.GetValue(5) is not string
            || reader.GetValue(6) is not long || reader.GetValue(7) is not string
            || reader.GetValue(8) is not long || reader.GetValue(9) is not string
            || Enumerable.Range(10, 6).Any(index => reader.GetValue(index) is not long)
            || reader.GetValue(16) is not string
            || Enumerable.Range(17, 6).Any(index => reader.GetValue(index) is not long)
            || reader.GetValue(23) is not string)
            throw new InvalidDataException("Invalid maintenance schedule evidence column types.");
        if (reader.GetInt64(22) != ScheduleEvidencePayloadSchemaVersion)
            throw new NotSupportedException("Unsupported maintenance schedule evidence payload schema.");

        AirframeMaintenanceScheduleEvidenceRecord record;
        try
        {
            record = JsonSerializer.Deserialize<AirframeMaintenanceScheduleEvidenceRecord>(
                         reader.GetString(23), ScheduleEvidenceJson)
                     ?? throw new InvalidDataException("Missing maintenance schedule evidence payload.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed maintenance schedule evidence payload.", ex);
        }

        if (record.Applicability is null || record.Baseline is null)
            throw new InvalidDataException("Maintenance schedule evidence payload is incomplete.");

        if (record.EvidenceId.ToString("D") != reader.GetString(0)
            || record.Applicability.AirframeId.ToString() != reader.GetString(1)
            || record.Applicability.CanonicalAircraftId != reader.GetString(2)
            || (int)record.Applicability.Component != reader.GetInt64(3)
            || record.Applicability.CertifiedAircraftModel != reader.GetString(4)
            || record.Applicability.InstalledComponentModel != reader.GetString(5)
            || (int)record.Applicability.Authority != reader.GetInt64(6)
            || record.Applicability.SourceReference != reader.GetString(7)
            || record.Applicability.Revision != reader.GetInt64(8)
            || record.Baseline.ScheduleId != reader.GetString(9)
            || record.Baseline.ScheduleVersion != reader.GetInt64(10)
            || record.Baseline.TrackedAirborneTime.Ticks != reader.GetInt64(11)
            || record.Baseline.TrackedLandingCycles != reader.GetInt64(12)
            || (int)record.Baseline.AirborneTimeOrigin != reader.GetInt64(13)
            || (int)record.Baseline.LandingCycleOrigin != reader.GetInt64(14)
            || (int)record.Baseline.Authority != reader.GetInt64(15)
            || record.Baseline.SourceReference != reader.GetString(16)
            || record.Baseline.SourceServiceRevision != reader.GetInt64(17)
            || record.Baseline.SourceServiceUpdatedAt.UtcTicks != reader.GetInt64(18)
            || record.Baseline.Revision != reader.GetInt64(19)
            || record.Applicability.RecordedAt.UtcTicks != reader.GetInt64(20)
            || record.Baseline.EstablishedAt.UtcTicks != reader.GetInt64(21)
            || record.PayloadSchemaVersion != reader.GetInt64(22))
            throw new InvalidDataException("Maintenance schedule evidence metadata does not match its payload.");
        return record;
    }
}
