using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed partial class FlightSessionAircraftAssignmentTests
{
    [Theory]
    [InlineData(AirframeDamageState.None)]
    [InlineData(AirframeDamageState.Recorded)]
    public async Task PhysicalStartActionAllowsNonGroundingDamageEvenAtMaximumWear(AirframeDamageState damage)
    {
        var app = await CreateAsync();
        var initial = await app.CreateAirframeAsync(damage: damage);
        var selected = await app.Airframes.UpdateConditionAsync(initial.Airframe, new(1, damage), initial.Revision, Epoch);
        var unrelated = await app.CreateAirframeAsync();
        var request = await app.RequestAsync();
        var offerId = request.Contract.Offer.OfferId;
        var ready = await app.Inputs.ReadAsync(offerId, Model, physicalAirframeId: selected.Airframe.AirframeId);
        Assert.Equal(CareerJobStartInputState.Ready, ready.State);
        Assert.Equal(selected.Airframe.AirframeId, ready.Request!.PhysicalAirframeId);
        Assert.True((await app.StartAction.ReadAvailabilityAsync(offerId, Model, physicalAirframeId: selected.Airframe.AirframeId)).CanStart);
        var started = await app.StartAction.StartAsync(offerId, Model, physicalAirframeId: selected.Airframe.AirframeId);
        Assert.Equal(ContractStatus.InProgress, started.StartedFlight.Contract.Contract.Status);
        Assert.Equal(selected.Airframe.AirframeId, started.StartedFlight.FlightSession.AircraftIdentity!.PhysicalAirframeId);
        Assert.Equal(selected.Airframe.AirframeId, (await app.Checkpoints.LoadAsync())!.AircraftIdentity!.PhysicalAirframeId);
        Assert.NotNull(await app.Fleet.FindByReservationIdAsync(started.Dispatch.FleetResult.ReservationId));
        Assert.Equal(selected, await app.Airframes.FindAsync(selected.Airframe.AirframeId));
        Assert.Equal(unrelated, await app.Airframes.FindAsync(unrelated.Airframe.AirframeId));
    }

    [Theory]
    [InlineData("grounded")]
    [InlineData("missing")]
    [InlineData("model-mismatch")]
    [InlineData("wrong-record")]
    [InlineData("missing-authority")]
    [InlineData("default")]
    public async Task PhysicalReadinessAndDirectStartFailBeforeAcceptanceOrReservation(string fault)
    {
        LookupProbe? probe = null;
        var app = await CreateAsync(store => probe = new(store), configureEligibility: fault != "missing-authority");
        var selected = await app.CreateAirframeAsync(
            model: fault == "model-mismatch" ? "other-model" : Model,
            damage: fault == "grounded" ? AirframeDamageState.Grounding : AirframeDamageState.None);
        var unrelated = await app.CreateAirframeAsync();
        if (fault == "wrong-record") probe!.OverrideRecord = unrelated;
        AirframeId id = fault == "missing" ? new(Guid.NewGuid()) : fault == "default" ? default : selected.Airframe.AirframeId;
        var request = (await app.RequestAsync()) with { PhysicalAirframeId = id };
        var offerId = request.Contract.Offer.OfferId;
        if (fault == "default")
        {
            await Assert.ThrowsAsync<ArgumentException>(() => app.Inputs.ReadAsync(offerId, Model, physicalAirframeId: id));
            await Assert.ThrowsAsync<ArgumentException>(() => app.StartAction.StartAsync(offerId, Model, physicalAirframeId: id));
            await Assert.ThrowsAsync<ArgumentException>(() => app.Loop.AcceptAndStartAsync(request));
            Assert.Empty(probe!.RequestedIds);
        }
        else
        {
            var ready = await app.Inputs.ReadAsync(offerId, Model, physicalAirframeId: id);
            Assert.Equal(CareerJobStartInputState.PhysicalAirframeUnavailable, ready.State);
            Assert.False(ready.IsReady);
            Assert.Null(ready.Request);
            var availability = await app.StartAction.ReadAvailabilityAsync(offerId, Model, physicalAirframeId: id);
            Assert.False(availability.CanStart);
            Assert.Equal(CareerJobStartInputState.PhysicalAirframeUnavailable, availability.State);
            if (fault == "grounded") Assert.Contains("grounded", availability.Detail);
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAction.StartAsync(offerId, Model, physicalAirframeId: id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Loop.AcceptAndStartAsync(request));
        }
        await AssertNoAcceptedStateAsync(app, offerId);
        Assert.Equal(selected, await app.Airframes.FindAsync(selected.Airframe.AirframeId));
        Assert.Equal(unrelated, await app.Airframes.FindAsync(unrelated.Airframe.AirframeId));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM airframes;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelOnlyReadinessAndStartRequireNeitherPhysicalLookupNorAssignment(bool configureEligibility)
    {
        LookupProbe? probe = null;
        var app = await CreateAsync(store => probe = new(store) { FailReads = true }, configureEligibility);
        var request = await app.RequestAsync();
        Assert.Null(request.PhysicalAirframeId);
        Assert.True((await app.StartAction.ReadAvailabilityAsync(request.Contract.Offer.OfferId, Model)).CanStart);
        var started = await app.StartAction.StartAsync(request.Contract.Offer.OfferId, Model);
        Assert.Equal(Model, started.StartedFlight.FlightSession.AircraftIdentity!.CanonicalAircraftId);
        Assert.Null(started.StartedFlight.FlightSession.AircraftIdentity.PhysicalAirframeId);
        Assert.Equal(ContractStatus.InProgress, started.StartedFlight.Contract.Contract.Status);
        if (probe is not null) Assert.Empty(probe.RequestedIds);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframes;"));
    }

    [Fact]
    public async Task GroundingAfterReadinessIsCaughtBeforeCoordinatorAcceptance()
    {
        LookupProbe? probe = null;
        var app = await CreateAsync(store => probe = new(store));
        var physical = await app.CreateAirframeAsync();
        var request = await app.RequestAsync();
        probe!.BeforeRead = async count =>
        {
            if (count != 2) return; // action readiness succeeded; coordinator must refresh before acceptance
            await AssertNoAcceptedStateAsync(app, request.Contract.Offer.OfferId);
            await app.Airframes.UpdateConditionAsync(physical.Airframe, new(0.2, AirframeDamageState.Grounding), physical.Revision, Epoch);
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAction.StartAsync(
            request.Contract.Offer.OfferId, Model, physicalAirframeId: physical.Airframe.AirframeId));
        Assert.Equal(2, probe.RequestedIds.Count);
        await AssertNoAcceptedStateAsync(app, request.Contract.Offer.OfferId);
    }

    [Fact]
    public async Task GroundingAfterAcceptanceIsCaughtByStartRecheckWithoutFallbackOrInProgressTransition()
    {
        LookupProbe? probe = null;
        var app = await CreateAsync(store => probe = new(store));
        var physical = await app.CreateAirframeAsync();
        var healthyAlternative = await app.CreateAirframeAsync();
        var request = (await app.RequestAsync()) with { PhysicalAirframeId = physical.Airframe.AirframeId };
        var offerId = request.Contract.Offer.OfferId;
        var reservationId = JobAcceptanceFleetBridge.GetReservationId(offerId);
        probe!.BeforeRead = async count =>
        {
            if (count != 2) return;
            Assert.Equal(ContractStatus.Accepted, (await app.Contracts.ReadJobContractAsync(offerId))!.Contract.Status);
            Assert.NotNull(await app.Fleet.FindByReservationIdAsync(reservationId));
            Assert.Null(app.Sessions.Current);
            await app.Airframes.UpdateConditionAsync(physical.Airframe, new(0.2, AirframeDamageState.Grounding), physical.Revision, Epoch);
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Loop.AcceptAndStartAsync(request));
        Assert.Contains("grounded", error.Message);
        Assert.Equal(2, probe.RequestedIds.Count);
        Assert.All(probe.RequestedIds, id => Assert.Equal(physical.Airframe.AirframeId, id));
        var accepted = (await app.Contracts.ReadJobContractAsync(offerId))!.Contract;
        Assert.Equal(ContractStatus.Accepted, accepted.Status);
        Assert.Null(accepted.StartedAt);
        Assert.Equal(Model, (await app.Fleet.FindByReservationIdAsync(reservationId))!.CanonicalAircraftId);
        Assert.Null(app.Sessions.Current);
        Assert.Null(await app.Checkpoints.LoadAsync());
        Assert.Equal(healthyAlternative, await app.Airframes.FindAsync(healthyAlternative.Airframe.AirframeId));
        var grounded = (await app.Airframes.FindAsync(physical.Airframe.AirframeId))!;
        Assert.Equal(2, grounded.Revision); // only the injected condition change, never an eligibility write
        Assert.True(grounded.Condition.RequiresGrounding);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Loop.AcceptAndStartAsync(request));
        Assert.Equal(accepted, (await app.Contracts.ReadJobContractAsync(offerId))!.Contract);
        Assert.Equal(grounded, await app.Airframes.FindAsync(physical.Airframe.AirframeId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrashConsequenceBlocksOnlyItsPhysicalAirframeAndPreservesModelOnlyOperation(bool chooseHealthyPhysical)
    {
        var app = await CreateAsync();
        var crashed = await app.CreateAirframeAsync();
        var healthy = await app.CreateAirframeAsync();
        var crashSession = ConsequenceFixture.Session(crashed.Airframe.AirframeId, FlightSessionStatus.Interrupted, crash: true);
        var consequences = new FlightAirframeConsequenceCoordinator(app.Airframes, app.Airframes, TimeProvider.System);
        var applied = (await consequences.ApplyAsync(crashSession))!;
        Assert.Equal(FlightDamageSeverity.Severe, applied.Application.Consequence.Severity);
        Assert.True(applied.Application.After.Condition.RequiresGrounding);
        var request = await app.RequestAsync();
        Assert.False((await app.StartAction.ReadAvailabilityAsync(request.Contract.Offer.OfferId, Model,
            physicalAirframeId: crashed.Airframe.AirframeId)).CanStart);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Loop.AcceptAndStartAsync(request with
        { PhysicalAirframeId = crashed.Airframe.AirframeId }));
        await AssertNoAcceptedStateAsync(app, request.Contract.Offer.OfferId);
        AirframeId? selected = chooseHealthyPhysical ? healthy.Airframe.AirframeId : null;
        var ready = await app.Inputs.ReadAsync(request.Contract.Offer.OfferId, Model, physicalAirframeId: selected);
        Assert.Equal(CareerJobStartInputState.Ready, ready.State);
        var started = await app.StartAction.StartAsync(request.Contract.Offer.OfferId, Model, physicalAirframeId: selected);
        Assert.Equal(selected, started.StartedFlight.FlightSession.AircraftIdentity!.PhysicalAirframeId);
        Assert.Equal(applied.Application.After, await app.Airframes.FindAsync(crashed.Airframe.AirframeId));
        Assert.Equal(healthy, await app.Airframes.FindAsync(healthy.Airframe.AirframeId));
        Assert.False((await consequences.ApplyAsync(crashSession))!.WasNewlyApplied);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
        Assert.Equal(16L, await ScalarAsync("PRAGMA user_version;"));
    }

    [Fact]
    public async Task SharedPhysicalEligibilityIsRegisteredInProduction()
    {
        string registration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<PhysicalAirframeEligibilityService>()", registration);
    }

    private static async Task AssertNoAcceptedStateAsync(Harness app, Guid offerId)
    {
        Assert.Null(await app.Contracts.ReadJobContractAsync(offerId));
        Assert.Null(await app.Fleet.FindByReservationIdAsync(JobAcceptanceFleetBridge.GetReservationId(offerId)));
        Assert.Null(await app.Checkpoints.LoadAsync());
        Assert.Null(app.Sessions.Current);
    }

    // Test-only observation hook around the real SQLite authority; mutation happens at the precise
    // readiness/acceptance boundary. Stores, reservation, dispatch and session orchestration are real.
    private sealed class LookupProbe(IAirframeStore inner) : IAirframeStore
    {
        public List<AirframeId> RequestedIds { get; } = [];
        public Func<int, Task>? BeforeRead { get; set; }
        public AirframeStoreRecord? OverrideRecord { get; set; }
        public bool FailReads { get; init; }
        public async Task<AirframeStoreRecord?> FindAsync(AirframeId airframeId, CancellationToken cancellationToken = default)
        {
            RequestedIds.Add(airframeId);
            if (FailReads) throw new InvalidOperationException("Model-only path looked up a physical aircraft.");
            if (BeforeRead is not null) await BeforeRead(RequestedIds.Count);
            return OverrideRecord ?? await inner.FindAsync(airframeId, cancellationToken);
        }
        public Task<AirframeStoreRecord> CreateAsync(Airframe airframe, AirframeCondition condition, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AirframeStoreRecord> UpdateConditionAsync(Airframe airframe, AirframeCondition condition, long expectedRevision, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
