using OpenCareer.Domain.Checklists;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Checklists;

public sealed class FlightChecklistProgression
{
    private static readonly StepDefinition[] Definitions =
    [
        new(
            FlightChecklistStepId.AircraftReady,
            FlightChecklistPhase.Preflight,
            static evidence =>
                evidence.StableTelemetry
                && evidence.ValidLoadedAircraft),
        new(
            FlightChecklistStepId.EngineStarted,
            FlightChecklistPhase.EngineStart,
            static evidence =>
                evidence.EngineStartObserved),
        new(
            FlightChecklistStepId.TaxiMovementEstablished,
            FlightChecklistPhase.TaxiOut,
            static evidence =>
                evidence.SelfPoweredMovementForFlight),
        new(
            FlightChecklistStepId.TakeoffRollEstablished,
            FlightChecklistPhase.Takeoff,
            static evidence =>
                evidence.TakeoffCandidate),
        new(
            FlightChecklistStepId.AirborneEstablished,
            FlightChecklistPhase.Airborne,
            static evidence =>
                evidence.AirborneConfirmed),
        new(
            FlightChecklistStepId.ApproachEstablished,
            FlightChecklistPhase.Approach,
            static evidence =>
                evidence.ApproachConfirmed),
        new(
            FlightChecklistStepId.TouchdownConfirmed,
            FlightChecklistPhase.Landing,
            static evidence =>
                evidence.TouchdownConfirmed),
        new(
            FlightChecklistStepId.LandingRolloutComplete,
            FlightChecklistPhase.TaxiIn,
            static evidence =>
                evidence.LandingRolloutConfirmed),
        new(
            FlightChecklistStepId.AircraftParked,
            FlightChecklistPhase.TaxiIn,
            static evidence =>
                evidence.ParkingConfirmed),
        new(
            FlightChecklistStepId.ShutdownConfirmed,
            FlightChecklistPhase.Shutdown,
            static evidence =>
                evidence.OperationCompleteConfirmed)
    ];

    private readonly Dictionary<FlightChecklistStepId, DateTimeOffset> _verifiedAt = [];
    private DateTimeOffset? _lastEvidenceTimestamp;

    public FlightChecklistSnapshot Current =>
        CreateSnapshot();

    public FlightChecklistSnapshot Process(
        FlightStateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (_lastEvidenceTimestamp is { } previous
            && evidence.Timestamp < previous)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidence),
                "Checklist evidence cannot move backward in time.");
        }

        _lastEvidenceTimestamp = evidence.Timestamp;

        if (!evidence.Connected)
            return CreateSnapshot();

        for (int index = 0; index < Definitions.Length; index++)
        {
            StepDefinition definition = Definitions[index];

            if (_verifiedAt.ContainsKey(definition.Id))
                continue;

            if (!PreviousStepsVerified(index))
                break;

            if (!definition.IsSatisfied(evidence))
                break;

            _verifiedAt.Add(
                definition.Id,
                evidence.Timestamp);
        }

        return CreateSnapshot();
    }

    public FlightChecklistSnapshot Reset()
    {
        _verifiedAt.Clear();
        _lastEvidenceTimestamp = null;
        return CreateSnapshot();
    }

    private bool PreviousStepsVerified(int exclusiveEnd)
    {
        for (int index = 0; index < exclusiveEnd; index++)
        {
            if (!_verifiedAt.ContainsKey(Definitions[index].Id))
                return false;
        }

        return true;
    }

    private FlightChecklistSnapshot CreateSnapshot()
    {
        FlightChecklistStepSnapshot[] steps = Definitions
            .Select(definition =>
            {
                bool verified = _verifiedAt.TryGetValue(
                    definition.Id,
                    out DateTimeOffset verifiedAt);

                return new FlightChecklistStepSnapshot(
                    definition.Id,
                    definition.Phase,
                    verified
                        ? FlightChecklistStepState.Verified
                        : FlightChecklistStepState.Pending,
                    verified
                        ? verifiedAt
                        : null);
            })
            .ToArray();

        FlightChecklistPhase phase =
            steps
                .FirstOrDefault(static step =>
                    step.State == FlightChecklistStepState.Pending)
                ?.Phase
            ?? FlightChecklistPhase.Complete;

        return new FlightChecklistSnapshot(
            phase,
            steps);
    }

    private sealed record StepDefinition(
        FlightChecklistStepId Id,
        FlightChecklistPhase Phase,
        Func<FlightStateEvidence, bool> IsSatisfied);
}
