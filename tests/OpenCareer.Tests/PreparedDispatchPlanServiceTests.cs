using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class PreparedDispatchPlanServiceTests
{
    private static readonly Guid ContractId =
        Guid.Parse("a1000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static string ReservationId =>
        JobAcceptanceFleetBridge.GetReservationId(ContractId);

    [Fact]
    public async Task FeasibleExactAcceptedPlanIsPersisted()
    {
        var plans = new InMemoryPlanStore();
        var planning = new StubPlanning(Feasible());
        var service = Service(
            new StubContracts(Persisted()),
            planning,
            plans);

        PrepareDispatchPlanResult result = await service.PrepareAsync(Request());

        Assert.Equal(PrepareDispatchPlanStatus.Prepared, result.Status);
        PreparedDispatchPlan plan = Assert.IsType<PreparedDispatchPlan>(result.Plan);
        Assert.Equal(ContractId, plan.ContractId);
        Assert.Equal(1, plan.ContractVersion);
        Assert.Equal(180, plan.Requirements.PlannedFuelPounds);
        Assert.Equal(0, plan.Requirements.PlannedFuelTypeIndex);
        Assert.Equal("04L", plan.OriginRunwayIdentifier);
        Assert.Equal("22R", plan.DestinationRunwayIdentifier);
        Assert.Equal(1, planning.CallCount);
        Assert.Equal(1, plans.SaveCount);
    }

    [Fact]
    public async Task ExactRetryIsIdempotent()
    {
        var plans = new InMemoryPlanStore();
        var service = Service(
            new StubContracts(Persisted()),
            new StubPlanning(Feasible()),
            plans);

        PrepareDispatchPlanResult first = await service.PrepareAsync(Request());
        PrepareDispatchPlanResult retry = await service.PrepareAsync(Request());

        Assert.Equal(PrepareDispatchPlanStatus.Prepared, first.Status);
        Assert.Equal(PrepareDispatchPlanStatus.AlreadyPrepared, retry.Status);
        Assert.Equal(first.Plan, retry.Plan);
        Assert.Equal(2, plans.SaveCount);
    }

    [Theory]
    [InlineData("KTEB", "KBOS", 250, 300)]
    [InlineData("KJFK", "KTEB", 250, 300)]
    [InlineData("KJFK", "KBOS", 249, 300)]
    [InlineData("KJFK", "KBOS", 250, 299)]
    public async Task ContractTermMismatchCannotReachPlannerOrStore(
        string origin,
        string destination,
        double payload,
        double distance)
    {
        var planning = new StubPlanning(Feasible());
        var plans = new InMemoryPlanStore();
        var service = Service(
            new StubContracts(Persisted()),
            planning,
            plans);

        PrepareDispatchPlanRequest request = Request() with
        {
            OriginIcao = origin,
            DestinationIcao = destination,
            RouteFuel = Request().RouteFuel with
            {
                TotalRouteDistanceNauticalMiles = distance,
                CruiseSegmentDistanceNauticalMiles = Math.Min(260, distance)
            },
            Requirements = Request().Requirements with
            {
                PayloadPounds = payload,
                RequiredRangeNauticalMiles = distance
            }
        };

        PrepareDispatchPlanResult result = await service.PrepareAsync(request);

        Assert.Equal(PrepareDispatchPlanStatus.ContractMismatch, result.Status);
        Assert.Equal(0, planning.CallCount);
        Assert.Equal(0, plans.SaveCount);
    }

    [Fact]
    public async Task InsufficientPlanCannotBePersisted()
    {
        var plans = new InMemoryPlanStore();
        var service = Service(
            new StubContracts(Persisted()),
            new StubPlanning(new(
                CompositeDispatchPlanningStatus.InsufficientData,
                CompositeDispatchPlanningReason.FuelWeightPlanUnavailable,
                RouteFuelWeightPlanningReason.RouteFuelUnavailable,
                RouteFuelPlanningReason.CruisePerformanceUnavailable,
                CruisePerformancePlanningReason.AircraftDispatchPerformanceUnknown,
                null,
                null,
                null)),
            plans);

        PrepareDispatchPlanResult result = await service.PrepareAsync(Request());

        Assert.Equal(PrepareDispatchPlanStatus.PlanningInsufficient, result.Status);
        Assert.Equal(0, plans.SaveCount);
    }

    [Fact]
    public async Task AuthorityChangeDuringEvaluationCannotBePersisted()
    {
        var plans = new InMemoryPlanStore();
        var service = Service(
            new StubContracts(Persisted(version: 1), Persisted(version: 2)),
            new StubPlanning(Feasible()),
            plans);

        PrepareDispatchPlanResult result = await service.PrepareAsync(Request());

        Assert.Equal(PrepareDispatchPlanStatus.AuthorityChanged, result.Status);
        Assert.Equal(0, plans.SaveCount);
    }

    private static PreparedDispatchPlanService Service(
        IJobContractStore contracts,
        ICompositeDispatchPlanningService planning,
        IPreparedDispatchPlanStore plans) =>
        new(
            contracts,
            new StubReservations(new("aircraft-a", ReservationId)),
            planning,
            plans,
            new FixedTimeProvider(Now));

    private static PrepareDispatchPlanRequest Request()
    {
        var routeFuel = new RouteFuelPlanningRequest(
            300,
            260,
            10,
            8,
            3,
            2,
            new(5, 30, 1));

        return new(
            ContractId,
            "aircraft-a",
            ReservationId,
            "KJFK",
            "KBOS",
            new(0, 0, 6000, 0, 12000),
            routeFuel,
            new(
                PayloadPounds: 250,
                RequiredRangeNauticalMiles: 300,
                RangeSafetyMarginPercent: 10,
                RunwayLengthSafetyMarginPercent: 15));
    }

    private static CompositeDispatchPlanningResult Feasible() =>
        new(
            CompositeDispatchPlanningStatus.Evaluated,
            null,
            null,
            null,
            null,
            null,
            new(0, 6, 30, 180, 50, 300),
            DispatchFeasibilityResult.Create(
                DispatchFeasibilityStatus.Feasible,
                "04L",
                "22R",
                []));

    private static PersistedJobContract Persisted(long version = 1)
    {
        var contract = new JobContract(
            ContractId,
            EmployerId: null,
            ContractKind.Ferry,
            ServiceTrack.CivilianEmployment,
            "KJFK",
            "KBOS",
            new(
                CompensationModel.PilotWage,
                GrossCustomerRevenue: 1000m,
                PilotCompensation: 250m,
                EmployerCoversFuel: true,
                EmployerCoversMaintenance: true,
                EmployerCoversAirportFees: true),
            Now.AddHours(-2),
            MustStartBy: Now.AddHours(2),
            MustCompleteBy: Now.AddHours(4),
            new AircraftMissionRequirements(),
            ContractStatus.Accepted,
            AcceptedAt: Now.AddHours(-1),
            PlanningTerms: new(300, 250));
        contract.Validate();
        return new(contract, version);
    }

    private sealed class StubContracts(params PersistedJobContract[] reads)
        : IJobContractStore
    {
        private int _index;

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PersistedJobContract result = reads[Math.Min(_index, reads.Length - 1)];
            _index++;
            return Task.FromResult<PersistedJobContract?>(result);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubReservations(AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup
    {
        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ownership);
    }

    private sealed class StubPlanning(CompositeDispatchPlanningResult result)
        : ICompositeDispatchPlanningService
    {
        public int CallCount { get; private set; }

        public Task<CompositeDispatchPlanningResult> EvaluateAsync(
            string aircraftId,
            string originIcao,
            string destinationIcao,
            CruisePerformancePlanningRequest cruiseRequest,
            RouteFuelPlanningRequest routeFuelRequest,
            OperationDispatchRequirements requirements,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CompositeDispatchPlanningResult> EvaluateAsync(
            string aircraftId,
            string originIcao,
            string destinationIcao,
            CruisePerformancePlanningRequest cruiseRequest,
            RouteFuelPlanningRequest routeFuelRequest,
            OperationDispatchRequirements requirements,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class InMemoryPlanStore : IPreparedDispatchPlanStore
    {
        private PreparedDispatchPlan? _plan;
        public int SaveCount { get; private set; }

        public Task<PreparedDispatchPlan?> ReadAsync(
            Guid contractId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _plan?.ContractId == contractId
                    ? _plan
                    : null);

        public Task<PreparedDispatchPlanSaveResult> SaveAsync(
            PreparedDispatchPlan plan,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (_plan == plan)
            {
                return Task.FromResult(
                    PreparedDispatchPlanSaveResult.AlreadySaved);
            }

            PreparedDispatchPlanSaveResult result = _plan is null
                ? PreparedDispatchPlanSaveResult.Created
                : PreparedDispatchPlanSaveResult.Replaced;
            _plan = plan;
            return Task.FromResult(result);
        }

        public Task ClearAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            if (_plan?.ContractId == contractId)
                _plan = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
