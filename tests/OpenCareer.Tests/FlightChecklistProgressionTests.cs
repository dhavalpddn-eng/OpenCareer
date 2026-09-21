using OpenCareer.Application.Checklists;
using OpenCareer.Domain.Checklists;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightChecklistProgressionTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StandardGroundFlowAdvancesFromPreflightThroughShutdown()
    {
        var checklist = new FlightChecklistProgression();

        Assert.Equal(
            FlightChecklistPhase.Preflight,
            checklist.Current.CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.EngineStart,
            checklist.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.TaxiOut,
            checklist.Process(
                Evidence(
                    1,
                    engineStartObserved: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.Takeoff,
            checklist.Process(
                Evidence(
                    2,
                    selfPoweredMovementForFlight: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.Airborne,
            checklist.Process(
                Evidence(
                    3,
                    takeoffCandidate: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.Approach,
            checklist.Process(
                Evidence(
                    4,
                    airborneConfirmed: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.Landing,
            checklist.Process(
                Evidence(
                    5,
                    approachConfirmed: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.TaxiIn,
            checklist.Process(
                Evidence(
                    6,
                    touchdownConfirmed: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.TaxiIn,
            checklist.Process(
                Evidence(
                    7,
                    landingRolloutConfirmed: true))
                .CurrentPhase);

        Assert.Equal(
            FlightChecklistPhase.Shutdown,
            checklist.Process(
                Evidence(
                    8,
                    parkingConfirmed: true))
                .CurrentPhase);

        FlightChecklistSnapshot complete =
            checklist.Process(
                Evidence(
                    9,
                    operationCompleteConfirmed: true));

        Assert.True(complete.IsComplete);
        Assert.All(
            complete.Steps,
            static step =>
                Assert.Equal(
                    FlightChecklistStepState.Verified,
                    step.State));
    }

    [Fact]
    public void OutOfOrderEvidenceDoesNotSkipUnverifiedPrerequisites()
    {
        var checklist = new FlightChecklistProgression();

        FlightChecklistSnapshot result =
            checklist.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true,
                    airborneConfirmed: true,
                    approachConfirmed: true,
                    touchdownConfirmed: true,
                    parkingConfirmed: true,
                    operationCompleteConfirmed: true));

        Assert.Equal(
            FlightChecklistPhase.EngineStart,
            result.CurrentPhase);

        Assert.Equal(
            FlightChecklistStepState.Verified,
            Step(result, FlightChecklistStepId.AircraftReady).State);

        Assert.Equal(
            FlightChecklistStepState.Pending,
            Step(result, FlightChecklistStepId.EngineStarted).State);

        Assert.Equal(
            FlightChecklistStepState.Pending,
            Step(result, FlightChecklistStepId.ShutdownConfirmed).State);
    }

    [Fact]
    public void DisconnectDoesNotClearLatchedProgress()
    {
        var checklist = new FlightChecklistProgression();

        _ =
            checklist.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true));

        FlightChecklistSnapshot disconnected =
            checklist.Process(
                Evidence(
                    1,
                    connected: false));

        Assert.Equal(
            FlightChecklistPhase.EngineStart,
            disconnected.CurrentPhase);

        Assert.Equal(
            FlightChecklistStepState.Verified,
            Step(disconnected, FlightChecklistStepId.AircraftReady).State);

        Assert.Equal(
            Epoch,
            Step(disconnected, FlightChecklistStepId.AircraftReady).VerifiedAt);
    }

    [Fact]
    public void ContiguousSignalsMayVerifyMultipleStepsOnOneObservation()
    {
        var checklist = new FlightChecklistProgression();

        FlightChecklistSnapshot result =
            checklist.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true,
                    engineStartObserved: true,
                    selfPoweredMovementForFlight: true,
                    takeoffCandidate: true));

        Assert.Equal(
            FlightChecklistPhase.Airborne,
            result.CurrentPhase);

        Assert.Equal(
            FlightChecklistStepState.Verified,
            Step(result, FlightChecklistStepId.TakeoffRollEstablished).State);
    }

    [Fact]
    public void ManualOnlyStepDoesNotAcceptAutomaticEvidence()
    {
        var checklist =
            new FlightChecklistProgression(
                new Dictionary<FlightChecklistStepId, FlightChecklistVerificationCapability>
                {
                    [FlightChecklistStepId.EngineStarted] =
                        FlightChecklistVerificationCapability.ManualOnly
                });

        _ =
            checklist.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true));

        FlightChecklistSnapshot automatic =
            checklist.Process(
                Evidence(
                    1,
                    engineStartObserved: true));

        FlightChecklistStepSnapshot engine =
            Step(
                automatic,
                FlightChecklistStepId.EngineStarted);

        Assert.Equal(
            FlightChecklistVerificationCapability.ManualOnly,
            engine.VerificationCapability);
        Assert.Equal(
            FlightChecklistStepState.Pending,
            engine.State);
        Assert.Equal(
            FlightChecklistPhase.EngineStart,
            automatic.CurrentPhase);

        FlightChecklistSnapshot manual =
            checklist.ConfirmManual(
                FlightChecklistStepId.EngineStarted,
                Epoch.AddSeconds(2));

        Assert.Equal(
            FlightChecklistStepState.Verified,
            Step(
                manual,
                FlightChecklistStepId.EngineStarted)
                .State);
        Assert.Equal(
            FlightChecklistPhase.TaxiOut,
            manual.CurrentPhase);
    }

    [Fact]
    public void UnavailableStepCannotBeVerifiedAutomaticallyOrManually()
    {
        var checklist =
            new FlightChecklistProgression(
                new Dictionary<FlightChecklistStepId, FlightChecklistVerificationCapability>
                {
                    [FlightChecklistStepId.EngineStarted] =
                        FlightChecklistVerificationCapability.Unavailable
                });

        _ =
            checklist.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true));

        FlightChecklistSnapshot automatic =
            checklist.Process(
                Evidence(
                    1,
                    engineStartObserved: true));

        FlightChecklistStepSnapshot engine =
            Step(
                automatic,
                FlightChecklistStepId.EngineStarted);

        Assert.Equal(
            FlightChecklistVerificationCapability.Unavailable,
            engine.VerificationCapability);
        Assert.Equal(
            FlightChecklistStepState.Pending,
            engine.State);

        Assert.Throws<InvalidOperationException>(
            () =>
                checklist.ConfirmManual(
                    FlightChecklistStepId.EngineStarted,
                    Epoch.AddSeconds(2)));
    }

    [Fact]
    public void AutomaticStepRejectsManualSubstitution()
    {
        var checklist = new FlightChecklistProgression();

        Assert.Throws<InvalidOperationException>(
            () =>
                checklist.ConfirmManual(
                    FlightChecklistStepId.AircraftReady,
                    Epoch));
    }

    [Fact]
    public void ManualConfirmationCannotSkipPrerequisites()
    {
        var checklist =
            new FlightChecklistProgression(
                new Dictionary<FlightChecklistStepId, FlightChecklistVerificationCapability>
                {
                    [FlightChecklistStepId.EngineStarted] =
                        FlightChecklistVerificationCapability.ManualOnly
                });

        Assert.Throws<InvalidOperationException>(
            () =>
                checklist.ConfirmManual(
                    FlightChecklistStepId.EngineStarted,
                    Epoch));
    }

    [Fact]
    public void BackwardEvidenceTimestampIsRejected()
    {
        var checklist = new FlightChecklistProgression();

        _ =
            checklist.Process(
                Evidence(
                    2,
                    stableTelemetry: true,
                    validLoadedAircraft: true));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                checklist.Process(
                    Evidence(1)));
    }

    [Fact]
    public void ResetReturnsChecklistToPreflight()
    {
        var checklist = new FlightChecklistProgression();

        _ =
            checklist.Process(
                Evidence(
                    0,
                    stableTelemetry: true,
                    validLoadedAircraft: true,
                    engineStartObserved: true));

        FlightChecklistSnapshot reset =
            checklist.Reset();

        Assert.Equal(
            FlightChecklistPhase.Preflight,
            reset.CurrentPhase);

        Assert.All(
            reset.Steps,
            static step =>
            {
                Assert.Equal(
                    FlightChecklistStepState.Pending,
                    step.State);
                Assert.Null(step.VerifiedAt);
            });
    }

    private static FlightChecklistStepSnapshot Step(
        FlightChecklistSnapshot snapshot,
        FlightChecklistStepId id) =>
        Assert.Single(
            snapshot.Steps,
            step => step.Id == id);

    private static FlightStateEvidence Evidence(
        int seconds,
        bool connected = true,
        bool stableTelemetry = false,
        bool validLoadedAircraft = false,
        bool engineStartObserved = false,
        bool selfPoweredMovementForFlight = false,
        bool takeoffCandidate = false,
        bool airborneConfirmed = false,
        bool approachConfirmed = false,
        bool touchdownConfirmed = false,
        bool landingRolloutConfirmed = false,
        bool parkingConfirmed = false,
        bool operationCompleteConfirmed = false) =>
        new(
            Epoch.AddSeconds(seconds),
            Connected: connected,
            StableTelemetry: stableTelemetry,
            ValidLoadedAircraft: validLoadedAircraft,
            EngineStartObserved: engineStartObserved,
            SelfPoweredMovementForFlight: selfPoweredMovementForFlight,
            TakeoffCandidate: takeoffCandidate,
            AirborneConfirmed: airborneConfirmed,
            ApproachConfirmed: approachConfirmed,
            TouchdownConfirmed: touchdownConfirmed,
            LandingRolloutConfirmed: landingRolloutConfirmed,
            ParkingConfirmed: parkingConfirmed,
            OperationCompleteConfirmed: operationCompleteConfirmed);
}
