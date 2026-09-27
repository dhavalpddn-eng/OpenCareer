using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed partial class FlightSessionAircraftAssignmentTests
{
    [Fact]
    public async Task InspectionDueBlocksBeforeAcceptanceAndInspectionRestoresActualPhysicalStart()
    {
        var app = await CreateAsync();
        var physical = await app.CreateAirframeAsync(damage: AirframeDamageState.Recorded);
        var other = await app.CreateAirframeAsync();
        await ApplyInspectionUsageAsync(app.Airframes, physical);
        physical = (await app.Airframes.FindAsync(physical.Airframe.AirframeId))!;
        var service = (await app.Airframes.ReadServiceStateAsync(physical.Airframe.AirframeId))!;
        var request = (await app.RequestAsync()) with { PhysicalAirframeId = physical.Airframe.AirframeId };
        var offerId = request.Contract.Offer.OfferId;
        var readiness = await app.Inputs.ReadAsync(offerId, Model, physicalAirframeId: request.PhysicalAirframeId);
        Assert.Equal(CareerJobStartInputState.PhysicalAirframeUnavailable, readiness.State);
        Assert.Contains("inspection due", readiness.Detail);
        Assert.False((await app.StartAction.ReadAvailabilityAsync(offerId, Model, physicalAirframeId: request.PhysicalAirframeId)).CanStart);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAction.StartAsync(offerId, Model, physicalAirframeId: request.PhysicalAirframeId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Loop.AcceptAndStartAsync(request));
        await AssertNoAcceptedStateAsync(app, offerId);
        Assert.Equal(physical, await app.Airframes.FindAsync(physical.Airframe.AirframeId));
        Assert.Equal(service, await app.Airframes.ReadServiceStateAsync(physical.Airframe.AirframeId));
        Assert.True((await app.StartAction.ReadAvailabilityAsync(offerId, Model)).CanStart);
        Assert.True((await app.StartAction.ReadAvailabilityAsync(offerId, Model, physicalAirframeId: other.Airframe.AirframeId)).CanStart);
        var inspection = await new AirframeMaintenanceService(app.Airframes, app.Airframes).PerformRoutineInspectionAsync(
            new(Guid.NewGuid(), physical.Airframe.AirframeId, physical.Revision, service.Revision, service.UpdatedAt.AddMinutes(1)));
        Assert.Equal(physical, inspection.Condition);
        Assert.Equal(AirframeDamageState.Recorded, inspection.Condition!.Condition.Damage);
        Assert.True((await app.StartAction.ReadAvailabilityAsync(offerId, Model, physicalAirframeId: request.PhysicalAirframeId)).CanStart);
        var started = await app.StartAction.StartAsync(offerId, Model, physicalAirframeId: request.PhysicalAirframeId);
        Assert.Equal(request.PhysicalAirframeId, started.StartedFlight.FlightSession.AircraftIdentity!.PhysicalAirframeId);
        Assert.Equal(ContractStatus.InProgress, started.StartedFlight.Contract.Contract.Status);
        Assert.Equal(other, await app.Airframes.FindAsync(other.Airframe.AirframeId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectionBecomingDueAtCoordinatorOrStartRecheckFailsClosed(bool afterAcceptance)
    {
        LookupProbe? probe = null;
        var app = await CreateAsync(store => probe = new(store));
        var physical = await app.CreateAirframeAsync();
        var other = await app.CreateAirframeAsync();
        var request = (await app.RequestAsync()) with { PhysicalAirframeId = physical.Airframe.AirframeId };
        var offerId = request.Contract.Offer.OfferId;
        var reservationId = JobAcceptanceFleetBridge.GetReservationId(offerId);
        probe!.BeforeRead = async count =>
        {
            if (count != 2) return;
            if (afterAcceptance) Assert.Equal(ContractStatus.Accepted, (await app.Contracts.ReadJobContractAsync(offerId))!.Contract.Status);
            else await AssertNoAcceptedStateAsync(app, offerId);
            await ApplyInspectionUsageAsync(app.Airframes, physical);
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => afterAcceptance
            ? app.Loop.AcceptAndStartAsync(request)
            : app.StartAction.StartAsync(offerId, Model, physicalAirframeId: physical.Airframe.AirframeId));
        Assert.Contains("inspection due", error.Message);
        Assert.Null(app.Sessions.Current); Assert.Null(await app.Checkpoints.LoadAsync());
        if (afterAcceptance)
        {
            Assert.Equal(ContractStatus.Accepted, (await app.Contracts.ReadJobContractAsync(offerId))!.Contract.Status);
            Assert.NotNull(await app.Fleet.FindByReservationIdAsync(reservationId));
        }
        else await AssertNoAcceptedStateAsync(app, offerId);
        Assert.Equal(other, await app.Airframes.FindAsync(other.Airframe.AirframeId));
        Assert.All(probe.RequestedIds, id => Assert.Equal(physical.Airframe.AirframeId, id));
    }

    private static async Task ApplyInspectionUsageAsync(SqliteAirframeStore store, AirframeStoreRecord airframe)
    {
        var session = ConsequenceFixture.Session(airframe.Airframe.AirframeId);
        session = session with { TimeLedger = session.TimeLedger with { AirborneTime = TimeSpan.FromHours(50), BlockTime = TimeSpan.FromHours(51) },
            UpdatedAt = session.CreatedAt.AddHours(51) };
        await store.ApplyAsync(FlightAirframeConsequenceCalculator.Calculate(session), airframe, session.UpdatedAt);
    }
}
