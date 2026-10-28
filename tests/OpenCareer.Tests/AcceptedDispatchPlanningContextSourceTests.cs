using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class AcceptedDispatchPlanningContextSourceTests
{
    [Fact]
    public async Task UninitializedContractsFailClosedWithoutReservationLookup()
    {
        var reservations = new StubReservations(null);
        var source = new AcceptedDispatchPlanningContextSource(
            new StubContracts(false, []),
            reservations);

        AcceptedDispatchPlanningContext result = await source.ReadAsync();

        Assert.Equal(AcceptedDispatchPlanningContextStatus.ContractsUnavailable, result.Status);
        Assert.Equal(0, reservations.CallCount);
    }

    [Fact]
    public async Task NoAcceptedContractDoesNotUseInProgressOperation()
    {
        var reservations = new StubReservations(null);
        var source = new AcceptedDispatchPlanningContextSource(
            new StubContracts(
                true,
                [Persisted(ContractStatus.InProgress)]),
            reservations);

        AcceptedDispatchPlanningContext result = await source.ReadAsync();

        Assert.Equal(AcceptedDispatchPlanningContextStatus.NoAcceptedContract, result.Status);
        Assert.Equal(0, reservations.CallCount);
    }

    [Fact]
    public async Task PersistedCancellationCleanupTakesPriorityOverAcceptedPlanning()
    {
        Guid pendingId =
            Guid.Parse("33333333-3333-3333-3333-333333333333");
        var reservations = new StubReservations(null);
        var source = new AcceptedDispatchPlanningContextSource(
            new StubContracts(
                true,
                [Persisted(ContractStatus.Accepted)]),
            reservations,
            new StubCancellationRecovery([pendingId]));

        AcceptedDispatchPlanningContext result = await source.ReadAsync();

        Assert.Equal(
            AcceptedDispatchPlanningContextStatus.CancellationCleanupPending,
            result.Status);
        Assert.Equal(pendingId, result.ContractId);
        Assert.Equal(
            new[] { pendingId },
            Assert.IsAssignableFrom<IReadOnlyList<Guid>>(
                result.AcceptedContractIds));
        Assert.Contains("cleanup pending", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, reservations.CallCount);
    }

    [Fact]
    public async Task CleanupInspectionFailureBlocksPlanningButRetainsAcceptedCancellation()
    {
        PersistedJobContract accepted = Persisted(ContractStatus.Accepted);
        var reservations = new StubReservations(null);
        var source = new AcceptedDispatchPlanningContextSource(
            new StubContracts(true, [accepted]),
            reservations,
            new FailingCancellationRecovery());

        AcceptedDispatchPlanningContext result = await source.ReadAsync();

        Assert.Equal(
            AcceptedDispatchPlanningContextStatus.CancellationCleanupUnavailable,
            result.Status);
        Assert.Equal(accepted.Contract.ContractId, result.ContractId);
        Assert.Equal(
            new[] { accepted.Contract.ContractId },
            Assert.IsAssignableFrom<IReadOnlyList<Guid>>(
                result.AcceptedContractIds));
        Assert.Contains(
            "Planning is blocked",
            result.Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "available to cancel",
            result.Detail,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            FailingCancellationRecovery.TechnicalMessage,
            result.Detail,
            StringComparison.Ordinal);
        Assert.Equal(0, reservations.CallCount);
    }

    [Fact]
    public async Task MultipleAcceptedContractsAreAmbiguous()
    {
        var reservations = new StubReservations(null);
        var contracts = new StubContracts(
            true,
            [
                Persisted(ContractStatus.Accepted),
                Persisted(
                    ContractStatus.Accepted,
                    "22222222-2222-2222-2222-222222222222")
            ]);
        var source = new AcceptedDispatchPlanningContextSource(
            contracts,
            reservations);

        AcceptedDispatchPlanningContext result = await source.ReadAsync();

        Assert.Equal(AcceptedDispatchPlanningContextStatus.MultipleAcceptedContracts, result.Status);
        Assert.Equal(
            contracts.Current
                .Select(static item => item.Contract.ContractId)
                .OrderBy(static id => id),
            Assert.IsAssignableFrom<IReadOnlyList<Guid>>(result.AcceptedContractIds)
                .OrderBy(static id => id));
        Assert.Equal(0, reservations.CallCount);
    }

    [Fact]
    public async Task MissingReservationRetainsRouteButNotAircraft()
    {
        PersistedJobContract accepted = Persisted(ContractStatus.Accepted);
        var source = new AcceptedDispatchPlanningContextSource(
            new StubContracts(true, [accepted]),
            new StubReservations(null));

        AcceptedDispatchPlanningContext result = await source.ReadAsync();

        Assert.Equal(AcceptedDispatchPlanningContextStatus.ReservationUnavailable, result.Status);
        Assert.Equal("KJFK", result.OriginIcao);
        Assert.Equal("KBOS", result.DestinationIcao);
        Assert.Null(result.CanonicalAircraftId);
        Assert.Equal(
            JobAcceptanceFleetBridge.GetReservationId(accepted.Contract.ContractId),
            result.ReservationId);
    }

    [Fact]
    public async Task OneAcceptedContractAndMatchingReservationAreReady()
    {
        PersistedJobContract accepted = Persisted(ContractStatus.Accepted);
        string reservationId = JobAcceptanceFleetBridge.GetReservationId(
            accepted.Contract.ContractId);
        var source = new AcceptedDispatchPlanningContextSource(
            new StubContracts(true, [accepted]),
            new StubReservations(new("aircraft-a", reservationId)));

        AcceptedDispatchPlanningContext result = await source.ReadAsync();

        Assert.Equal(AcceptedDispatchPlanningContextStatus.Ready, result.Status);
        Assert.Equal(accepted.Contract.ContractId, result.ContractId);
        Assert.Equal(ContractKind.Ferry, result.Kind);
        Assert.Equal("aircraft-a", result.CanonicalAircraftId);
        Assert.Equal(reservationId, result.ReservationId);
        Assert.Equal(187, result.RouteDistanceNauticalMiles);
        Assert.Equal(245, result.PayloadPounds);
        Assert.Contains("payload, distance", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MismatchedReservationIdentityIsRejected()
    {
        PersistedJobContract accepted = Persisted(ContractStatus.Accepted);
        var source = new AcceptedDispatchPlanningContextSource(
            new StubContracts(true, [accepted]),
            new StubReservations(new("aircraft-a", "different-reservation")));

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync());
    }

    private static PersistedJobContract Persisted(
        ContractStatus status,
        string contractId = "11111111-1111-1111-1111-111111111111")
    {
        DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var contract = new JobContract(
            Guid.Parse(contractId),
            null,
            ContractKind.Ferry,
            ServiceTrack.CivilianEmployment,
            "KJFK",
            "KBOS",
            new(
                CompensationModel.PilotWage,
                1000m,
                200m,
                true,
                true,
                true),
            now.AddHours(-1),
            now.AddHours(1),
            now.AddHours(3),
            new AircraftMissionRequirements(),
            status,
            AcceptedAt: status is ContractStatus.Accepted or ContractStatus.InProgress
                ? now.AddMinutes(-30)
                : null,
            StartedAt: status == ContractStatus.InProgress
                ? now.AddMinutes(-10)
                : null,
            PlanningTerms: new(
                RouteDistanceNauticalMiles: 187,
                PayloadPounds: 245));
        contract.Validate();
        return new(contract, 0);
    }

    private sealed class StubContracts(
        bool initialized,
        IReadOnlyList<PersistedJobContract> current)
        : IJobContractRuntimeSource
    {
        public bool IsInitialized { get; } = initialized;
        public IReadOnlyList<PersistedJobContract> Current { get; } = current;

        public PersistedJobContract? Find(Guid contractId) =>
            Current.SingleOrDefault(item => item.Contract.ContractId == contractId);
    }

    private sealed class StubReservations(AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup
    {
        public int CallCount { get; private set; }

        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(ownership);
        }
    }

    private sealed class StubCancellationRecovery(
        IReadOnlyList<Guid> pending)
        : IAcceptedJobCancellationRecoverySource
    {
        public Task<IReadOnlyList<Guid>> ReadPendingContractIdsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(pending);
        }
    }

    private sealed class FailingCancellationRecovery
        : IAcceptedJobCancellationRecoverySource
    {
        public const string TechnicalMessage =
            "sqlite path C:\\Users\\pilot\\OpenCareer.db";

        public Task<IReadOnlyList<Guid>> ReadPendingContractIdsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<IReadOnlyList<Guid>>(
                new IOException(TechnicalMessage));
        }
    }
}
