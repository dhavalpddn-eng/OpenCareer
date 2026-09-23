using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum JobAcceptanceFleetStatus
{
    AcceptedAndReserved = 0,
    AcceptedAndReservationReused = 1,
    AircraftUnavailable = 2,
    AircraftReservedByAnother = 3,
    AircraftNotFound = 4,
    AircraftNotInstalled = 5
}

public sealed record JobAcceptanceFleetResult(
    JobAcceptanceFleetStatus Status,
    PersistedJobContract? AcceptedContract,
    string ReservationId,
    string? CanonicalAircraftId);

public sealed class JobAcceptanceFleetBridge
{
    private readonly JobOfferAcceptanceService _acceptanceService;
    private readonly IJobContractStore _contractStore;
    private readonly AircraftReservationCoordinator _reservationCoordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JobAcceptanceFleetBridge(
        JobOfferAcceptanceService acceptanceService,
        IJobContractStore contractStore,
        AircraftReservationCoordinator reservationCoordinator)
    {
        _acceptanceService =
            acceptanceService
            ?? throw new ArgumentNullException(nameof(acceptanceService));
        _contractStore =
            contractStore
            ?? throw new ArgumentNullException(nameof(contractStore));
        _reservationCoordinator =
            reservationCoordinator
            ?? throw new ArgumentNullException(nameof(reservationCoordinator));
    }

    public async Task<JobAcceptanceFleetResult> AcceptAndReserveAsync(
        JobContractCreationRequest request,
        ContractDispatchContext context,
        Guid? selectedProviderAircraftInstanceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        request.Validate();
        ArgumentNullException.ThrowIfNull(context.Aircraft);
        context.Aircraft.Validate();

        bool providerAircraftSelected =
            ValidateProviderAircraftSelection(
                request,
                context.Aircraft.AircraftId,
                selectedProviderAircraftInstanceId);

        if (context.Time != request.AcceptanceTime)
        {
            throw new ArgumentException(
                "Dispatch context time must match the offer acceptance time.",
                nameof(context));
        }

        JobContract offered =
            JobContractFactory.Create(request);

        // Validate all domain acceptance gates before Fleet state is touched.
        _ = offered.Accept(context);

        string reservationId =
            GetReservationId(offered.ContractId);

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PersistedJobContract? existing =
                await _contractStore
                    .ReadJobContractAsync(
                        offered.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (existing is not null
                && existing.Contract.Status
                    != ContractStatus.Offered)
            {
                PersistedJobContract accepted =
                    await _acceptanceService
                        .AcceptOfferAsync(
                            request,
                            context,
                            cancellationToken)
                        .ConfigureAwait(false);

                AircraftReservationRequestResult reservation =
                    await ReserveAsync(
                            context.Aircraft.AircraftId,
                            reservationId,
                            providerAircraftSelected,
                            cancellationToken)
                        .ConfigureAwait(false);

                return CreateResult(
                    reservation,
                    reservationId,
                    accepted);
            }

            AircraftReservationRequestResult reserved =
                await ReserveAsync(
                        context.Aircraft.AircraftId,
                        reservationId,
                        providerAircraftSelected,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (reserved.Status is not
                (AircraftReservationRequestStatus.Acquired
                or AircraftReservationRequestStatus.AlreadyHeld))
            {
                return CreateResult(
                    reserved,
                    reservationId,
                    acceptedContract: null);
            }

            try
            {
                PersistedJobContract accepted =
                    await _acceptanceService
                        .AcceptOfferAsync(
                            request,
                            context,
                            cancellationToken)
                        .ConfigureAwait(false);

                return CreateResult(
                    reserved,
                    reservationId,
                    accepted);
            }
            catch (Exception acceptanceException)
            {
                if (reserved.Status
                    == AircraftReservationRequestStatus.Acquired)
                {
                    try
                    {
                        await ReleaseIfAcceptanceDidNotPersistAsync(
                                offered.ContractId,
                                context.Aircraft.AircraftId,
                                reservationId)
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        throw new InvalidOperationException(
                            "Job acceptance failed and the newly acquired aircraft reservation could not be safely reconciled.",
                            new AggregateException(
                                acceptanceException,
                                cleanupException));
                    }
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string GetReservationId(
        Guid contractId)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        return $"contract:{contractId:D}:aircraft-v1";
    }

    private Task<AircraftReservationRequestResult> ReserveAsync(
        string aircraftId,
        string reservationId,
        bool providerAircraftSelected,
        CancellationToken cancellationToken) =>
        providerAircraftSelected
            ? _reservationCoordinator
                .ReserveRegisteredAircraftAsync(
                    aircraftId,
                    reservationId,
                    cancellationToken)
            : _reservationCoordinator
                .ReserveAsync(
                    aircraftId,
                    reservationId,
                    cancellationToken);

    private static bool ValidateProviderAircraftSelection(
        JobContractCreationRequest request,
        string selectedAircraftId,
        Guid? selectedProviderAircraftInstanceId)
    {
        if (selectedProviderAircraftInstanceId is null)
            return false;

        if (request.ProviderAircraft is not { } providerAircraft
            || providerAircraft.ProviderAircraftInstanceId
                != selectedProviderAircraftInstanceId
            || !string.Equals(
                providerAircraft.AircraftId,
                selectedAircraftId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The selected provider-aircraft instance does not belong to the accepted contract and canonical aircraft identity.",
                nameof(selectedProviderAircraftInstanceId));
        }

        return true;
    }

    private async Task ReleaseIfAcceptanceDidNotPersistAsync(
        Guid contractId,
        string aircraftId,
        string reservationId)
    {
        PersistedJobContract? authoritative =
            await _contractStore
                .ReadJobContractAsync(
                    contractId,
                    CancellationToken.None)
                .ConfigureAwait(false);

        if (authoritative?.Contract.Status
            == ContractStatus.Accepted)
        {
            return;
        }

        AircraftReservationReleaseRequestResult released =
            await _reservationCoordinator
                .ReleaseAsync(
                    aircraftId,
                    reservationId,
                    CancellationToken.None)
                .ConfigureAwait(false);

        if (released.Status is not
            (AircraftReservationReleaseRequestStatus.Released
            or AircraftReservationReleaseRequestStatus.AlreadyReleased))
        {
            throw new InvalidOperationException(
                $"Newly acquired aircraft reservation could not be released after failed job acceptance: {released.Status}.");
        }
    }

    private static JobAcceptanceFleetResult CreateResult(
        AircraftReservationRequestResult reservation,
        string reservationId,
        PersistedJobContract? acceptedContract)
    {
        JobAcceptanceFleetStatus status =
            reservation.Status switch
            {
                AircraftReservationRequestStatus.Acquired =>
                    JobAcceptanceFleetStatus.AcceptedAndReserved,
                AircraftReservationRequestStatus.AlreadyHeld =>
                    JobAcceptanceFleetStatus.AcceptedAndReservationReused,
                AircraftReservationRequestStatus.Unavailable =>
                    JobAcceptanceFleetStatus.AircraftUnavailable,
                AircraftReservationRequestStatus.HeldByAnotherReservation =>
                    JobAcceptanceFleetStatus.AircraftReservedByAnother,
                AircraftReservationRequestStatus.AircraftNotFound =>
                    JobAcceptanceFleetStatus.AircraftNotFound,
                AircraftReservationRequestStatus.AircraftNotInstalled =>
                    JobAcceptanceFleetStatus.AircraftNotInstalled,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(reservation),
                    reservation.Status,
                    "Unknown aircraft reservation result.")
            };

        return new(
            status,
            acceptedContract,
            reservationId,
            reservation.CanonicalAircraftId);
    }
}
