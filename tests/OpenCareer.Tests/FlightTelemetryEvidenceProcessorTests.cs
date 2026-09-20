using OpenCareer.Application.Flights;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class FlightTelemetryEvidenceProcessorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StableAircraftRequiresConfiguredSampleStreak()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 3));

        FlightStateEvidence first =
            processor.Process(
                Observation(Telemetry(0)));

        FlightStateEvidence second =
            processor.Process(
                Observation(Telemetry(1)));

        FlightStateEvidence third =
            processor.Process(
                Observation(Telemetry(2)));

        Assert.False(first.StableTelemetry);
        Assert.False(second.StableTelemetry);
        Assert.True(third.StableTelemetry);
        Assert.True(third.ValidLoadedAircraft);
    }

    [Fact]
    public void EngineStartAndTaxiAreDerivedFromOperationalTelemetry()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1));

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        enginesRunning: 0)));

        FlightStateEvidence engine =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        enginesRunning: 1)));

        FlightStateEvidence taxi =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        enginesRunning: 1,
                        groundSpeed: 5)));

        Assert.True(engine.EngineStartObserved);
        Assert.True(taxi.SelfPoweredMovementForFlight);
    }

    [Fact]
    public void AirborneConfirmationUsesHysteresis()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1,
                    AirborneConfirmationSamples: 2));

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        enginesRunning: 1,
                        groundSpeed: 35,
                        indicatedAirspeed: 40)));

        FlightStateEvidence firstAirborne =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: false,
                        altitudeAgl: 30,
                        groundSpeed: 55,
                        indicatedAirspeed: 60,
                        enginesRunning: 1)));

        FlightStateEvidence secondAirborne =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 80,
                        groundSpeed: 70,
                        indicatedAirspeed: 72,
                        enginesRunning: 1)));

        Assert.False(firstAirborne.AirborneConfirmed);
        Assert.True(secondAirborne.AirborneConfirmed);
    }

    [Fact]
    public void TouchdownRequiresGroundConfirmationAfterConfirmedAirborneFlight()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1,
                    AirborneConfirmationSamples: 1,
                    GroundConfirmationSamples: 2));

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 500,
                        groundSpeed: 90,
                        indicatedAirspeed: 85,
                        enginesRunning: 1)));

        FlightStateEvidence firstGround =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: true,
                        altitudeAgl: 0,
                        groundSpeed: 55,
                        indicatedAirspeed: 50,
                        enginesRunning: 1)));

        FlightStateEvidence secondGround =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        onGround: true,
                        altitudeAgl: 0,
                        groundSpeed: 45,
                        indicatedAirspeed: 40,
                        enginesRunning: 1)));

        Assert.False(firstGround.TouchdownConfirmed);
        Assert.True(secondGround.TouchdownConfirmed);
    }

    [Fact]
    public void LandingRolloutRequiresConfirmedTouchdownAndLowGroundSpeed()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1,
                    AirborneConfirmationSamples: 1,
                    GroundConfirmationSamples: 2,
                    LandingRolloutMaximumGroundSpeedKnots: 25));

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 500,
                        groundSpeed: 90,
                        indicatedAirspeed: 85,
                        enginesRunning: 1)));

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: true,
                        groundSpeed: 50,
                        indicatedAirspeed: 45,
                        enginesRunning: 1)));

        FlightStateEvidence touchdown =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        onGround: true,
                        groundSpeed: 40,
                        indicatedAirspeed: 35,
                        enginesRunning: 1)));

        FlightStateEvidence fastRollout =
            processor.Process(
                Observation(
                    Telemetry(
                        3,
                        onGround: true,
                        groundSpeed: 30,
                        indicatedAirspeed: 25,
                        enginesRunning: 1)));

        FlightStateEvidence rollout =
            processor.Process(
                Observation(
                    Telemetry(
                        4,
                        onGround: true,
                        groundSpeed: 20,
                        indicatedAirspeed: 15,
                        enginesRunning: 1)));

        Assert.True(touchdown.TouchdownConfirmed);
        Assert.False(touchdown.LandingRolloutConfirmed);
        Assert.False(fastRollout.LandingRolloutConfirmed);
        Assert.True(rollout.LandingRolloutConfirmed);
    }

    [Fact]
    public void RejectedTakeoffRequiresPriorTakeoffCandidate()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1));

        FlightStateEvidence candidate =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        enginesRunning: 1,
                        groundSpeed: 35,
                        indicatedAirspeed: 35)));

        FlightStateEvidence rejected =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        enginesRunning: 1,
                        groundSpeed: 8,
                        indicatedAirspeed: 8)));

        Assert.True(candidate.TakeoffCandidate);
        Assert.True(rejected.RejectedTakeoffConfirmed);
    }

    [Fact]
    public void PauseAndSlewDoNotProduceOperationalTransitions()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1,
                    AirborneConfirmationSamples: 1));

        FlightStateEvidence paused =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 500,
                        groundSpeed: 100,
                        indicatedAirspeed: 100,
                        enginesRunning: 1,
                        paused: true)));

        FlightStateEvidence slewed =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: false,
                        altitudeAgl: 500,
                        groundSpeed: 100,
                        indicatedAirspeed: 100,
                        enginesRunning: 1,
                        slew: true)));

        Assert.False(paused.AirborneConfirmed);
        Assert.False(slewed.AirborneConfirmed);
        Assert.False(paused.SelfPoweredMovementForFlight);
        Assert.False(slewed.SelfPoweredMovementForFlight);
    }

    [Fact]
    public void ParkingAndCompletionRequireStoppedBrakeAndEnginesOff()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1));

        FlightStateEvidence notComplete =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        enginesRunning: 1,
                        groundSpeed: .5,
                        parkingBrake: true),
                    operationCompleteConfirmed: true));

        FlightStateEvidence complete =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        enginesRunning: 0,
                        groundSpeed: .2,
                        parkingBrake: true),
                    operationCompleteConfirmed: true));

        Assert.True(notComplete.ParkingConfirmed);
        Assert.False(notComplete.OperationCompleteConfirmed);

        Assert.True(complete.ParkingConfirmed);
        Assert.True(complete.OperationCompleteConfirmed);
    }

    [Fact]
    public void InvalidNumericTelemetryDoesNotBecomeStableEvidence()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1));

        AircraftTelemetrySnapshot invalid =
            Telemetry(0) with
            {
                GroundSpeedKnots = double.NaN
            };

        FlightStateEvidence evidence =
            processor.Process(
                Observation(invalid));

        Assert.False(evidence.StableTelemetry);
        Assert.False(evidence.ValidLoadedAircraft);
    }

    [Fact]
    public void BackwardTelemetryTimestampIsRejected()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1));

        _ =
            processor.Process(
                Observation(
                    Telemetry(2)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                processor.Process(
                    Observation(
                        Telemetry(1))));
    }

    [Fact]
    public void DisconnectResetsTransientConfirmationStreaks()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 2,
                    AirborneConfirmationSamples: 2));

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 100,
                        groundSpeed: 70,
                        indicatedAirspeed: 70,
                        enginesRunning: 1)));

        FlightStateEvidence disconnected =
            processor.Process(
                new FlightEvidenceObservation(
                    SimulatorConnectionState.Disconnected,
                    Telemetry(1),
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true));

        FlightStateEvidence reconnectFirst =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 100,
                        groundSpeed: 70,
                        indicatedAirspeed: 70,
                        enginesRunning: 1)));

        Assert.False(disconnected.Connected);
        Assert.False(reconnectFirst.StableTelemetry);
        Assert.False(reconnectFirst.AirborneConfirmed);
    }

    private static FlightEvidenceObservation Observation(
        AircraftTelemetrySnapshot telemetry,
        bool operationCompleteConfirmed = false) =>
        new(
            SimulatorConnectionState.Connected,
            telemetry,
            ValidLoadedAircraft: true,
            ContinuityPlausible: true,
            OperationCompleteConfirmed:
                operationCompleteConfirmed);

    private static AircraftTelemetrySnapshot Telemetry(
        int seconds,
        bool onGround = true,
        double altitudeAgl = 0,
        double groundSpeed = 0,
        double indicatedAirspeed = 0,
        int enginesRunning = 0,
        bool parkingBrake = false,
        bool paused = false,
        bool slew = false) =>
        new(
            Epoch.AddSeconds(seconds),
            LatitudeDegrees: 32.0,
            LongitudeDegrees: -97.0,
            AltitudeMslFeet: 600 + altitudeAgl,
            AltitudeAglFeet: altitudeAgl,
            IndicatedAirspeedKnots: indicatedAirspeed,
            GroundSpeedKnots: groundSpeed,
            VerticalSpeedFeetPerMinute:
                onGround ? 0 : -150,
            HeadingDegrees: 180,
            PitchDegrees: 2,
            BankDegrees: 0,
            NormalAccelerationG: 1,
            OnGround: onGround,
            ParkingBrakeSet: parkingBrake,
            EnginesRunning: enginesRunning,
            FuelTotalPounds: 500,
            PayloadPounds: 200,
            FlapsPositionPercent: 0,
            GearDown: true,
            Paused: paused,
            SlewActive: slew);
}
