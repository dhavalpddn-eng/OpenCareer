using Microsoft.Extensions.Logging;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public enum CareerFlightAbandonAvailabilityState
{
    Unavailable = 0,
    Ready = 1,
    CleanupPending = 2
}

public sealed record CareerFlightAbandonAvailability(
    bool CanAbandon,
    CareerFlightAbandonAvailabilityState State,
    Guid? SessionId,
    Guid? ContractId,
    string Detail);

public enum CareerFlightAbandonStatus
{
    Abandoned = 0,
    NoActiveSession = 1
}

public sealed record CareerFlightAbandonResult(
    CareerFlightAbandonStatus Status,
    Guid? SessionId,
    Guid? ContractId,
    bool SessionWasAlreadyCancelled,
    bool ContractWasAlreadyCancelled,
    bool ReservationWasAlreadyReleased);

public interface ICareerFlightAbandonAction
{
    Task<CareerFlightAbandonAvailability> ReadAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<CareerFlightAbandonResult> AbandonAsync(
        Guid expectedSessionId,
        Guid expectedContractId,
        CancellationToken cancellationToken = default);
}

public sealed class CareerFlightAbandonCoordinator
    : ICareerFlightAbandonAction
{
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly FlightSessionPersistenceService _flightPersistence;
    private readonly IJobContractStore _contractStore;
    private readonly JobContractLifecycleService _contractLifecycle;
    private readonly JobContractRuntimeState _contractRuntime;
    private readonly IAircraftReservationLookup _reservationLookup;
    private readonly IAircraftReservationStore _reservationStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CareerFlightAbandonCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CareerFlightAbandonCoordinator(
        FlightSessionCoordinator flightSessions,
        FlightSessionPersistenceService flightPersistence,
        IJobContractStore contractStore,
        JobContractLifecycleService contractLifecycle,
        JobContractRuntimeState contractRuntime,
        IAircraftReservationLookup reservationLookup,
        IAircraftReservationStore reservationStore,
        TimeProvider timeProvider,
        ILogger<CareerFlightAbandonCoordinator> logger)
    {
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
        _flightPersistence =
            flightPersistence
            ?? throw new ArgumentNullException(nameof(flightPersistence));
        _contractStore =
            contractStore
            ?? throw new ArgumentNullException(nameof(contractStore));
        _contractLifecycle =
            contractLifecycle
            ?? throw new ArgumentNullException(nameof(contractLifecycle));
        _contractRuntime =
            contractRuntime
            ?? throw new ArgumentNullException(nameof(contractRuntime));
        _reservationLookup =
            reservationLookup
            ?? throw new ArgumentNullException(nameof(reservationLookup));
        _reservationStore =
            reservationStore
            ?? throw new ArgumentNullException(nameof(reservationStore));
        _timeProvider =
            timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger =
            logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CareerFlightAbandonAvailability> ReadAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession? session =
                _flightSessions.Current;

            if (session is null)
            {
                return Unavailable(
                    "No current career FlightSession is available to abandon.");
            }

            if (session.ContractId is not { } contractId)
            {
                return Unavailable(
                    "The current FlightSession is not linked to a career contract.");
            }

            if (!IsCancellableSession(session))
            {
                return Unavailable(
                    session.Status == FlightSessionStatus.Completed
                        ? "A completed career flight cannot be abandoned."
                        : "This terminal FlightSession cannot be abandoned.");
            }

            PersistedJobContract? persisted =
                await _contractStore
                    .ReadJobContractAsync(
                        contractId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (persisted is null
                || persisted.Contract.ContractId != contractId)
            {
                return Unavailable(
                    "The matching authoritative career contract could not be verified.");
            }

            persisted.Validate();

            if (persisted.Contract.Status is not
                (ContractStatus.InProgress
                or ContractStatus.Cancelled))
            {
                return Unavailable(
                    $"The matching career contract is {persisted.Contract.Status} and cannot use flight abandonment.");
            }

            string reservationId =
                JobAcceptanceFleetBridge.GetReservationId(
                    contractId);

            AircraftReservationOwnership? ownership =
                await _reservationLookup
                    .FindByReservationIdAsync(
                        reservationId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (ownership is not null)
            {
                ValidateReservationOwnership(
                    ownership,
                    reservationId);
            }
            else if (session.Status != FlightSessionStatus.Cancelled
                && persisted.Contract.Status != ContractStatus.Cancelled)
            {
                return Unavailable(
                    "The active contract's Fleet reservation could not be verified.");
            }

            bool cleanupPending =
                session.Status == FlightSessionStatus.Cancelled
                || persisted.Contract.Status == ContractStatus.Cancelled
                || ownership is null;

            return new(
                CanAbandon: true,
                cleanupPending
                    ? CareerFlightAbandonAvailabilityState.CleanupPending
                    : CareerFlightAbandonAvailabilityState.Ready,
                session.SessionId,
                contractId,
                cleanupPending
                    ? "Cancellation cleanup is pending. Retry to reconcile the contract, Fleet reservation, and saved FlightSession."
                    : "Abandoning cancels this flight and contract, releases its aircraft, and grants no completion rewards.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CareerFlightAbandonResult> AbandonAsync(
        Guid expectedSessionId,
        Guid expectedContractId,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
            throw new ArgumentException("Session ID is required.", nameof(expectedSessionId));

        if (expectedContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(expectedContractId));

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession? session =
                _flightSessions.Current;

            if (session is null)
            {
                _logger.LogInformation(
                    "Career flight abandon replay found no current FlightSession for session {SessionId} and contract {ContractId}.",
                    expectedSessionId,
                    expectedContractId);

                return new(
                    CareerFlightAbandonStatus.NoActiveSession,
                    expectedSessionId,
                    expectedContractId,
                    SessionWasAlreadyCancelled: false,
                    ContractWasAlreadyCancelled: false,
                    ReservationWasAlreadyReleased: false);
            }

            ValidateSession(
                session,
                expectedSessionId,
                expectedContractId);

            PersistedJobContract contract =
                await ReadMatchingContractAsync(
                        expectedContractId,
                        cancellationToken)
                    .ConfigureAwait(false);

            ValidateContractState(contract);

            string reservationId =
                JobAcceptanceFleetBridge.GetReservationId(
                    expectedContractId);

            AircraftReservationOwnership? ownership =
                await _reservationLookup
                    .FindByReservationIdAsync(
                        reservationId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (ownership is not null)
            {
                ValidateReservationOwnership(
                    ownership,
                    reservationId);
            }
            else if (session.Status != FlightSessionStatus.Cancelled
                && contract.Contract.Status != ContractStatus.Cancelled)
            {
                throw new InvalidOperationException(
                    "The active contract's Fleet reservation could not be verified before cancellation.");
            }

            bool sessionWasAlreadyCancelled =
                session.Status == FlightSessionStatus.Cancelled;

            _logger.LogInformation(
                "Abandoning career FlightSession {SessionId} for contract {ContractId}; session already cancelled: {SessionAlreadyCancelled}.",
                expectedSessionId,
                expectedContractId,
                sessionWasAlreadyCancelled);

            FlightSession cancelledSession =
                await _flightPersistence
                    .CancelAsync(
                        expectedSessionId,
                        expectedContractId,
                        _timeProvider.GetUtcNow(),
                        cancellationToken)
                    .ConfigureAwait(false);

            ValidateCancelledSession(
                cancelledSession,
                expectedSessionId,
                expectedContractId);

            bool contractWasAlreadyCancelled =
                contract.Contract.Status == ContractStatus.Cancelled;

            if (!contractWasAlreadyCancelled)
            {
                contract =
                    await _contractLifecycle
                        .CancelAsync(
                            expectedContractId,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            else
            {
                contract =
                    await ReadMatchingContractAsync(
                            expectedContractId,
                            cancellationToken)
                        .ConfigureAwait(false);
            }

            if (contract.Contract.Status != ContractStatus.Cancelled)
            {
                throw new InvalidOperationException(
                    "Authoritative career contract cancellation did not persist.");
            }

            await _contractRuntime
                .RemoveCancelledAsync(
                    contract,
                    cancellationToken)
                .ConfigureAwait(false);

            bool reservationWasAlreadyReleased =
                ownership is null;

            if (ownership is not null)
            {
                AircraftReservationReleaseResult release =
                    await _reservationStore
                        .ReleaseReservationAsync(
                            ownership.CanonicalAircraftId,
                            reservationId,
                            cancellationToken)
                        .ConfigureAwait(false);

                reservationWasAlreadyReleased =
                    release == AircraftReservationReleaseResult.AlreadyReleased;

                if (release is not
                    (AircraftReservationReleaseResult.Released
                    or AircraftReservationReleaseResult.AlreadyReleased))
                {
                    throw new InvalidOperationException(
                        release == AircraftReservationReleaseResult.HeldByAnotherReservation
                            ? "The aircraft reservation changed ownership before abandonment could release it."
                            : "The contract-owned aircraft is unavailable for a non-reservation reason.");
                }
            }

            FlightSession terminal =
                _flightSessions.Current
                ?? throw new InvalidOperationException(
                    "Cancelled FlightSession disappeared before terminal cleanup.");

            ValidateCancelledSession(
                terminal,
                expectedSessionId,
                expectedContractId);

            await _flightPersistence
                .ClearTerminalAsync(
                    expectedSessionId,
                    expectedContractId,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Career FlightSession {SessionId} abandoned; contract {ContractId} cancelled, reservation {ReservationId} reconciled, and terminal checkpoint cleared.",
                expectedSessionId,
                expectedContractId,
                reservationId);

            return new(
                CareerFlightAbandonStatus.Abandoned,
                expectedSessionId,
                expectedContractId,
                sessionWasAlreadyCancelled,
                contractWasAlreadyCancelled,
                reservationWasAlreadyReleased);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PersistedJobContract> ReadMatchingContractAsync(
        Guid contractId,
        CancellationToken cancellationToken)
    {
        PersistedJobContract persisted =
            await _contractStore
                .ReadJobContractAsync(
                    contractId,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Persisted career contract was not found.");

        persisted.Validate();

        if (persisted.Contract.ContractId != contractId)
        {
            throw new InvalidOperationException(
                "Persisted career-contract identity does not match the current FlightSession.");
        }

        return persisted;
    }

    private static void ValidateSession(
        FlightSession session,
        Guid expectedSessionId,
        Guid expectedContractId)
    {
        if (session.SessionId != expectedSessionId
            || session.ContractId != expectedContractId)
        {
            throw new InvalidOperationException(
                "Current FlightSession identity changed before abandonment.");
        }

        if (!IsCancellableSession(session))
        {
            throw new InvalidOperationException(
                session.Status == FlightSessionStatus.Completed
                    ? "A completed career flight cannot be abandoned."
                    : "An interrupted terminal FlightSession cannot be abandoned.");
        }

        if (session.Status == FlightSessionStatus.Cancelled
            && session.OperationState != FlightOperationState.Cancelled)
        {
            throw new InvalidOperationException(
                "Cancelled FlightSession operation state is inconsistent.");
        }
    }

    private static bool IsCancellableSession(
        FlightSession session) =>
        !session.IsTerminal
        || session.Status == FlightSessionStatus.Cancelled;

    private static void ValidateContractState(
        PersistedJobContract contract)
    {
        if (contract.Contract.Status is not
            (ContractStatus.InProgress
            or ContractStatus.Cancelled))
        {
            throw new InvalidOperationException(
                $"A started career flight cannot be abandoned while its contract is {contract.Contract.Status}.");
        }
    }

    private static void ValidateCancelledSession(
        FlightSession session,
        Guid expectedSessionId,
        Guid expectedContractId)
    {
        if (session.SessionId != expectedSessionId
            || session.ContractId != expectedContractId
            || session.Status != FlightSessionStatus.Cancelled
            || session.OperationState != FlightOperationState.Cancelled)
        {
            throw new InvalidOperationException(
                "FlightSession cancellation did not preserve the expected terminal identity and state.");
        }
    }

    private static void ValidateReservationOwnership(
        AircraftReservationOwnership ownership,
        string expectedReservationId)
    {
        ownership.Validate();

        if (!string.Equals(
                ownership.ReservationId,
                expectedReservationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Fleet reservation lookup returned a different contract owner.");
        }
    }

    private static CareerFlightAbandonAvailability Unavailable(
        string detail) =>
        new(
            CanAbandon: false,
            CareerFlightAbandonAvailabilityState.Unavailable,
            SessionId: null,
            ContractId: null,
            detail);
}
