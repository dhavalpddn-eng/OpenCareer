using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class PreparedDispatchStartAuthorityTests
{
    private static readonly Guid ContractId =
        Guid.Parse("a2000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now =
        new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);

    private static string ReservationId =>
        JobAcceptanceFleetBridge.GetReservationId(ContractId);

    [Fact]
    public async Task ExactPreparedPlanIsRevalidatedForStart()
    {
        var planning = new StubPlanning(Feasible());
        var authority = Authority(
            new StubContracts(Persisted()),
            new StubPlans(Plan()),
            new StubReservations(new("aircraft-a", ReservationId)),
            planning);

        PreparedDispatchStartAuthorityResult result =
            await authority.RevalidateAsync(ContractId);

        Assert.True(result.IsReady);
        Assert.Equal(PreparedDispatchStartAuthorityStatus.Ready, result.Status);
        Assert.Equal(1, planning.CallCount);
        Assert.Equal(ReservationId, planning.ReservationId);
        Assert.Null(planning.Requirements!.PlannedFuelPounds);
        Assert.Null(planning.Requirements.PlannedFuelTypeIndex);
        Assert.Equal(250, result.Plan!.Requirements.PayloadPounds);
    }

    [Fact]
    public async Task MissingPreparedPlanFailsClosedBeforePlanning()
    {
        var planning = new StubPlanning(Feasible());
        var authority = Authority(
            new StubContracts(Persisted()),
            new StubPlans(null),
            new StubReservations(new("aircraft-a", ReservationId)),
            planning);

        PreparedDispatchStartAuthorityResult result =
            await authority.RevalidateAsync(ContractId);

        Assert.Equal(
            PreparedDispatchStartAuthorityStatus.PlanUnavailable,
            result.Status);
        Assert.Equal(0, planning.CallCount);
    }

    [Fact]
    public async Task StaleContractVersionFailsClosedBeforePlanning()
    {
        var planning = new StubPlanning(Feasible());
        var authority = Authority(
            new StubContracts(Persisted(version: 2)),
            new StubPlans(Plan(contractVersion: 1)),
            new StubReservations(new("aircraft-a", ReservationId)),
            planning);

        PreparedDispatchStartAuthorityResult result =
            await authority.RevalidateAsync(ContractId);

        Assert.Equal(
            PreparedDispatchStartAuthorityStatus.ContractMismatch,
            result.Status);
        Assert.Equal(0, planning.CallCount);
    }

    [Fact]
    public async Task ReservationAircraftMismatchFailsClosedBeforePlanning()
    {
        var planning = new StubPlanning(Feasible());
        var authority = Authority(
            new StubContracts(Persisted()),
            new StubPlans(Plan()),
            new StubReservations(new("aircraft-b", ReservationId)),
            planning);

        PreparedDispatchStartAuthorityResult result =
            await authority.RevalidateAsync(ContractId);

        Assert.Equal(
            PreparedDispatchStartAuthorityStatus.ReservationMismatch,
            result.Status);
        Assert.Equal(0, planning.CallCount);
    }

    [Fact]
    public async Task ChangedFuelOrRunwayEvidenceMakesPlanStale()
    {
        CompositeDispatchPlanningResult changed = Feasible() with
        {
            FuelWeightPlan = new(0, 6, 31, 186, 50, 300)
        };
        var authority = Authority(
            new StubContracts(Persisted()),
            new StubPlans(Plan()),
            new StubReservations(new("aircraft-a", ReservationId)),
            new StubPlanning(changed));

        PreparedDispatchStartAuthorityResult result =
            await authority.RevalidateAsync(ContractId);

        Assert.Equal(
            PreparedDispatchStartAuthorityStatus.PlanStale,
            result.Status);
        Assert.False(result.IsReady);
    }

    [Fact]
    public async Task ContractChangeDuringRevalidationFailsClosed()
    {
        var authority = Authority(
            new StubContracts(Persisted(version: 1), Persisted(version: 2)),
            new StubPlans(Plan(contractVersion: 1)),
            new StubReservations(new("aircraft-a", ReservationId)),
            new StubPlanning(Feasible()));

        PreparedDispatchStartAuthorityResult result =
            await authority.RevalidateAsync(ContractId);

        Assert.Equal(
            PreparedDispatchStartAuthorityStatus.AuthorityChanged,
            result.Status);
        Assert.False(result.IsReady);
    }

    private static PreparedDispatchStartAuthority Authority(
        IJobContractStore contracts,
        IPreparedDispatchPlanStore plans,
        IAircraftReservationLookup reservations,
        ICompositeDispatchPlanningService planning) =>
        new(contracts, reservations, plans, planning);

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

    private static PreparedDispatchPlan Plan(long contractVersion = 1)
    {
        var routeFuel = new RouteFuelPlanningRequest(
            300,
            260,
            10,
            8,
            3,
            2,
            new(5, 30, 1));
        var fuel = new RouteFuelWeightPlan(0, 6, 30, 180, 50, 300);
        OperationDispatchRequirements requirements = fuel.ApplyTo(new(
            PayloadPounds: 250,
            RequiredRangeNauticalMiles: 300,
            RangeSafetyMarginPercent: 10,
            RunwayLengthSafetyMarginPercent: 15));

        return new(
            ContractId,
            contractVersion,
            "aircraft-a",
            ReservationId,
            "KJFK",
            "KBOS",
            new(0, 0, 6000, 0, 12000),
            routeFuel,
            requirements,
            fuel,
            "04L",
            "22R",
            Now);
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

    private sealed class StubContracts(params PersistedJobContract[] reads)
        : IJobContractStore
    {
        private int _index;

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PersistedJobContract value = reads[Math.Min(_index, reads.Length - 1)];
            _index++;
            return Task.FromResult<PersistedJobContract?>(value);
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

    private sealed class StubPlans(PreparedDispatchPlan? plan)
        : IPreparedDispatchPlanStore
    {
        public Task<PreparedDispatchPlan?> ReadAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(plan);
        }

        public Task<PreparedDispatchPlanSaveResult> SaveAsync(
            PreparedDispatchPlan plan,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearAsync(
            Guid contractId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubReservations(AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup
    {
        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ownership);
        }
    }

    private sealed class StubPlanning(CompositeDispatchPlanningResult result)
        : ICompositeDispatchPlanningService
    {
        public int CallCount { get; private set; }
        public OperationDispatchRequirements? Requirements { get; private set; }
        public string? ReservationId { get; private set; }

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
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Requirements = requirements;
            ReservationId = reservationId;
            return Task.FromResult(result);
        }
    }
}
