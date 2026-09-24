using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobMissionCompletionEvidence(
    Guid ContractId,
    Guid FlightSessionId,
    DateTimeOffset ObservedAt,
    bool MissionConditionsVerified,
    string? ActualDeparture,
    string? ActualArrival,
    string? DiversionLocation,
    PayloadDebrief Payload,
    FlightSafetyOutcome SafetyOutcome,
    MissionOutcome MissionOutcome,
    IReadOnlyList<FlightDebriefEvent>? Events = null,
    bool PositionJumpObserved = false)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));

        if (FlightSessionId == Guid.Empty)
            throw new ArgumentException("FlightSession ID is required.", nameof(FlightSessionId));

        if (ObservedAt == default)
            throw new ArgumentOutOfRangeException(nameof(ObservedAt));

        ArgumentNullException.ThrowIfNull(Payload);
        Payload.Validate();

        if (!Enum.IsDefined(SafetyOutcome))
            throw new ArgumentOutOfRangeException(nameof(SafetyOutcome));

        if (!Enum.IsDefined(MissionOutcome))
            throw new ArgumentOutOfRangeException(nameof(MissionOutcome));

        if (MissionConditionsVerified
            && MissionOutcome != MissionOutcome.Succeeded)
        {
            throw new InvalidOperationException(
                "Verified standard-job mission completion must carry a succeeded mission outcome.");
        }

        if (MissionOutcome == MissionOutcome.Pending)
        {
            throw new InvalidOperationException(
                "Mission completion evidence cannot remain pending.");
        }

        if (Events is not null)
        {
            foreach (FlightDebriefEvent item in Events)
            {
                ArgumentNullException.ThrowIfNull(item);
                item.Validate();
            }
        }
    }
}

public interface ICareerJobMissionCompletionSource
{
    Task<CareerJobMissionCompletionEvidence?> ReadAsync(
        JobContract contract,
        FlightSession flightSession,
        CancellationToken cancellationToken = default);
}

public sealed record CareerJobSettlementCostsEvidence(
    Guid ContractId,
    DateTimeOffset ObservedAt,
    ContractSettlementCosts Costs)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));

        if (ObservedAt == default)
            throw new ArgumentOutOfRangeException(nameof(ObservedAt));

        ArgumentNullException.ThrowIfNull(Costs);
        Costs.Validate();
    }
}

public interface ICareerJobSettlementCostsSource
{
    Task<CareerJobSettlementCostsEvidence?> ReadAsync(
        JobContract contract,
        FlightSession flightSession,
        CancellationToken cancellationToken = default);
}

public enum CareerJobCompletionInputState
{
    NoCareerFlight = 0,
    ContractStateUnavailable = 1,
    ContractNotInProgress = 2,
    FlightEvidenceIncomplete = 3,
    AircraftReservationUnavailable = 4,
    AircraftDebriefUnavailable = 5,
    MissionEvidenceUnavailable = 6,
    MissionConditionsNotVerified = 7,
    SettlementCostsUnavailable = 8,
    Ready = 9
}

public sealed record CareerJobCompletionInputSnapshot(
    CareerJobCompletionInputState State,
    Guid? ContractId,
    CareerJobPlayableCompletionRequest? Request,
    string Detail)
{
    public bool IsReady =>
        State == CareerJobCompletionInputState.Ready
        && Request is not null;
}

public sealed class CareerJobCompletionInputSource
{
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly IJobContractRuntimeSource _contracts;
    private readonly JobFlightCompletionEvidenceTracker _flightEvidence;
    private readonly IAircraftReservationLookup _reservations;
    private readonly IAircraftRegistrySource _aircraftRegistry;
    private readonly ICareerJobMissionCompletionSource[] _missionSources;
    private readonly ICareerJobSettlementCostsSource[] _costSources;

    public CareerJobCompletionInputSource(
        FlightSessionCoordinator flightSessions,
        IJobContractRuntimeSource contracts,
        JobFlightCompletionEvidenceTracker flightEvidence,
        IAircraftReservationLookup reservations,
        IAircraftRegistrySource aircraftRegistry,
        IEnumerable<ICareerJobMissionCompletionSource> missionSources,
        IEnumerable<ICareerJobSettlementCostsSource> costSources)
    {
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
        _contracts =
            contracts
            ?? throw new ArgumentNullException(nameof(contracts));
        _flightEvidence =
            flightEvidence
            ?? throw new ArgumentNullException(nameof(flightEvidence));
        _reservations =
            reservations
            ?? throw new ArgumentNullException(nameof(reservations));
        _aircraftRegistry =
            aircraftRegistry
            ?? throw new ArgumentNullException(nameof(aircraftRegistry));
        _missionSources =
            missionSources?.ToArray()
            ?? throw new ArgumentNullException(nameof(missionSources));
        _costSources =
            costSources?.ToArray()
            ?? throw new ArgumentNullException(nameof(costSources));

        if (_missionSources.Any(static source => source is null))
            throw new ArgumentException("Mission completion sources cannot contain null entries.", nameof(missionSources));

        if (_costSources.Any(static source => source is null))
            throw new ArgumentException("Settlement cost sources cannot contain null entries.", nameof(costSources));
    }

    public async Task<CareerJobCompletionInputSnapshot> ReadCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        FlightSession? session =
            _flightSessions.Current;

        if (session?.ContractId is not { } contractId)
        {
            return Blocked(
                CareerJobCompletionInputState.NoCareerFlight,
                contractId: null,
                "No contract-linked career FlightSession is active.");
        }

        PersistedJobContract? persisted =
            _contracts.Find(contractId);

        if (persisted is null)
        {
            return Blocked(
                CareerJobCompletionInputState.ContractStateUnavailable,
                contractId,
                "Authoritative job-contract runtime state is unavailable.");
        }

        persisted.Validate();

        if (persisted.Contract.Status
            != ContractStatus.InProgress)
        {
            return Blocked(
                CareerJobCompletionInputState.ContractNotInProgress,
                contractId,
                $"Career completion inputs require an InProgress contract, not {persisted.Contract.Status}.");
        }

        JobFlightCompletionEvidenceSnapshot? flightEvidence =
            _flightEvidence.Current;

        if (session.Status != FlightSessionStatus.Active
            || session.OperationState != FlightOperationState.Shutdown
            || flightEvidence is null
            || flightEvidence.ContractId != contractId
            || flightEvidence.FlightSessionId != session.SessionId
            || !flightEvidence.CoreFlightSequenceObserved
            || flightEvidence.IsFailedOrCancelled)
        {
            return Blocked(
                CareerJobCompletionInputState.FlightEvidenceIncomplete,
                contractId,
                "Authoritative takeoff, airborne, landing, parking, and shutdown evidence is not complete.");
        }

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(contractId);

        AircraftReservationOwnership? ownership =
            await _reservations
                .FindByReservationIdAsync(
                    reservationId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (ownership is null)
        {
            return Blocked(
                CareerJobCompletionInputState.AircraftReservationUnavailable,
                contractId,
                "The contract-owned Fleet reservation is unavailable, so aircraft debrief identity cannot be proven.");
        }

        ownership.Validate();

        if (!string.Equals(
                ownership.ReservationId,
                reservationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Fleet reservation lookup returned a different reservation identity.");
        }

        AircraftRegistryResolution? aircraft =
            await _aircraftRegistry
                .FindAircraftAsync(
                    ownership.CanonicalAircraftId,
                    cancellationToken)
                .ConfigureAwait(false);

        string? displayName =
            aircraft?.CapabilityValues.DisplayName;

        if (aircraft is null
            || string.IsNullOrWhiteSpace(displayName))
        {
            return Blocked(
                CareerJobCompletionInputState.AircraftDebriefUnavailable,
                contractId,
                "Canonical aircraft registry identity is insufficient to create the authoritative debrief.");
        }

        CareerJobMissionCompletionEvidence? mission =
            await ReadSingleMissionEvidenceAsync(
                    persisted.Contract,
                    session,
                    cancellationToken)
                .ConfigureAwait(false);

        if (mission is null)
        {
            return Blocked(
                CareerJobCompletionInputState.MissionEvidenceUnavailable,
                contractId,
                "No authoritative standard-job mission completion source produced evidence.");
        }

        mission.Validate();

        if (mission.ContractId != contractId
            || mission.FlightSessionId != session.SessionId)
        {
            throw new InvalidOperationException(
                "Mission completion evidence does not belong to the current career flight.");
        }

        if (!mission.MissionConditionsVerified)
        {
            return Blocked(
                CareerJobCompletionInputState.MissionConditionsNotVerified,
                contractId,
                "The authoritative mission source has not verified standard-job completion conditions.");
        }

        CareerJobSettlementCostsEvidence? costs =
            await ReadSingleCostsEvidenceAsync(
                    persisted.Contract,
                    session,
                    cancellationToken)
                .ConfigureAwait(false);

        if (costs is null)
        {
            return Blocked(
                CareerJobCompletionInputState.SettlementCostsUnavailable,
                contractId,
                "No authoritative actual-cost source produced settlement costs for this career flight.");
        }

        costs.Validate();

        if (costs.ContractId != contractId)
        {
            throw new InvalidOperationException(
                "Settlement cost evidence does not belong to the current career contract.");
        }

        DateTimeOffset terminalTimestamp =
            Latest(
                session.UpdatedAt,
                mission.ObservedAt,
                costs.ObservedAt);

        var logbookContext =
            new SettledJobLogbookContext(
                new AircraftDebrief(displayName),
                mission.ActualDeparture,
                mission.ActualArrival,
                mission.DiversionLocation,
                mission.Payload,
                mission.SafetyOutcome,
                mission.MissionOutcome,
                persisted.Contract.ReputationReward,
                mission.Events,
                mission.PositionJumpObserved);

        var request =
            new CareerJobPlayableCompletionRequest(
                contractId,
                FlightCompletionTime:
                    session.UpdatedAt,
                MissionConditionsVerified:
                    true,
                PostFlightTasksVerified:
                    true,
                costs.Costs,
                SettledAt:
                    terminalTimestamp,
                logbookContext,
                LogbookCommittedAt:
                    terminalTimestamp,
                ExperienceSavedAt:
                    terminalTimestamp);

        return new(
            CareerJobCompletionInputState.Ready,
            contractId,
            request,
            "Authoritative flight, mission, aircraft, settlement-cost, and debrief inputs are ready.");
    }

    private async Task<CareerJobMissionCompletionEvidence?> ReadSingleMissionEvidenceAsync(
        JobContract contract,
        FlightSession session,
        CancellationToken cancellationToken)
    {
        var matches =
            new List<CareerJobMissionCompletionEvidence>();

        foreach (ICareerJobMissionCompletionSource source in _missionSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CareerJobMissionCompletionEvidence? evidence =
                await source
                    .ReadAsync(
                        contract,
                        session,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (evidence is not null)
                matches.Add(evidence);
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "Multiple mission completion sources claimed authority for one career job.")
        };
    }

    private async Task<CareerJobSettlementCostsEvidence?> ReadSingleCostsEvidenceAsync(
        JobContract contract,
        FlightSession session,
        CancellationToken cancellationToken)
    {
        var matches =
            new List<CareerJobSettlementCostsEvidence>();

        foreach (ICareerJobSettlementCostsSource source in _costSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CareerJobSettlementCostsEvidence? evidence =
                await source
                    .ReadAsync(
                        contract,
                        session,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (evidence is not null)
                matches.Add(evidence);
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "Multiple settlement-cost sources claimed authority for one career job.")
        };
    }

    private static DateTimeOffset Latest(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third)
    {
        DateTimeOffset latest =
            first > second
                ? first
                : second;

        return latest > third
            ? latest
            : third;
    }

    private static CareerJobCompletionInputSnapshot Blocked(
        CareerJobCompletionInputState state,
        Guid? contractId,
        string detail) =>
        new(
            state,
            contractId,
            Request: null,
            detail);
}
