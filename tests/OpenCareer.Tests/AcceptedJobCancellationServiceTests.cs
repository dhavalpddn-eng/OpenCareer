using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class AcceptedJobCancellationServiceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthoritativeCancellationFinishesCleanupAfterCallerCancellation()
    {
        Guid contractId =
            Guid.Parse("c4000000-0000-0000-0000-000000000001");
        using var cancellation = new CancellationTokenSource();
        var contracts = new CancellingContractStore(
            AcceptedContract(contractId),
            cancellation);
        var runtime = new JobContractRuntimeState(
            new JobContractRecoveryService(
                new EmptyRecoverySource(),
                contracts));
        var reservations = new RecordingReservations(
            contractId,
            "aircraft-a");
        var preparedPlans = new RecordingPreparedPlanStore();
        var service = new AcceptedJobCancellationService(
            contracts,
            new JobContractLifecycleService(contracts),
            runtime,
            reservations,
            reservations,
            preparedPlans);

        AcceptedJobCancellationResult result = await service
            .CancelAsync(contractId, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(ContractStatus.Cancelled, contracts.Current.Contract.Status);
        Assert.Equal(1, reservations.LookupCount);
        Assert.Equal(1, reservations.ReleaseCount);
        Assert.Equal(1, preparedPlans.ClearCount);
        Assert.False(reservations.LookupObservedCancellation);
        Assert.False(reservations.ReleaseObservedCancellation);
        Assert.False(preparedPlans.ClearObservedCancellation);
        Assert.Equal(contractId, result.ContractId);
        Assert.False(result.ReservationWasAlreadyReleased);
    }

    private static PersistedJobContract AcceptedContract(Guid contractId)
    {
        var contract = new JobContract(
            ContractId: contractId,
            EmployerId:
                Guid.Parse("e4000000-0000-0000-0000-000000000001"),
            Kind: ContractKind.Ferry,
            ServiceTrack: ServiceTrack.CivilianEmployment,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation: new ContractCompensation(
                CompensationModel.PilotWage,
                GrossCustomerRevenue: 1_250m,
                PilotCompensation: 450m,
                EmployerCoversFuel: true,
                EmployerCoversMaintenance: true,
                EmployerCoversAirportFees: true),
            OfferedAt: OfferedAt,
            MustStartBy: OfferedAt.AddHours(2),
            MustCompleteBy: OfferedAt.AddHours(5),
            AircraftRequirements: new AircraftMissionRequirements(
                RequiredCapabilities: AircraftCapability.Cargo,
                AllowedAccess: AircraftAccess.Civilian,
                MinimumPayloadPounds: 0,
                MinimumRangeNauticalMiles: 100,
                MinimumSeats: 0),
            Status: ContractStatus.Accepted,
            AcceptedAt: OfferedAt.AddMinutes(10));
        contract.Validate();
        return new PersistedJobContract(contract, Version: 1);
    }

    private sealed class CancellingContractStore(
        PersistedJobContract initial,
        CancellationTokenSource cancellation)
        : IJobContractStore
    {
        public PersistedJobContract Current { get; private set; } = initial;

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PersistedJobContract?>(
                Current.Contract.ContractId == contractId
                    ? Current
                    : null);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(Current.Version, expectedVersion);
            Current = new PersistedJobContract(
                contract,
                checked(expectedVersion + 1));
            cancellation.Cancel();
            return Task.FromResult(JobContractSaveResult.Updated);
        }
    }

    private sealed class EmptyRecoverySource : IJobContractRecoverySource
    {
        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<JobContractRecoveryCandidate>>(
                Array.Empty<JobContractRecoveryCandidate>());
        }
    }

    private sealed class RecordingReservations(
        Guid contractId,
        string aircraftId)
        : IAircraftReservationLookup,
          IAircraftReservationStore
    {
        private readonly string _reservationId =
            JobAcceptanceFleetBridge.GetReservationId(contractId);

        public int LookupCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public bool LookupObservedCancellation { get; private set; }
        public bool ReleaseObservedCancellation { get; private set; }

        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            LookupCount++;
            LookupObservedCancellation = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(_reservationId, reservationId);
            return Task.FromResult<AircraftReservationOwnership?>(
                new AircraftReservationOwnership(aircraftId, reservationId));
        }

        public Task<AircraftReservationAcquireResult> TryReserveAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            ReleaseObservedCancellation = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(aircraftId, canonicalAircraftId);
            Assert.Equal(_reservationId, reservationId);
            return Task.FromResult(AircraftReservationReleaseResult.Released);
        }
    }

    private sealed class RecordingPreparedPlanStore : IPreparedDispatchPlanStore
    {
        public int ClearCount { get; private set; }
        public bool ClearObservedCancellation { get; private set; }

        public Task<PreparedDispatchPlan?> ReadAsync(
            Guid contractId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PreparedDispatchPlanSaveResult> SaveAsync(
            PreparedDispatchPlan plan,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            ClearCount++;
            ClearObservedCancellation = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
