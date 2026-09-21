using OpenCareer.Application.Checklists;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Checklists;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightChecklistCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartSelectsAndLocksProfileForFlight()
    {
        var coordinator =
            new FlightChecklistCoordinator();

        FlightChecklistSnapshot started =
            coordinator.Start(
                new FlightChecklistSelectionContext(
                    Aircraft(
                        AircraftCapability.Helicopter
                        | AircraftCapability.Passenger)));

        Assert.True(coordinator.IsActive);
        Assert.Equal(
            "helicopter",
            coordinator.ActiveProfileId);
        Assert.DoesNotContain(
            started.Steps,
            static step =>
                step.Id
                    == FlightChecklistStepId.TakeoffRollEstablished);

        Assert.Throws<InvalidOperationException>(
            () =>
                coordinator.Start(
                    new FlightChecklistSelectionContext(
                        Aircraft())));
    }

    [Fact]
    public void EvidenceFlowsThroughLockedProgression()
    {
        var coordinator =
            new FlightChecklistCoordinator();

        _ =
            coordinator.Start(
                new FlightChecklistSelectionContext(
                    Aircraft()));

        FlightChecklistSnapshot result =
            coordinator.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true));

        Assert.Equal(
            FlightChecklistPhase.EngineStart,
            result.CurrentPhase);

        Assert.NotNull(
            coordinator.Current);
        Assert.Equal(
            result.CurrentPhase,
            coordinator.Current!.CurrentPhase);
        Assert.Equal(
            result.Steps.Select(static step => step.Id),
            coordinator.Current.Steps.Select(static step => step.Id));
        Assert.Equal(
            "standard",
            coordinator.ActiveProfileId);
    }

    [Fact]
    public void AuthorizedStartProfileRemainsLockedAfterEvidenceChanges()
    {
        var coordinator =
            new FlightChecklistCoordinator();

        FlightChecklistSnapshot started =
            coordinator.Start(
                new FlightChecklistSelectionContext(
                    Aircraft(),
                    AuthorizedAirborneStart: true));

        Assert.Equal(
            "standard-airborne-start",
            coordinator.ActiveProfileId);

        FlightChecklistSnapshot updated =
            coordinator.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true,
                    engineStartObserved: true,
                    selfPoweredMovementForFlight: true,
                    takeoffCandidate: true));

        Assert.Equal(
            "standard-airborne-start",
            coordinator.ActiveProfileId);

        Assert.Equal(
            started.Steps.Select(static step => step.Id),
            updated.Steps.Select(static step => step.Id));

        Assert.DoesNotContain(
            updated.Steps,
            static step =>
                step.Id == FlightChecklistStepId.EngineStarted);
    }

    [Fact]
    public void EndReturnsFinalSnapshotAndAllowsNextFlight()
    {
        var coordinator =
            new FlightChecklistCoordinator();

        _ =
            coordinator.Start(
                new FlightChecklistSelectionContext(
                    Aircraft()));

        FlightChecklistSnapshot progressed =
            coordinator.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true));

        FlightChecklistSnapshot ended =
            coordinator.End();

        Assert.Equal(
            progressed.CurrentPhase,
            ended.CurrentPhase);
        Assert.Equal(
            progressed.Steps.Select(static step => step.State),
            ended.Steps.Select(static step => step.State));
        Assert.False(coordinator.IsActive);
        Assert.Null(coordinator.ActiveProfileId);
        Assert.Null(coordinator.Current);

        FlightChecklistSnapshot next =
            coordinator.Start(
                new FlightChecklistSelectionContext(
                    Aircraft(AircraftCapability.Helicopter)));

        Assert.Equal(
            FlightChecklistPhase.Preflight,
            next.CurrentPhase);
        Assert.Equal(
            "helicopter",
            coordinator.ActiveProfileId);
    }

    [Fact]
    public void OperationsRequireActiveChecklist()
    {
        var coordinator =
            new FlightChecklistCoordinator();

        Assert.Throws<InvalidOperationException>(
            () =>
                coordinator.Process(
                    Evidence(0)));

        Assert.Throws<InvalidOperationException>(
            () =>
                coordinator.ConfirmManual(
                    FlightChecklistStepId.EngineStarted,
                    Epoch));

        Assert.Throws<InvalidOperationException>(
            () => coordinator.End());
    }

    private static AircraftCapabilityProfile Aircraft(
        AircraftCapability capabilities =
            AircraftCapability.Passenger) =>
        new(
            AircraftId: "test-aircraft",
            DisplayName: "Test Aircraft",
            Capabilities: capabilities,
            Access: AircraftAccess.Civilian,
            MaximumPayloadPounds: 1200,
            MaximumRangeNauticalMiles: 600,
            TypicalCruiseKnots: 120,
            Seats: 4,
            EngineCount: 1,
            IfrCapable: true,
            Pressurized: false,
            RetractableGear: false);

    private static FlightStateEvidence Evidence(
        int seconds,
        bool stableTelemetry = false,
        bool validLoadedAircraft = false,
        bool engineStartObserved = false,
        bool selfPoweredMovementForFlight = false,
        bool takeoffCandidate = false) =>
        new(
            Epoch.AddSeconds(seconds),
            Connected: true,
            StableTelemetry: stableTelemetry,
            ValidLoadedAircraft: validLoadedAircraft,
            EngineStartObserved: engineStartObserved,
            SelfPoweredMovementForFlight: selfPoweredMovementForFlight,
            TakeoffCandidate: takeoffCandidate);
}
