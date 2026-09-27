using System.Text.Json;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed partial class FlightSessionAircraftAssignmentTests
{
    [Fact]
    public async Task CrashRepairRestoresExplicitJobStartAndRetainsBothHistories()
    {
        var app = await CreateAsync();
        var airframe = await app.CreateAirframeAsync();
        var id = airframe.Airframe.AirframeId;
        var crash = ConsequenceFixture.Session(id, FlightSessionStatus.Interrupted, crash: true);
        var consequences = new FlightAirframeConsequenceCoordinator(app.Airframes, app.Airframes, TimeProvider.System);
        var applied = (await consequences.ApplyAsync(crash))!.Application;
        var offer = await app.RequestAsync();
        Assert.False((await app.StartAction.ReadAvailabilityAsync(offer.Contract.Offer.OfferId, Model, physicalAirframeId: id)).CanStart);
        var repair = await new AirframeMaintenanceService(app.Airframes, app.Airframes).RepairDiscreteDamageAsync(
            new(Guid.NewGuid(), id, applied.After.Revision, applied.After.SavedAt.AddMinutes(1)));
        Assert.True(repair.WasNewlyApplied);
        var source = new AirframeMaintenanceHistorySource(app.Airframes, app.Airframes, app.Airframes);
        var snapshot = (await source.ReadAsync(new(id))).Snapshot!;
        Assert.Equal(AirframeServiceability.AvailableForDispatch, snapshot.Serviceability);
        Assert.Equal(JsonSerializer.Serialize(applied), JsonSerializer.Serialize(Assert.Single(snapshot.History).Application));
        Assert.Equal(repair.Event, Assert.Single((await source.ReadServiceHistoryAsync(new(id))).Snapshot!.History.Events));
        Assert.True((await new PhysicalAirframeEligibilityService(app.Airframes).EvaluateAsync(Model, id)).IsEligible);
        var ready = await app.Inputs.ReadAsync(offer.Contract.Offer.OfferId, Model, physicalAirframeId: id);
        Assert.Equal(CareerJobStartInputState.Ready, ready.State);
        Assert.True((await app.StartAction.ReadAvailabilityAsync(offer.Contract.Offer.OfferId, Model, physicalAirframeId: id)).CanStart);
        var started = await app.StartAction.StartAsync(offer.Contract.Offer.OfferId, Model, physicalAirframeId: id);
        Assert.Equal(id, started.StartedFlight.FlightSession.AircraftIdentity!.PhysicalAirframeId);
        Assert.False((await consequences.ApplyAsync(crash))!.WasNewlyApplied);
        Assert.Equal(repair.Current, await app.Airframes.FindAsync(id));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }
}
