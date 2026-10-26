using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public enum CareerJobPreFlightSessionRecoveryState
{
    NothingToRecover = 0,
    ActiveFlightSessionAlreadyPresent = 1,
    WaitingForReservation = 2,
    WaitingForCareer = 3,
    WaitingForAircraft = 4,
    WaitingForDispatch = 5,
    ContractStartBlocked = 6,
    Recovered = 7,
    WaitingForPreparedDispatch = 8
}

public sealed record CareerJobPreFlightSessionRecoveryResult(
    CareerJobPreFlightSessionRecoveryState State,
    Guid? ContractId,
    string Detail,
    StartedJobFlightSessionResult? StartedFlight = null)
{
    public bool Recovered =>
        State == CareerJobPreFlightSessionRecoveryState.Recovered
        && StartedFlight is not null;
}

public interface ICareerJobPreFlightSessionRecoveryService
{
    Task<CareerJobPreFlightSessionRecoveryResult> RecoverAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CareerJobPreFlightSessionRecoveryService
    : ICareerJobPreFlightSessionRecoveryService
{
    private readonly JobContractRuntimeState _contracts;
    private readonly PlayerCareerRuntimeState _career;
    private readonly IAircraftReservationLookup _reservations;
    private readonly IAircraftRegistrySource _aircraftRegistry;
    private readonly IPreparedDispatchStartAuthority _preparedDispatch;
    private readonly AcceptedJobFlightSessionBridge _flightStart;
    private readonly FlightSessionCoordinator _sessions;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CareerJobPreFlightSessionRecoveryService(
        JobContractRuntimeState contracts,
        PlayerCareerRuntimeState career,
        IAircraftReservationLookup reservations,
        IAircraftRegistrySource aircraftRegistry,
        IPreparedDispatchStartAuthority preparedDispatch,
        AcceptedJobFlightSessionBridge flightStart,
        FlightSessionCoordinator sessions,
        TimeProvider timeProvider)
    {
        _contracts =
            contracts
            ?? throw new ArgumentNullException(nameof(contracts));
        _career =
            career
            ?? throw new ArgumentNullException(nameof(career));
        _reservations =
            reservations
            ?? throw new ArgumentNullException(nameof(reservations));
        _aircraftRegistry =
            aircraftRegistry
            ?? throw new ArgumentNullException(nameof(aircraftRegistry));
        _preparedDispatch =
            preparedDispatch
            ?? throw new ArgumentNullException(nameof(preparedDispatch));
        _flightStart =
            flightStart
            ?? throw new ArgumentNullException(nameof(flightStart));
        _sessions =
            sessions
            ?? throw new ArgumentNullException(nameof(sessions));
        _timeProvider =
            timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CareerJobPreFlightSessionRecoveryResult> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            CareerJobPreFlightSessionRecoveryResult? activeSession =
                ReadActiveSessionResult();

            if (activeSession is not null)
                return activeSession;

            IReadOnlyList<PersistedJobContract> recovered =
                await _contracts
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false);

            activeSession =
                ReadActiveSessionResult();

            if (activeSession is not null)
                return activeSession;

            PersistedJobContract? candidate =
                SelectRecoveryCandidate(recovered);

            if (candidate is null)
            {
                return Result(
                    CareerJobPreFlightSessionRecoveryState.NothingToRecover,
                    contractId:
                        null,
                    "No supported accepted or in-progress career job requires pre-FlightSession recovery.");
            }

            JobContract contract =
                candidate.Contract;

            string reservationId =
                JobAcceptanceFleetBridge.GetReservationId(
                    contract.ContractId);

            AircraftReservationOwnership? ownership =
                await _reservations
                    .FindByReservationIdAsync(
                        reservationId,
                        cancellationToken)
                    .ConfigureAwait(false);

            activeSession =
                ReadActiveSessionResult();

            if (activeSession is not null)
                return activeSession;

            if (ownership is null)
            {
                return Result(
                    CareerJobPreFlightSessionRecoveryState.WaitingForReservation,
                    contract.ContractId,
                    "The recoverable contract does not currently own its deterministic Fleet reservation.");
            }

            ownership.Validate();

            if (!string.Equals(
                    ownership.ReservationId,
                    reservationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Recovered Fleet reservation identity does not belong to the job contract.");
            }

            AircraftRegistryResolution? resolution =
                await _aircraftRegistry
                    .FindAircraftAsync(
                        ownership.CanonicalAircraftId,
                        cancellationToken)
                    .ConfigureAwait(false);

            activeSession =
                ReadActiveSessionResult();

            if (activeSession is not null)
                return activeSession;

            AircraftRegistryRecord? aircraft =
                resolution is
                    {
                        InstallationStatus:
                            AircraftInstallationStatus.Installed
                    }
                    ? resolution.TryCreateRegistryRecord()
                    : null;

            if (aircraft is null
                || !aircraft.Capabilities.Satisfies(
                    contract.AircraftRequirements)
                || (aircraft.Capabilities.Access
                    & contract.AircraftRequirements.AllowedAccess
                    & AircraftAccess.Civilian) == 0)
            {
                return Result(
                    CareerJobPreFlightSessionRecoveryState.WaitingForAircraft,
                    contract.ContractId,
                    "The contract-owned aircraft is unavailable or no longer satisfies the persisted job requirements.");
            }

            if (contract.Status
                == ContractStatus.InProgress)
            {
                StartedJobFlightSessionResult resumed =
                    await ResumeInProgressAsync(
                            candidate,
                            ownership,
                            aircraft,
                            cancellationToken)
                        .ConfigureAwait(false);

                return Result(
                    CareerJobPreFlightSessionRecoveryState.Recovered,
                    contract.ContractId,
                    "Recovered the durable in-progress job into its deterministic FlightSession.",
                    resumed);
            }

            PlayerCareerProfileStoreRecord? career =
                await _career
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false);

            activeSession =
                ReadActiveSessionResult();

            if (activeSession is not null)
                return activeSession;

            if (career is null
                || !string.Equals(
                    career.Profile.Location.CurrentAirportIcao,
                    contract.OriginIcao,
                    StringComparison.Ordinal))
            {
                return Result(
                    CareerJobPreFlightSessionRecoveryState.WaitingForCareer,
                    contract.ContractId,
                    "The accepted job cannot resume until authoritative Career/Profile state is available at its origin.");
            }

            DateTimeOffset now =
                _timeProvider.GetUtcNow();

            if (contract.AcceptedAt is not { } acceptedAt
                || now < acceptedAt
                || (contract.MustStartBy is { } startBy
                    && now > startBy)
                || (contract.MustCompleteBy is { } completeBy
                    && now > completeBy))
            {
                return Result(
                    CareerJobPreFlightSessionRecoveryState.ContractStartBlocked,
                    contract.ContractId,
                    "The accepted job can no longer enter InProgress at the current career time.");
            }

            PreparedDispatchStartAuthorityResult prepared =
                await _preparedDispatch
                    .RevalidateAsync(
                        contract.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false);

            activeSession =
                ReadActiveSessionResult();

            if (activeSession is not null)
                return activeSession;

            if (!prepared.IsReady
                || prepared.DispatchResult is not { } dispatch)
            {
                return Result(
                    CareerJobPreFlightSessionRecoveryState.WaitingForPreparedDispatch,
                    contract.ContractId,
                    $"Accepted-job recovery requires a current prepared dispatch plan: {prepared.Status}. {prepared.Detail}");
            }

            PilotQualificationState required =
                PilotQualificationState.Entry;

            bool qualificationsVerified =
                career.Profile.Qualifications.License
                    >= required.License
                && career.Profile.Qualifications.Ratings
                    .IsSupersetOf(required.Ratings);

            if (!qualificationsVerified)
            {
                return Result(
                    CareerJobPreFlightSessionRecoveryState.ContractStartBlocked,
                    contract.ContractId,
                    "Current Career/Profile qualifications do not permit the supported recovered job.");
            }

            var context =
                new ContractDispatchContext(
                    now,
                    aircraft.Capabilities,
                    AircraftAccess.Civilian,
                    new WorldEventEffects(),
                    QualificationsVerified:
                        true,
                    DispatchFeasibilityVerified:
                        false);

            var fleet =
                new JobAcceptanceFleetResult(
                    JobAcceptanceFleetStatus.AcceptedAndReservationReused,
                    candidate,
                    reservationId,
                    ownership.CanonicalAircraftId);

            StartedJobFlightSessionResult started =
                await _flightStart
                    .StartAsync(
                        new AcceptedJobDispatchResult(
                            fleet,
                            dispatch),
                        context,
                        cancellationToken)
                    .ConfigureAwait(false);

            return Result(
                CareerJobPreFlightSessionRecoveryState.Recovered,
                contract.ContractId,
                "Recovered the accepted job through reservation-aware Dispatch, Contract Start, and durable FlightSession creation.",
                started);
        }
        finally
        {
            _gate.Release();
        }
    }

    private CareerJobPreFlightSessionRecoveryResult? ReadActiveSessionResult()
    {
        FlightSession? existingSession =
            _sessions.Current;

        return existingSession is null
            ? null
            : Result(
                CareerJobPreFlightSessionRecoveryState.ActiveFlightSessionAlreadyPresent,
                existingSession.ContractId,
                "A FlightSession already owns the current operation.");
    }

    private async Task<StartedJobFlightSessionResult> ResumeInProgressAsync(
        PersistedJobContract inProgress,
        AircraftReservationOwnership ownership,
        AircraftRegistryRecord aircraft,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt =
            inProgress.Contract.StartedAt
            ?? throw new InvalidOperationException(
                "An in-progress recovery candidate is missing its authoritative start time.");

        PersistedJobContract retainedAccepted =
            inProgress with
            {
                Contract =
                    inProgress.Contract with
                    {
                        Status =
                            ContractStatus.Accepted,
                        StartedAt =
                            null,
                        CompletedAt =
                            null
                    }
            };

        retainedAccepted.Validate();

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                inProgress.Contract.ContractId);

        var fleet =
            new JobAcceptanceFleetResult(
                JobAcceptanceFleetStatus.AcceptedAndReservationReused,
                retainedAccepted,
                reservationId,
                ownership.CanonicalAircraftId);

        var context =
            new ContractDispatchContext(
                startedAt,
                aircraft.Capabilities,
                AircraftAccess.Civilian,
                new WorldEventEffects(),
                QualificationsVerified:
                    true,
                DispatchFeasibilityVerified:
                    true);

        return await _flightStart
            .StartAsync(
                new AcceptedJobDispatchResult(
                    fleet,
                    DispatchResult:
                        null),
                context,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static PersistedJobContract? SelectRecoveryCandidate(
        IReadOnlyList<PersistedJobContract> recovered)
    {
        ArgumentNullException.ThrowIfNull(recovered);

        PersistedJobContract[] inProgress =
            recovered
                .Where(
                    item =>
                        item.Contract.Status
                            == ContractStatus.InProgress
                        && Supports(item.Contract))
                .ToArray();

        if (inProgress.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple supported in-progress job contracts cannot share one FlightSession recovery boundary.");
        }

        if (inProgress.Length == 1)
            return inProgress[0];

        PersistedJobContract[] accepted =
            recovered
                .Where(
                    item =>
                        item.Contract.Status
                            == ContractStatus.Accepted
                        && Supports(item.Contract))
                .ToArray();

        if (accepted.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple supported accepted job contracts require explicit operation selection before recovery.");
        }

        return accepted.SingleOrDefault();
    }

    private static bool Supports(
        JobContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Validate();

        bool supportedKind = contract.Kind is
            ContractKind.Ferry
                or ContractKind.Reposition;

        if (contract.Kind == ContractKind.Passenger)
        {
            supportedKind =
                AcceptedPassengerManifest.TryCreate(
                    contract,
                    out _)
                || contract.Status == ContractStatus.InProgress;
        }

        if (contract.Kind is
                ContractKind.Cargo
                or ContractKind.ExpressCargo
                or ContractKind.AogPartsDelivery)
        {
            if (contract.PlanningTerms is { } planningTerms
                && !planningTerms.EffectiveCargoLots.IsEmpty)
            {
                _ = AcceptedCargoManifest.Create(contract);
                supportedKind = true;
            }
            else
            {
                supportedKind =
                    contract.Status == ContractStatus.InProgress;
            }
        }

        return contract.ServiceTrack
                == ServiceTrack.CivilianEmployment
            && supportedKind
            && contract.WorldEventId is null
            && !contract.GovernmentAuthorizationRequired
            && (contract.AircraftRequirements.AllowedAccess
                & AircraftAccess.Civilian) != 0;
    }

    private static CareerJobPreFlightSessionRecoveryResult Result(
        CareerJobPreFlightSessionRecoveryState state,
        Guid? contractId,
        string detail,
        StartedJobFlightSessionResult? startedFlight = null) =>
        new(
            state,
            contractId,
            detail,
            startedFlight);
}
