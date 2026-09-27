using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Persistence;

public sealed partial class SqliteAirframeStore
{
    public async Task<AirframeComponentInspectionResult> PerformVerifiedComponentInspectionAsync(
        AirframeComponentInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        request = request with { PerformedAt = request.PerformedAt.ToUniversalTime() };

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();

        // Durable replay is resolved before today's condition, usage, due state or clock can make
        // an already-committed request look stale.
        AirframeServiceEvent? retained = await ReadMaintenanceActionAsync(
                connection,
                transaction,
                request.MaintenanceActionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (retained is not null)
            return await ReplayRetainedComponentInspectionAsync(
                    connection,
                    transaction,
                    retained,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

        // Clock rollback after a successful commit must not invalidate exact replay. A fresh
        // action, however, cannot establish a service baseline in the future.
        if (request.PerformedAt > _clock.GetUtcNow())
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Component inspection time cannot be in the future.");

        AirframeStoreRecord? condition = await ReadAsync(
                connection,
                transaction,
                request.AirframeId,
                cancellationToken)
            .ConfigureAwait(false);
        if (condition is null)
            return new(
                AirframeComponentInspectionResultStatus.AirframeNotFound,
                null,
                null,
                null,
                null,
                WasNewlyApplied: false);

        AirframeMaintenanceScheduleEvidenceRecord? immutableEvidence =
            await ReadScheduleEvidenceByIdAsync(
                    connection,
                    transaction,
                    request.EvidenceId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (immutableEvidence is null)
            return new(
                AirframeComponentInspectionResultStatus.ScheduleEvidenceNotFound,
                condition,
                null,
                null,
                null,
                WasNewlyApplied: false);

        AirframeServiceState authoritativeService = await ReadServiceStateAsync(
                connection,
                transaction,
                request.AirframeId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Physical airframe has no authoritative service state.");
        ImmutableArray<ComponentServiceBaselineRevision> retainedBaselines =
            await ReadAndValidateComponentServiceBaselineChainAsync(
                    connection,
                    transaction,
                    immutableEvidence,
                    condition.Airframe,
                    authoritativeService,
                    cancellationToken)
                .ConfigureAwait(false);
        AirframeMaintenanceScheduleEvidenceRecord scheduleBefore = retainedBaselines[^1].Record;

        immutableEvidence.Validate(condition.Airframe, authoritativeService);
        scheduleBefore.Validate(condition.Airframe, authoritativeService);
        if (immutableEvidence.EvidenceId != scheduleBefore.EvidenceId
            || immutableEvidence.Applicability != scheduleBefore.Applicability
            || immutableEvidence.PayloadSchemaVersion != scheduleBefore.PayloadSchemaVersion
            || immutableEvidence.Baseline.AirframeId != scheduleBefore.Baseline.AirframeId
            || immutableEvidence.Baseline.ScheduleId != scheduleBefore.Baseline.ScheduleId
            || immutableEvidence.Baseline.ScheduleVersion != scheduleBefore.Baseline.ScheduleVersion
            || immutableEvidence.Baseline.Component != scheduleBefore.Baseline.Component
            || immutableEvidence.Baseline.ApplicabilityRevision
                != scheduleBefore.Baseline.ApplicabilityRevision
            || scheduleBefore.Baseline.Revision < immutableEvidence.Baseline.Revision)
            throw new InvalidDataException(
                "Current component service baseline does not match immutable schedule evidence.");

        request.ValidateBefore(condition, authoritativeService, scheduleBefore);
        AirframeMaintenanceScheduleAssessment assessment = AirframeMaintenanceScheduleCatalog
            .AssessCandidates(condition.Airframe, authoritativeService, [scheduleBefore])
            .Single(candidate => candidate.Definition.ScheduleId == request.ScheduleId
                && candidate.Definition.Version == request.ScheduleVersion);
        if (assessment.Status != AirframeMaintenanceScheduleStatus.TrackedProxyDue)
            return new(
                AirframeComponentInspectionResultStatus.InspectionNotDue,
                condition,
                authoritativeService,
                scheduleBefore,
                null,
                WasNewlyApplied: false);

        await EnsureAfterLatestComponentServiceAsync(
                connection,
                transaction,
                request.AirframeId,
                request.PerformedAt,
                cancellationToken)
            .ConfigureAwait(false);

        AirframeMaintenanceServiceBaseline baselineAfter = scheduleBefore.Baseline.Advance(
            authoritativeService,
            request.ServiceRecordReference,
            request.PerformedAt);
        var scheduleAfter = scheduleBefore with { Baseline = baselineAfter };
        var serviceEvent = new AirframeComponentInspectionEvent(
            request,
            AirframeMaintenanceEventKind.VerifiedComponentInspection,
            condition,
            condition,
            AirframeComponentInspectionEvent.InspectionRationale,
            authoritativeService,
            scheduleBefore,
            scheduleAfter);
        serviceEvent.Validate();

        // The event is inserted first because the append-only baseline revision references its
        // global action identity. Both writes remain in this transaction and roll back together.
        try
        {
            await InsertMaintenanceEventAsync(connection, transaction, serviceEvent, cancellationToken)
                .ConfigureAwait(false);
            await InsertComponentServiceBaselineAsync(
                    connection,
                    transaction,
                    scheduleAfter,
                    request.MaintenanceActionId,
                    cancellationToken)
                .ConfigureAwait(false);
            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6
            || ex.SqliteExtendedErrorCode is 1555 or 2067)
        {
            transaction.Rollback();
            using SqliteTransaction replayTransaction = connection.BeginTransaction();
            AirframeServiceEvent? racedReplay = await ReadMaintenanceActionAsync(
                    connection,
                    replayTransaction,
                    request.MaintenanceActionId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (racedReplay is not null)
            {
                AirframeComponentInspectionResult replay =
                    await ReplayRetainedComponentInspectionAsync(
                            connection,
                            replayTransaction,
                            racedReplay,
                            request,
                            cancellationToken)
                        .ConfigureAwait(false);
                replayTransaction.Commit();
                return replay;
            }
            throw new AirframeConcurrencyException(
                "Component baseline changed while the verified inspection was being committed.");
        }
        _logger.LogInformation(
            "Completed verified component inspection for airframe {AirframeId}, component {Component}, schedule {ScheduleId}/{ScheduleVersion}, action {MaintenanceActionId}, baseline revision {BeforeRevision}->{AfterRevision}, source service revision {ServiceRevision}; condition and generic inspection state unchanged.",
            request.AirframeId,
            request.Component,
            request.ScheduleId,
            request.ScheduleVersion,
            request.MaintenanceActionId,
            scheduleBefore.Baseline.Revision,
            scheduleAfter.Baseline.Revision,
            authoritativeService.Revision);
        return new(
            AirframeComponentInspectionResultStatus.Inspected,
            condition,
            authoritativeService,
            scheduleAfter,
            serviceEvent,
            WasNewlyApplied: true);
    }

    private static async Task<AirframeComponentInspectionResult>
        ReplayRetainedComponentInspectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AirframeServiceEvent retained,
        AirframeComponentInspectionRequest request,
        CancellationToken cancellationToken)
    {
        if (retained is not AirframeComponentInspectionEvent componentInspection)
            return retained.Replay(request);
        AirframeComponentInspectionResult replay = componentInspection.Replay(request);
        await ValidateRetainedComponentInspectionAsync(
                connection,
                transaction,
                componentInspection,
                cancellationToken)
            .ConfigureAwait(false);
        return replay;
    }

    private static async Task ValidateRetainedComponentInspectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AirframeComponentInspectionEvent retained,
        CancellationToken cancellationToken)
    {
        AirframeStoreRecord condition = await ReadAsync(
                connection,
                transaction,
                retained.AirframeId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                "Retained component inspection references a missing physical airframe.");
        if (retained.Before.Airframe != condition.Airframe
            || retained.After.Revision > condition.Revision
            || retained.After.Revision == condition.Revision && retained.After != condition)
            throw new InvalidDataException(
                "Retained component inspection condition does not match the authoritative airframe.");
        AirframeServiceState service = await ReadServiceStateAsync(
                connection,
                transaction,
                retained.AirframeId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                "Retained component inspection references missing authoritative usage.");
        if (retained.AuthoritativeServiceState.Revision > service.Revision
            || retained.AuthoritativeServiceState.Revision == service.Revision
            && retained.AuthoritativeServiceState != service)
            throw new InvalidDataException(
                "Retained component inspection usage does not match authoritative service state.");
        AirframeMaintenanceScheduleEvidenceRecord immutableEvidence =
            await ReadScheduleEvidenceByIdAsync(
                    connection,
                    transaction,
                    retained.ScheduleAfter.EvidenceId,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                "Retained component inspection references missing schedule evidence.");
        ImmutableArray<ComponentServiceBaselineRevision> baselines =
            await ReadAndValidateComponentServiceBaselineChainAsync(
                    connection,
                    transaction,
                    immutableEvidence,
                    condition.Airframe,
                    service,
                    cancellationToken)
                .ConfigureAwait(false);
        ComponentServiceBaselineRevision applied = baselines.SingleOrDefault(revision =>
                revision.MaintenanceActionId == retained.MaintenanceActionId)
            ?? throw new InvalidDataException(
                "Retained component inspection has no matching service-baseline revision.");
        if (applied.Record != retained.ScheduleAfter)
            throw new InvalidDataException(
                "Retained component inspection does not match its service-baseline revision.");
    }
}
