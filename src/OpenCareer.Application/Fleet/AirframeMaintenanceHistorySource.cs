using System.Collections.Immutable;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Fleet;

/// <summary>Condition and inspection description only; AvailableForDispatch does not replace other dispatch gates.</summary>
public enum AirframeServiceability { AvailableForDispatch, Grounded, InspectionDue }
public enum AirframeMaintenanceReadStatus { Available, NotFound }

/// <summary>Projection of the retained application, never a decision made using today's calibration.</summary>
public sealed record AirframeMaintenanceHistoryEntry(FlightAirframeApplication Application)
{
    public Guid SessionId => Application.Consequence.Summary.SessionId;
    public Guid? ContractId => Application.Consequence.Summary.ContractId;
    public AirframeId AirframeId => Application.Consequence.Summary.AirframeId;
    public DateTimeOffset OccurredAt => Application.Consequence.Summary.EndedAt;
    public DateTimeOffset AppliedAt => Application.AppliedAt;
    public FlightSessionStatus TerminalStatus => Application.Consequence.Summary.TerminalStatus;
    public FlightDamageSeverity? Severity => Application.Consequence.Severity;
    public string Rationale => Application.Consequence.Rationale;
    public FlightLandingContactEvidence? StrongestContact => Application.Consequence.StrongestContact;
    public int BounceCount => Application.Consequence.Summary.BounceCount;
    public TimeSpan AirborneTime => Application.Consequence.Summary.AirborneTime;
    public AirframeCondition ConditionBefore => Application.Before.Condition;
    public AirframeCondition ConditionAfter => Application.After.Condition;
    public long BeforeRevision => Application.Before.Revision;
    public long AfterRevision => Application.After.Revision;
    // A revision can advance even when wear is saturated or there was no new usage/damage.
    public bool ConditionChanged => ConditionBefore != ConditionAfter;
}

public sealed record AirframeMaintenanceSnapshot(
    AirframeStoreRecord Current,
    ImmutableList<AirframeMaintenanceHistoryEntry> History,
    FlightAirframeHistoryCursor? Next,
    AirframeServiceState ServiceState,
    DateTimeOffset EvaluatedAt,
    ImmutableArray<AirframeMaintenanceScheduleAssessment> VerifiedScheduleCandidates)
{
    // Uses the very same revision-checked records; no independent reliability-store reads.
    public AirframeReliabilityAssessment Reliability => AirframeReliabilityAssessment.Evaluate(Current, ServiceState, EvaluatedAt);
    public Airframe Airframe => Current.Airframe;
    public AirframeCondition Condition => Current.Condition;
    public long Revision => Current.Revision;
    public DateTimeOffset SavedAt => Current.SavedAt;
    public AirframeServiceability Serviceability => Condition.RequiresGrounding
        ? AirframeServiceability.Grounded : InspectionStatus == AirframeInspectionStatus.InspectionDue
            ? AirframeServiceability.InspectionDue : AirframeServiceability.AvailableForDispatch;
    public AirframeInspectionStatus InspectionStatus => ServiceState.InspectionStatus;
    public double TotalTrackedAirborneHours => ServiceState.TotalTrackedAirborneTime.TotalHours;
    public long TotalTrackedLandingCycles => ServiceState.TotalTrackedLandingCycles;
    public AirframeUsageOrigin LandingCycleOrigin => ServiceState.LandingCycleOrigin;
    public double HoursUntilInspection => ServiceState.TimeUntilInspection.TotalHours;
    public string ScheduleId => ServiceState.ScheduleId;
    public int ScheduleVersion => ServiceState.ScheduleVersion;
}

public sealed record AirframeMaintenanceReadResult(
    AirframeId AirframeId,
    AirframeMaintenanceReadStatus Status,
    AirframeMaintenanceSnapshot? Snapshot);

/// <summary>Read-only condition/history authority for an explicitly requested physical airframe.</summary>
public sealed class AirframeMaintenanceHistorySource(IAirframeStore airframes, IFlightAirframeConsequenceStore consequences,
    IAirframeMaintenanceStore? maintenance = null, IAirframeServiceStateStore? serviceStates = null, TimeProvider? clock = null,
    IAirframeMaintenanceScheduleEvidenceStore? scheduleEvidence = null)
{
    public async Task<AirframeMaintenanceReadResult> ReadAsync(
        FlightAirframeHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var current = await airframes.FindAsync(query.AirframeId, cancellationToken).ConfigureAwait(false);
        if (current is null) return new(query.AirframeId, AirframeMaintenanceReadStatus.NotFound, null);
        current.Validate();
        if (current.Airframe.AirframeId != query.AirframeId)
            throw new InvalidDataException("Maintenance snapshot returned a different physical airframe.");

        var evidence = await ReadScheduleEvidenceAsync(current, cancellationToken).ConfigureAwait(false);
        var service = await ReadServiceAsync(current, cancellationToken).ConfigureAwait(false);
        ValidateScheduleEvidence(current, service, evidence);
        var page = await consequences.ReadHistoryAsync(query, cancellationToken).ConfigureAwait(false);
        // The stores need not share a transaction. Fail closed rather than combine an older
        // condition with history written concurrently; the caller can request a fresh snapshot.
        var finalCurrent = await airframes.FindAsync(query.AirframeId, cancellationToken).ConfigureAwait(false);
        if (finalCurrent is null) throw new AirframeConcurrencyException("Airframe was removed during maintenance read; refresh the snapshot.");
        finalCurrent.Validate();
        var finalEvidence = await ReadScheduleEvidenceAsync(finalCurrent, cancellationToken).ConfigureAwait(false);
        var finalService = await ReadServiceAsync(finalCurrent, cancellationToken).ConfigureAwait(false);
        ValidateScheduleEvidence(finalCurrent, finalService, finalEvidence);
        if (!evidence.SequenceEqual(finalEvidence) || service != finalService || current != finalCurrent)
            throw new AirframeConcurrencyException("Airframe condition/service/evidence state changed during maintenance read; refresh the snapshot.");

        foreach (var application in page.Entries)
        {
            application.Validate();
            if (application.Before.Airframe != current.Airframe
                || application.After.Revision > current.Revision
                || application.After.Revision == current.Revision && application.After != current)
                throw new InvalidDataException("Retained maintenance history does not match the current physical airframe/model/revision.");
        }
        ImmutableArray<AirframeMaintenanceScheduleAssessment> assessments =
            AirframeMaintenanceScheduleCatalog.AssessCandidates(current.Airframe, service, evidence);
        return new(query.AirframeId, AirframeMaintenanceReadStatus.Available,
            new(current, page.Entries.Select(a => new AirframeMaintenanceHistoryEntry(a)).ToImmutableList(), page.Next,
                service, (clock ?? TimeProvider.System).GetUtcNow(), assessments));
    }

    public async Task<AirframeServiceHistoryReadResult> ReadServiceHistoryAsync(
        AirframeServiceHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var current = await airframes.FindAsync(query.AirframeId, cancellationToken).ConfigureAwait(false);
        if (current is null) return new(query.AirframeId, AirframeMaintenanceReadStatus.NotFound, null);
        current.Validate();
        if (current.Airframe.AirframeId != query.AirframeId)
            throw new InvalidDataException("Service history returned a different physical airframe.");
        if (maintenance is null) throw new InvalidOperationException("Authoritative maintenance history store is required.");
        var evidence = await ReadScheduleEvidenceAsync(current, cancellationToken).ConfigureAwait(false);
        var service = await ReadServiceAsync(current, cancellationToken).ConfigureAwait(false);
        ValidateScheduleEvidence(current, service, evidence);
        var history = await maintenance.ReadServiceHistoryAsync(query, cancellationToken).ConfigureAwait(false);
        var finalCurrent = await airframes.FindAsync(query.AirframeId, cancellationToken).ConfigureAwait(false);
        if (finalCurrent is null)
            throw new AirframeConcurrencyException(
                "Airframe was removed during service history read; refresh the snapshot.");
        var finalEvidence = await ReadScheduleEvidenceAsync(finalCurrent, cancellationToken).ConfigureAwait(false);
        var finalService = await ReadServiceAsync(finalCurrent, cancellationToken).ConfigureAwait(false);
        ValidateScheduleEvidence(finalCurrent, finalService, finalEvidence);
        if (service != finalService || current != finalCurrent || !evidence.SequenceEqual(finalEvidence))
            throw new AirframeConcurrencyException(
                "Airframe condition/service/component baseline changed during service history read; refresh the snapshot.");
        foreach (var serviceEvent in history.Events)
        {
            serviceEvent.Validate();
            if (serviceEvent.Before.Airframe != current.Airframe || serviceEvent.After.Revision > current.Revision
                || serviceEvent.After.Revision == current.Revision && serviceEvent.After != current)
                throw new InvalidDataException("Retained service event does not match the current physical airframe/model/revision.");
            if (serviceEvent is AirframeRoutineInspectionEvent inspection
                && (inspection.ServiceAfter.Revision > service.Revision
                    || inspection.ServiceAfter.Revision == service.Revision && inspection.ServiceAfter != service))
                throw new InvalidDataException("Retained inspection does not match the current service revision/state.");
            if (serviceEvent is AirframeComponentInspectionEvent componentInspection)
            {
                AirframeMaintenanceScheduleEvidenceRecord retained = evidence.SingleOrDefault(record =>
                        record.EvidenceId == componentInspection.ScheduleAfter.EvidenceId)
                    ?? throw new InvalidDataException(
                        "Retained component inspection has no current schedule evidence.");
                if (componentInspection.ScheduleAfter.Applicability != retained.Applicability
                    || componentInspection.ScheduleAfter.Baseline.Revision > retained.Baseline.Revision
                    || componentInspection.ScheduleAfter.Baseline.Revision == retained.Baseline.Revision
                    && componentInspection.ScheduleAfter != retained
                    || componentInspection.AuthoritativeServiceState.Revision > service.Revision
                    || componentInspection.AuthoritativeServiceState.Revision == service.Revision
                    && componentInspection.AuthoritativeServiceState != service)
                    throw new InvalidDataException(
                        "Retained component inspection does not match current schedule/service evidence.");
            }
        }
        return new(query.AirframeId, AirframeMaintenanceReadStatus.Available, new(current, history, service));
    }

    private async Task<AirframeServiceState> ReadServiceAsync(AirframeStoreRecord current, CancellationToken cancellationToken)
    {
        var store = serviceStates ?? airframes as IAirframeServiceStateStore
            ?? throw new InvalidOperationException("Authoritative airframe service state is required.");
        var service = await store.ReadServiceStateAsync(current.Airframe.AirframeId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Physical airframe has no authoritative service state.");
        service.Validate();
        if (service.AirframeId != current.Airframe.AirframeId || service.UpdatedAt < current.Airframe.CreatedAt)
            throw new InvalidDataException("Service state does not match the requested physical airframe.");
        return service;
    }

    private async Task<ImmutableArray<AirframeMaintenanceScheduleEvidenceRecord>> ReadScheduleEvidenceAsync(
        AirframeStoreRecord current,
        CancellationToken cancellationToken)
    {
        var store = scheduleEvidence ?? airframes as IAirframeMaintenanceScheduleEvidenceStore;
        if (store is null) return [];
        ImmutableArray<AirframeMaintenanceScheduleEvidenceRecord> evidence =
            await store.ReadForAirframeAsync(current.Airframe.AirframeId, cancellationToken).ConfigureAwait(false);
        if (evidence.IsDefault)
            throw new InvalidDataException("Maintenance schedule evidence store returned an uninitialized result.");
        return evidence
            .OrderBy(record => record.Baseline.ScheduleId, StringComparer.Ordinal)
            .ThenBy(record => record.Baseline.ScheduleVersion)
            .ThenBy(record => record.EvidenceId)
            .ToImmutableArray();
    }

    private static void ValidateScheduleEvidence(
        AirframeStoreRecord current,
        AirframeServiceState service,
        ImmutableArray<AirframeMaintenanceScheduleEvidenceRecord> evidence)
    {
        foreach (AirframeMaintenanceScheduleEvidenceRecord record in evidence)
            record.Validate(current.Airframe, service);
    }
}
