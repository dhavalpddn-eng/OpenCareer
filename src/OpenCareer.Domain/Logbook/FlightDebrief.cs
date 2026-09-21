using OpenCareer.Domain.Flights;

namespace OpenCareer.Domain.Logbook;

public enum LogbookEntryKind
{
    CareerJob,
    FreeFlight,
    Training,
    Reposition,
    Other
}

public enum FlightSafetyOutcome
{
    CompletedNormally,
    CompletedWithIncident,
    DivertedSafely,
    Interrupted,
    Crashed
}

public enum MissionOutcome
{
    NotApplicable,
    Pending,
    Succeeded,
    PartiallySucceeded,
    Rerouted,
    Failed,
    Cancelled
}

public enum EvidenceQuality
{
    Observed,
    DerivedHighConfidence,
    DerivedLowConfidence,
    MissionDeclared,
    ExternalReference,
    Unavailable
}

public enum LandingOperationType
{
    Unknown,
    FullStop,
    TouchAndGo,
    StopAndGo
}

public enum DebriefEventSeverity
{
    Information,
    Advisory,
    Warning,
    Serious
}

public enum SettlementRecordStatus
{
    NotApplicable,
    Pending,
    Settled
}

public enum LogbookCommitKind
{
    AutomaticCareerSettlement,
    ManualPilotLog
}

public sealed record FlightRouteDebrief(
    string? PlannedOrigin,
    string? PlannedDestination,
    string? ActualDeparture,
    string? ActualArrival,
    string? DiversionLocation,
    double? DistanceNauticalMiles)
{
    public void Validate()
    {
        if (DistanceNauticalMiles is { } distance &&
            (!double.IsFinite(distance) || distance < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(DistanceNauticalMiles));
        }
    }
}

public sealed record AircraftDebrief(
    string DisplayName,
    string? Family = null,
    string? TailNumber = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new ArgumentException("Aircraft display name is required.", nameof(DisplayName));
    }
}

public sealed record LandingDebrief(
    int EpisodeNumber,
    DateTimeOffset Timestamp,
    LandingOperationType OperationType,
    int BounceCount,
    double? VerticalSpeedFeetPerMinute,
    double? TouchdownG,
    double? IndicatedAirspeedKnots,
    double? PitchDegrees,
    double? BankDegrees,
    bool? HardLanding,
    EvidenceQuality EvidenceQuality)
{
    public void Validate()
    {
        if (EpisodeNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(EpisodeNumber));

        if (BounceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(BounceCount));

        ValidateFinite(VerticalSpeedFeetPerMinute, nameof(VerticalSpeedFeetPerMinute));
        ValidateFinite(TouchdownG, nameof(TouchdownG));
        ValidateFinite(IndicatedAirspeedKnots, nameof(IndicatedAirspeedKnots));
        ValidateFinite(PitchDegrees, nameof(PitchDegrees));
        ValidateFinite(BankDegrees, nameof(BankDegrees));

        if (IndicatedAirspeedKnots is < 0)
            throw new ArgumentOutOfRangeException(nameof(IndicatedAirspeedKnots));
    }

    private static void ValidateFinite(double? value, string parameterName)
    {
        if (value is { } number && !double.IsFinite(number))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record FlightDebriefEvent(
    Guid EventId,
    DateTimeOffset Timestamp,
    string Category,
    DebriefEventSeverity Severity,
    string Text,
    EvidenceQuality EvidenceQuality)
{
    public void Validate()
    {
        if (EventId == Guid.Empty)
            throw new ArgumentException("Event id is required.", nameof(EventId));

        if (string.IsNullOrWhiteSpace(Category))
            throw new ArgumentException("Event category is required.", nameof(Category));

        if (string.IsNullOrWhiteSpace(Text))
            throw new ArgumentException("Event text is required.", nameof(Text));
    }
}

public sealed record FlightSettlementRecord(
    SettlementRecordStatus Status,
    string? IdempotencyKey,
    string? TransactionId,
    DateTimeOffset? SettledAt,
    decimal? CashDelta,
    double? ReputationDelta)
{
    public static FlightSettlementRecord NotApplicable { get; } =
        new(SettlementRecordStatus.NotApplicable, null, null, null, null, null);

    public static FlightSettlementRecord Pending(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Settlement idempotency key is required.", nameof(idempotencyKey));

        return new(
            SettlementRecordStatus.Pending,
            idempotencyKey,
            null,
            null,
            null,
            null);
    }

    public void Validate()
    {
        if (ReputationDelta is { } reputation && !double.IsFinite(reputation))
            throw new ArgumentOutOfRangeException(nameof(ReputationDelta));

        switch (Status)
        {
            case SettlementRecordStatus.NotApplicable:
                if (IdempotencyKey is not null ||
                    TransactionId is not null ||
                    SettledAt is not null ||
                    CashDelta is not null ||
                    ReputationDelta is not null)
                {
                    throw new InvalidOperationException(
                        "A non-settled flight cannot carry settlement values.");
                }
                break;

            case SettlementRecordStatus.Pending:
                if (string.IsNullOrWhiteSpace(IdempotencyKey))
                    throw new InvalidOperationException(
                        "Pending settlement requires an idempotency key.");

                if (TransactionId is not null ||
                    SettledAt is not null ||
                    CashDelta is not null ||
                    ReputationDelta is not null)
                {
                    throw new InvalidOperationException(
                        "Pending settlement cannot contain final settlement values.");
                }
                break;

            case SettlementRecordStatus.Settled:
                if (string.IsNullOrWhiteSpace(IdempotencyKey) ||
                    string.IsNullOrWhiteSpace(TransactionId) ||
                    SettledAt is null ||
                    CashDelta is null ||
                    ReputationDelta is null)
                {
                    throw new InvalidOperationException(
                        "Settled records require the authoritative settlement identity, timestamp and deltas.");
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Status));
        }
    }
}

public sealed record FlightDebriefDraft(
    Guid DebriefId,
    Guid SessionId,
    Guid? ContractId,
    LogbookEntryKind EntryKind,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    FlightRouteDebrief Route,
    AircraftDebrief Aircraft,
    FlightTimeLedger Time,
    FlightTrackingSnapshot Tracking,
    IReadOnlyList<FlightLegDebrief> Legs,
    FlightFuelDebrief Fuel,
    PayloadDebrief Payload,
    FlightAssistanceDebrief Assistance,
    FlightSafetyOutcome SafetyOutcome,
    MissionOutcome MissionOutcome,
    IReadOnlyList<LandingDebrief> Landings,
    IReadOnlyList<FlightDebriefEvent> Events,
    FlightSettlementRecord Settlement);

public sealed record FlightDebrief(
    Guid DebriefId,
    Guid SessionId,
    Guid? ContractId,
    LogbookEntryKind EntryKind,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    FlightRouteDebrief Route,
    AircraftDebrief Aircraft,
    FlightTimeLedger Time,
    FlightTrackingSnapshot Tracking,
    IReadOnlyList<FlightLegDebrief> Legs,
    FlightFuelDebrief Fuel,
    PayloadDebrief Payload,
    FlightAssistanceDebrief Assistance,
    FlightSafetyOutcome SafetyOutcome,
    MissionOutcome MissionOutcome,
    IReadOnlyList<LandingDebrief> Landings,
    IReadOnlyList<FlightDebriefEvent> Events,
    FlightSettlementRecord Settlement);

public static class FlightDebriefFactory
{
    public static FlightDebrief Create(FlightDebriefDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Route);
        ArgumentNullException.ThrowIfNull(draft.Aircraft);
        ArgumentNullException.ThrowIfNull(draft.Time);
        ArgumentNullException.ThrowIfNull(draft.Tracking);
        ArgumentNullException.ThrowIfNull(draft.Legs);
        ArgumentNullException.ThrowIfNull(draft.Fuel);
        ArgumentNullException.ThrowIfNull(draft.Payload);
        ArgumentNullException.ThrowIfNull(draft.Assistance);
        ArgumentNullException.ThrowIfNull(draft.Landings);
        ArgumentNullException.ThrowIfNull(draft.Events);
        ArgumentNullException.ThrowIfNull(draft.Settlement);

        if (draft.DebriefId == Guid.Empty)
            throw new ArgumentException("Debrief id is required.", nameof(draft));

        if (draft.SessionId == Guid.Empty)
            throw new ArgumentException("Session id is required.", nameof(draft));

        if (draft.EntryKind == LogbookEntryKind.CareerJob &&
            draft.ContractId is null)
        {
            throw new InvalidOperationException(
                "Career-job debriefs require a contract id.");
        }

        if (draft.EndedAt < draft.StartedAt)
            throw new InvalidOperationException(
                "Flight end time cannot precede flight start time.");

        draft.Route.Validate();
        draft.Aircraft.Validate();
        draft.Fuel.Validate();
        draft.Payload.Validate();
        draft.Settlement.Validate();

        FlightLegDebrief[] legs = draft.Legs
            .OrderBy(static leg => leg.Sequence)
            .ToArray();

        if (legs.Length == 0)
            throw new InvalidOperationException(
                "A completed debrief must contain at least one flight leg.");

        if (legs.Select(static leg => leg.LegId).Distinct().Count() != legs.Length)
            throw new InvalidOperationException("Flight leg ids must be unique.");

        for (int index = 0; index < legs.Length; index++)
        {
            FlightLegDebrief leg = legs[index];
            leg.Validate(draft.StartedAt, draft.EndedAt);

            int expectedSequence = index + 1;
            if (leg.Sequence != expectedSequence)
                throw new InvalidOperationException(
                    "Flight leg sequence must be contiguous and start at one.");

            if (index > 0 && leg.StartedAt < legs[index - 1].EndedAt)
                throw new InvalidOperationException(
                    "Flight legs cannot overlap.");
        }

        LandingDebrief[] landings = draft.Landings
            .OrderBy(static item => item.EpisodeNumber)
            .ThenBy(static item => item.Timestamp)
            .ToArray();

        if (landings.Select(static item => item.EpisodeNumber).Distinct().Count() != landings.Length)
            throw new InvalidOperationException(
                "A debrief can contain only one summary per landing episode.");

        foreach (LandingDebrief landing in landings)
        {
            landing.Validate();
            ValidateTimestamp(
                landing.Timestamp,
                draft.StartedAt,
                draft.EndedAt,
                "Landing");
        }

        FlightDebriefEvent[] events = draft.Events
            .OrderBy(static item => item.Timestamp)
            .ThenBy(static item => item.EventId)
            .ToArray();

        if (events.Select(static item => item.EventId).Distinct().Count() != events.Length)
            throw new InvalidOperationException("Debrief event ids must be unique.");

        foreach (FlightDebriefEvent item in events)
        {
            item.Validate();
            ValidateTimestamp(
                item.Timestamp,
                draft.StartedAt,
                draft.EndedAt,
                "Debrief event");
        }

        int[] referencedEpisodes = legs
            .SelectMany(static leg => leg.LandingEpisodeNumbers)
            .OrderBy(static episode => episode)
            .ToArray();

        int[] actualEpisodes = landings
            .Select(static landing => landing.EpisodeNumber)
            .OrderBy(static episode => episode)
            .ToArray();

        if (!referencedEpisodes.SequenceEqual(actualEpisodes))
            throw new InvalidOperationException(
                "Flight-leg landing references must match the session landing summaries exactly.");

        if (draft.Settlement.SettledAt is { } settledAt &&
            settledAt < draft.EndedAt)
        {
            throw new InvalidOperationException(
                "Settlement cannot precede the end of the flight.");
        }

        return new(
            draft.DebriefId,
            draft.SessionId,
            draft.ContractId,
            draft.EntryKind,
            draft.StartedAt,
            draft.EndedAt,
            draft.Route,
            draft.Aircraft,
            draft.Time,
            draft.Tracking,
            legs,
            draft.Fuel,
            draft.Payload,
            draft.Assistance,
            draft.SafetyOutcome,
            draft.MissionOutcome,
            landings,
            events,
            draft.Settlement);
    }

    private static void ValidateTimestamp(
        DateTimeOffset timestamp,
        DateTimeOffset start,
        DateTimeOffset end,
        string label)
    {
        if (timestamp < start || timestamp > end)
            throw new InvalidOperationException(
                $"{label} timestamp must fall within the flight interval.");
    }
}

public sealed record LogbookEntry(
    Guid EntryId,
    FlightDebrief Debrief,
    DateTimeOffset CommittedAt,
    LogbookCommitKind CommitKind)
{
    public static LogbookEntry Commit(
        Guid entryId,
        FlightDebrief debrief,
        DateTimeOffset committedAt,
        LogbookCommitKind commitKind)
    {
        if (entryId == Guid.Empty)
            throw new ArgumentException("Logbook entry id is required.", nameof(entryId));

        ArgumentNullException.ThrowIfNull(debrief);

        if (committedAt < debrief.EndedAt)
            throw new InvalidOperationException(
                "A logbook entry cannot be committed before the flight ends.");

        if (debrief.MissionOutcome == MissionOutcome.Pending)
            throw new InvalidOperationException(
                "A logbook entry cannot freeze a pending mission outcome.");

        if (debrief.Settlement.Status == SettlementRecordStatus.Pending)
            throw new InvalidOperationException(
                "A logbook entry cannot freeze a pending settlement.");

        switch (commitKind)
        {
            case LogbookCommitKind.AutomaticCareerSettlement:
                if (debrief.ContractId is null ||
                    debrief.Settlement.Status != SettlementRecordStatus.Settled)
                {
                    throw new InvalidOperationException(
                        "Automatic career logbook commits require an authoritative settled contract.");
                }
                break;

            case LogbookCommitKind.ManualPilotLog:
                if (debrief.ContractId is not null)
                {
                    throw new InvalidOperationException(
                        "Contract flights are committed by the authoritative career settlement flow.");
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(commitKind));
        }

        return new(entryId, debrief, committedAt, commitKind);
    }
}
