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
    public void InitialClimbRequiresPriorAirborneConfirmationAndSustainedClimb()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1,
                    AirborneConfirmationSamples: 1,
                    InitialClimbConfirmationSamples: 2,
                    InitialClimbMinimumAglFeet: 200,
                    InitialClimbMinimumVerticalSpeedFeetPerMinute: 100));

        FlightStateEvidence takeoff =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 50,
                        groundSpeed: 70,
                        indicatedAirspeed: 70,
                        enginesRunning: 1,
                        verticalSpeed: 600)));

        FlightStateEvidence firstClimb =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: false,
                        altitudeAgl: 220,
                        groundSpeed: 75,
                        indicatedAirspeed: 72,
                        enginesRunning: 1,
                        verticalSpeed: 600)));

        FlightStateEvidence interruptedClimb =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 250,
                        groundSpeed: 76,
                        indicatedAirspeed: 73,
                        enginesRunning: 1,
                        verticalSpeed: 50)));

        FlightStateEvidence restartedClimb =
            processor.Process(
                Observation(
                    Telemetry(
                        3,
                        onGround: false,
                        altitudeAgl: 275,
                        groundSpeed: 77,
                        indicatedAirspeed: 74,
                        enginesRunning: 1,
                        verticalSpeed: 500)));

        FlightStateEvidence confirmedClimb =
            processor.Process(
                Observation(
                    Telemetry(
                        4,
                        onGround: false,
                        altitudeAgl: 320,
                        groundSpeed: 78,
                        indicatedAirspeed: 75,
                        enginesRunning: 1,
                        verticalSpeed: 450)));

        Assert.True(takeoff.AirborneConfirmed);
        Assert.False(takeoff.InitialClimbConfirmed);
        Assert.False(firstClimb.InitialClimbConfirmed);
        Assert.False(interruptedClimb.InitialClimbConfirmed);
        Assert.False(restartedClimb.InitialClimbConfirmed);
        Assert.True(confirmedClimb.InitialClimbConfirmed);
    }

    [Fact]
    public void MissionFlightProgressRequiresDistanceAfterInitialClimbAndIgnoresPausedJump()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                new FlightEvidenceProcessorOptions(
                    StableTelemetrySamples: 1,
                    AirborneConfirmationSamples: 1,
                    InitialClimbConfirmationSamples: 1,
                    MissionFlightConfirmationSamples: 3,
                    MissionFlightMinimumDistanceNauticalMiles: 0.5));

        FlightStateEvidence takeoff =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 50,
                        groundSpeed: 90,
                        indicatedAirspeed: 85,
                        enginesRunning: 1,
                        verticalSpeed: 700,
                        latitude: 32.0)));

        FlightStateEvidence climb =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: false,
                        altitudeAgl: 250,
                        groundSpeed: 95,
                        indicatedAirspeed: 90,
                        enginesRunning: 1,
                        verticalSpeed: 600,
                        latitude: 32.0)));

        FlightStateEvidence pausedJump =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 300,
                        groundSpeed: 100,
                        indicatedAirspeed: 95,
                        enginesRunning: 1,
                        paused: true,
                        verticalSpeed: 400,
                        latitude: 33.0)));

        FlightStateEvidence resume =
            processor.Process(
                Observation(
                    Telemetry(
                        3,
                        onGround: false,
                        altitudeAgl: 320,
                        groundSpeed: 100,
                        indicatedAirspeed: 95,
                        enginesRunning: 1,
                        verticalSpeed: 300,
                        latitude: 33.0)));

        FlightStateEvidence progressOne =
            processor.Process(
                Observation(
                    Telemetry(
                        4,
                        onGround: false,
                        altitudeAgl: 330,
                        groundSpeed: 100,
                        indicatedAirspeed: 95,
                        enginesRunning: 1,
                        verticalSpeed: 250,
                        latitude: 33.003)));

        FlightStateEvidence progressTwo =
            processor.Process(
                Observation(
                    Telemetry(
                        5,
                        onGround: false,
                        altitudeAgl: 340,
                        groundSpeed: 100,
                        indicatedAirspeed: 95,
                        enginesRunning: 1,
                        verticalSpeed: 200,
                        latitude: 33.006)));

        FlightStateEvidence confirmed =
            processor.Process(
                Observation(
                    Telemetry(
                        6,
                        onGround: false,
                        altitudeAgl: 350,
                        groundSpeed: 100,
                        indicatedAirspeed: 95,
                        enginesRunning: 1,
                        verticalSpeed: 150,
                        latitude: 33.009)));

        Assert.True(takeoff.AirborneConfirmed);
        Assert.True(climb.InitialClimbConfirmed);
        Assert.False(climb.MissionFlightProgressConfirmed);
        Assert.False(pausedJump.MissionFlightProgressConfirmed);
        Assert.False(resume.MissionFlightProgressConfirmed);
        Assert.False(progressOne.MissionFlightProgressConfirmed);
        Assert.False(progressTwo.MissionFlightProgressConfirmed);
        Assert.True(confirmed.MissionFlightProgressConfirmed);
    }

    [Fact]
    public void ApproachRequiresMissionProgressAndSustainedOperationalDescent()
    {
        var processor = new FlightTelemetryEvidenceProcessor(
            new FlightEvidenceProcessorOptions(
                StableTelemetrySamples: 1,
                AirborneConfirmationSamples: 1,
                InitialClimbConfirmationSamples: 1,
                MissionFlightConfirmationSamples: 2,
                MissionFlightMinimumDistanceNauticalMiles: 0.5));

        FlightStateEvidence earlyDescent = processor.Process(Observation(Telemetry(
            0, onGround: false, altitudeAgl: 500, groundSpeed: 90,
            verticalSpeed: -300, enginesRunning: 1)));
        Assert.False(earlyDescent.ApproachConfirmed);

        _ = processor.Process(Observation(Telemetry(
            1, onGround: false, altitudeAgl: 600, groundSpeed: 95,
            verticalSpeed: 400, enginesRunning: 1)));
        _ = processor.Process(Observation(Telemetry(
            2, onGround: false, altitudeAgl: 800, groundSpeed: 95,
            verticalSpeed: 400, enginesRunning: 1)));
        FlightStateEvidence progress = processor.Process(Observation(Telemetry(
            3, onGround: false, altitudeAgl: 1500, groundSpeed: 95,
            verticalSpeed: 400, enginesRunning: 1, latitude: 32.01)));
        Assert.True(progress.MissionFlightProgressConfirmed);
        Assert.False(progress.ApproachConfirmed);

        FlightStateEvidence firstDescent = processor.Process(Observation(Telemetry(
            4, onGround: false, altitudeAgl: 1800, groundSpeed: 85,
            verticalSpeed: -250, enginesRunning: 1)));
        FlightStateEvidence paused = processor.Process(Observation(Telemetry(
            5, onGround: false, altitudeAgl: 1700, groundSpeed: 85,
            verticalSpeed: -250, enginesRunning: 1, paused: true)));
        FlightStateEvidence resumed = processor.Process(Observation(Telemetry(
            6, onGround: false, altitudeAgl: 1600, groundSpeed: 85,
            verticalSpeed: -250, enginesRunning: 1)));
        FlightStateEvidence confirmed = processor.Process(Observation(Telemetry(
            7, onGround: false, altitudeAgl: 1500, groundSpeed: 85,
            verticalSpeed: -250, enginesRunning: 1)));

        Assert.False(firstDescent.ApproachConfirmed);
        Assert.False(paused.ApproachConfirmed);
        Assert.False(resumed.ApproachConfirmed);
        Assert.True(confirmed.ApproachConfirmed);
    }

    [Fact]
    public void ApproachStreakResetsAcrossDisconnectAndRestoreUsesPersistedProgress()
    {
        var processor = new FlightTelemetryEvidenceProcessor(
            new FlightEvidenceProcessorOptions(StableTelemetrySamples: 1));
        FlightSession session = FlightSession.Start(Epoch) with
        {
            Milestones = new FlightSessionMilestones(
                InitialClimbAt: Epoch.AddSeconds(1),
                MissionFlightProgressAt: Epoch.AddSeconds(2)),
            Tracking = FlightTrackingSnapshot.Start(Epoch) with
            {
                State = FlightTrackingState.Airborne,
                UpdatedAt = Epoch.AddSeconds(2)
            }
        };
        processor.RestoreContext(session);

        FlightStateEvidence first = processor.Process(Observation(Telemetry(
            3, onGround: false, altitudeAgl: 1000, groundSpeed: 90,
            verticalSpeed: -200, enginesRunning: 1)));
        FlightStateEvidence disconnected = processor.Process(
            new FlightEvidenceObservation(
                SimulatorConnectionState.Disconnected, null,
                ValidLoadedAircraft: true, ContinuityPlausible: true));
        FlightStateEvidence resumed = processor.Process(Observation(Telemetry(
            4, onGround: false, altitudeAgl: 900, groundSpeed: 90,
            verticalSpeed: -200, enginesRunning: 1)));
        FlightStateEvidence confirmed = processor.Process(Observation(Telemetry(
            5, onGround: false, altitudeAgl: 800, groundSpeed: 90,
            verticalSpeed: -200, enginesRunning: 1)));

        Assert.False(first.ApproachConfirmed);
        Assert.False(disconnected.ApproachConfirmed);
        Assert.False(resumed.ApproachConfirmed);
        Assert.True(confirmed.ApproachConfirmed);
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
    public void LandingRolloutRequiresTouchdownAndContinuedSlowGroundContact()
    {
        var processor = new FlightTelemetryEvidenceProcessor(
            new FlightEvidenceProcessorOptions(
                StableTelemetrySamples: 1,
                AirborneConfirmationSamples: 1,
                GroundConfirmationSamples: 2));

        _ = processor.Process(Observation(Telemetry(
            0, onGround: false, altitudeAgl: 500, groundSpeed: 90)));
        FlightStateEvidence firstGround = processor.Process(Observation(Telemetry(
            1, groundSpeed: 55)));
        FlightStateEvidence touchdown = processor.Process(Observation(Telemetry(
            2, groundSpeed: 45)));
        FlightStateEvidence fastRollout = processor.Process(Observation(Telemetry(
            3, groundSpeed: 30)));
        FlightStateEvidence slowRollout = processor.Process(Observation(Telemetry(
            4, groundSpeed: 18)));

        Assert.False(firstGround.LandingRolloutConfirmed);
        Assert.True(touchdown.TouchdownConfirmed);
        Assert.False(touchdown.LandingRolloutConfirmed);
        Assert.False(fastRollout.LandingRolloutConfirmed);
        Assert.True(slowRollout.LandingRolloutConfirmed);
    }

    [Fact]
    public void BounceAndPauseDoNotConfirmLandingRollout()
    {
        var processor = new FlightTelemetryEvidenceProcessor(
            new FlightEvidenceProcessorOptions(
                StableTelemetrySamples: 1,
                AirborneConfirmationSamples: 1,
                GroundConfirmationSamples: 2));

        _ = processor.Process(Observation(Telemetry(
            0, onGround: false, altitudeAgl: 200, groundSpeed: 90)));
        _ = processor.Process(Observation(Telemetry(1, groundSpeed: 50)));
        _ = processor.Process(Observation(Telemetry(2, groundSpeed: 40)));
        FlightStateEvidence bounce = processor.Process(Observation(Telemetry(
            3, onGround: false, altitudeAgl: 25, groundSpeed: 35)));
        FlightStateEvidence paused = processor.Process(Observation(Telemetry(
            4, groundSpeed: 15, paused: true)));
        FlightStateEvidence recontact = processor.Process(Observation(Telemetry(
            5, groundSpeed: 18)));

        Assert.False(bounce.LandingRolloutConfirmed);
        Assert.False(paused.LandingRolloutConfirmed);
        Assert.False(recontact.LandingRolloutConfirmed);
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
        bool slew = false,
        double? verticalSpeed = null,
        double latitude = 32.0,
        double longitude = -97.0) =>
        new(
            Epoch.AddSeconds(seconds),
            LatitudeDegrees: latitude,
            LongitudeDegrees: longitude,
            AltitudeMslFeet: 600 + altitudeAgl,
            AltitudeAglFeet: altitudeAgl,
            IndicatedAirspeedKnots: indicatedAirspeed,
            GroundSpeedKnots: groundSpeed,
            VerticalSpeedFeetPerMinute:
                verticalSpeed
                ?? (onGround ? 0 : -150),
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
