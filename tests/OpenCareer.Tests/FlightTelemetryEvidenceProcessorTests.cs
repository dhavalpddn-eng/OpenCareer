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

    [Theory]
    [InlineData(8, 1, false, true)]
    [InlineData(18, 2, false, true)]
    [InlineData(100, 2, false, false)]
    [InlineData(8, 6, false, false)]
    [InlineData(8, 1, true, false)]
    public void BounceRequiresBoundedUninterruptedContactEvidence(
        double bounceAgl, int airborneSeconds, bool pauseDuringBounce, bool expectedBounce)
    {
        var processor = new FlightTelemetryEvidenceProcessor();
        for (int second = 0; second < 4; second++)
            processor.Process(Observation(Telemetry(second, onGround: false, altitudeAgl: 100)));
        var contact = processor.Process(Observation(Telemetry(4)));
        Assert.False(contact.TouchdownConfirmed);
        Assert.False(contact.BounceRecontact);
        for (int second = 5; second < 5 + airborneSeconds; second++)
            processor.Process(Observation(Telemetry(second, onGround: false,
                altitudeAgl: bounceAgl, paused: pauseDuringBounce)));
        var recontact = processor.Process(Observation(Telemetry(5 + airborneSeconds)));
        Assert.False(recontact.TouchdownConfirmed);
        Assert.False(recontact.BounceRecontact);
        var confirmed = processor.Process(Observation(Telemetry(6 + airborneSeconds)));
        Assert.True(confirmed.TouchdownConfirmed);
        Assert.Equal(expectedBounce, confirmed.BounceRecontact);
        Assert.False(processor.Process(Observation(Telemetry(7 + airborneSeconds))).BounceRecontact);
    }

    [Fact]
    public void OneSecondSamplingMissesSubsecondBounceThatFrequentSamplesDetect()
    {
        AircraftTelemetrySnapshot Sample(
            int milliseconds,
            bool onGround,
            double altitudeAgl) =>
            Telemetry(
                0,
                onGround: onGround,
                altitudeAgl: altitudeAgl,
                groundSpeed: onGround ? 45 : 70,
                indicatedAirspeed: onGround ? 40 : 70,
                enginesRunning: 1) with
            {
                Timestamp = Epoch.AddMilliseconds(milliseconds)
            };

        AircraftTelemetrySnapshot[] physicalSequence =
        [
            Sample(-1_000, onGround: false, altitudeAgl: 100),
            Sample(-750, onGround: false, altitudeAgl: 100),
            Sample(-500, onGround: false, altitudeAgl: 100),
            Sample(-250, onGround: false, altitudeAgl: 100),
            Sample(0, onGround: false, altitudeAgl: 100),
            Sample(250, onGround: false, altitudeAgl: 100),
            Sample(500, onGround: false, altitudeAgl: 100),
            Sample(750, onGround: false, altitudeAgl: 100),
            Sample(1_000, onGround: true, altitudeAgl: 0),
            Sample(1_250, onGround: false, altitudeAgl: 8),
            Sample(1_500, onGround: true, altitudeAgl: 0),
            Sample(1_750, onGround: true, altitudeAgl: 0),
            Sample(2_000, onGround: true, altitudeAgl: 0)
        ];

        static FlightStateEvidence[] Replay(
            IEnumerable<AircraftTelemetrySnapshot> samples)
        {
            var processor = new FlightTelemetryEvidenceProcessor();
            return samples
                .Select(sample => processor.Process(Observation(sample)))
                .ToArray();
        }

        static FlightTrackingSnapshot Track(
            IEnumerable<FlightStateEvidence> evidence)
        {
            var tracking =
                new FlightTrackingSnapshot(
                    FlightTrackingState.Airborne,
                    SuspendedFrom: null,
                    UpdatedAt: Epoch.AddSeconds(-2),
                    TakeoffCount: 1,
                    LandingEpisodeCount: 0,
                    BounceCount: 0,
                    TouchAndGoCount: 0,
                    RejectedTakeoffCount: 0,
                    CrashReported: false);

            foreach (FlightStateEvidence sample in evidence)
                tracking = FlightTrackingStateMachine.Advance(tracking, sample);

            return tracking;
        }

        FlightStateEvidence[] oneSecondEvidence =
            Replay(
                physicalSequence.Where(sample =>
                    (sample.Timestamp - Epoch).TotalMilliseconds % 1_000 == 0));
        FlightStateEvidence[] frequentEvidence = Replay(physicalSequence);

        FlightTrackingSnapshot oneSecondTracking = Track(oneSecondEvidence);
        FlightTrackingSnapshot frequentTracking = Track(frequentEvidence);

        Assert.Single(oneSecondEvidence, static evidence => evidence.TouchdownConfirmed);
        Assert.DoesNotContain(oneSecondEvidence, static evidence => evidence.BounceRecontact);
        Assert.Equal(1, oneSecondTracking.LandingEpisodeCount);
        Assert.Equal(0, oneSecondTracking.BounceCount);

        FlightStateEvidence detectedBounce =
            Assert.Single(frequentEvidence, static evidence => evidence.BounceRecontact);
        Assert.True(detectedBounce.TouchdownConfirmed);
        Assert.Equal(Epoch.AddMilliseconds(1_750), detectedBounce.Timestamp);
        Assert.Single(frequentEvidence, static evidence => evidence.TouchdownConfirmed);
        Assert.Equal(1, frequentTracking.LandingEpisodeCount);
        Assert.Equal(1, frequentTracking.BounceCount);
    }

    [Fact]
    public void ReconnectDoesNotInventBounceFromTheLastGroundContact()
    {
        var processor = new FlightTelemetryEvidenceProcessor();
        for (int second = 0; second < 4; second++)
            processor.Process(Observation(Telemetry(second, onGround: false, altitudeAgl: 100)));
        processor.Process(Observation(Telemetry(4)));
        processor.Process(new FlightEvidenceObservation(SimulatorConnectionState.Reconnecting, null, ValidLoadedAircraft: false, ContinuityPlausible: false));
        processor.Process(Observation(Telemetry(5, onGround: false, altitudeAgl: 8)));
        processor.Process(Observation(Telemetry(6)));
        Assert.False(processor.Process(Observation(Telemetry(7))).BounceRecontact);
    }

    [Fact]
    public void SustainedClimbAfterApproachConfirmsOneGoAroundAndAllowsLaterLanding()
    {
        var processor = GoAroundProcessor();
        FlightTrackingSnapshot tracking = AirborneTracking();
        var evidence = new List<FlightStateEvidence>();

        foreach (AircraftTelemetrySnapshot sample in new[]
        {
            Telemetry(0, onGround: false, altitudeAgl: 600, verticalSpeed: -300),
            Telemetry(1, onGround: false, altitudeAgl: 560, verticalSpeed: -300),
            Telemetry(2, onGround: false, altitudeAgl: 580, verticalSpeed: 500),
            Telemetry(3, onGround: false, altitudeAgl: 620, verticalSpeed: 500),
            Telemetry(4, onGround: false, altitudeAgl: 670, verticalSpeed: 500),
            Telemetry(5, onGround: false, altitudeAgl: 800, verticalSpeed: 500)
        })
        {
            FlightStateEvidence next = processor.Process(Observation(sample));
            evidence.Add(next);
            tracking = FlightTrackingStateMachine.Advance(tracking, next);
        }

        FlightStateEvidence goAround =
            Assert.Single(evidence, static item => item.GoAroundConfirmed);

        Assert.Equal(Epoch.AddSeconds(4), goAround.Timestamp);
        Assert.Equal(FlightTrackingState.Airborne, tracking.State);
        Assert.Equal(0, tracking.LandingEpisodeCount);

        FlightStateEvidence nextApproach =
            processor.Process(
                Observation(
                    Telemetry(
                        6,
                        onGround: false,
                        altitudeAgl: 500,
                        verticalSpeed: -300)));
        tracking = FlightTrackingStateMachine.Advance(tracking, nextApproach);

        FlightStateEvidence firstContact =
            processor.Process(Observation(Telemetry(7)));
        tracking = FlightTrackingStateMachine.Advance(tracking, firstContact);

        FlightStateEvidence touchdown =
            processor.Process(Observation(Telemetry(8)));
        tracking = FlightTrackingStateMachine.Advance(tracking, touchdown);

        Assert.True(nextApproach.ApproachConfirmed);
        Assert.Equal(FlightTrackingState.LandingEpisode, tracking.State);
        Assert.Equal(1, tracking.LandingEpisodeCount);
        Assert.Equal(0, tracking.BounceCount);
    }

    [Fact]
    public void BriefClimbAfterApproachDoesNotConfirmGoAround()
    {
        var processor = GoAroundProcessor();

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 500,
                        verticalSpeed: -300)));

        FlightStateEvidence briefClimb =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: false,
                        altitudeAgl: 620,
                        verticalSpeed: 500)));

        FlightStateEvidence climbEnded =
            processor.Process(
                Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 650,
                        verticalSpeed: 0)));

        Assert.False(briefClimb.GoAroundConfirmed);
        Assert.False(climbEnded.GoAroundConfirmed);
    }

    [Fact]
    public void ClimbWithoutPriorApproachDoesNotConfirmGoAround()
    {
        var processor = GoAroundProcessor();
        var evidence = new List<FlightStateEvidence>();

        for (int second = 0; second < 5; second++)
        {
            evidence.Add(
                processor.Process(
                    Observation(
                        Telemetry(
                            second,
                            onGround: false,
                            altitudeAgl: 2_500 + (second * 100),
                            verticalSpeed: 500))));
        }

        Assert.DoesNotContain(evidence, static item => item.ApproachConfirmed);
        Assert.DoesNotContain(evidence, static item => item.GoAroundConfirmed);
    }

    [Fact]
    public void GroundContactAndBounceCannotConfirmApproachGoAround()
    {
        var processor =
            new FlightTelemetryEvidenceProcessor(
                GoAroundOptions() with
                {
                    GroundConfirmationSamples = 2
                });
        var evidence = new List<FlightStateEvidence>();

        foreach (AircraftTelemetrySnapshot sample in new[]
        {
            Telemetry(0, onGround: false, altitudeAgl: 100, verticalSpeed: -300),
            Telemetry(1),
            Telemetry(2, onGround: false, altitudeAgl: 20, verticalSpeed: 600),
            Telemetry(3),
            Telemetry(4)
        })
        {
            evidence.Add(processor.Process(Observation(sample)));
        }

        Assert.DoesNotContain(evidence, static item => item.GoAroundConfirmed);
        FlightStateEvidence bounce =
            Assert.Single(evidence, static item => item.BounceRecontact);
        Assert.True(bounce.TouchdownConfirmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InterruptedGoAroundCandidateRequiresANewApproach(int interruption)
    {
        var processor = GoAroundProcessor();

        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        0,
                        onGround: false,
                        altitudeAgl: 500,
                        verticalSpeed: -300)));
        _ =
            processor.Process(
                Observation(
                    Telemetry(
                        1,
                        onGround: false,
                        altitudeAgl: 540,
                        verticalSpeed: 500)));

        FlightEvidenceObservation interrupted =
            interruption switch
            {
                0 => Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 580,
                        verticalSpeed: 500,
                        paused: true)),
                1 => Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 580,
                        verticalSpeed: 500,
                        slew: true)),
                2 => new FlightEvidenceObservation(
                    SimulatorConnectionState.Disconnected,
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 580,
                        verticalSpeed: 500),
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: false),
                3 => Observation(
                    Telemetry(
                        5,
                        onGround: false,
                        altitudeAgl: 700,
                        verticalSpeed: 500)),
                4 => Observation(
                    Telemetry(
                        2,
                        onGround: false,
                        altitudeAgl: 580,
                        verticalSpeed: 500),
                    continuityPlausible: false),
                _ => throw new ArgumentOutOfRangeException(nameof(interruption))
            };

        _ = processor.Process(interrupted);

        var laterClimb = new List<FlightStateEvidence>();
        int firstSecond = interruption == 3 ? 6 : 3;
        for (int offset = 0; offset < 4; offset++)
        {
            laterClimb.Add(
                processor.Process(
                    Observation(
                        Telemetry(
                            firstSecond + offset,
                            onGround: false,
                            altitudeAgl: 800 + (offset * 100),
                            verticalSpeed: 500))));
        }

        Assert.DoesNotContain(laterClimb, static item => item.GoAroundConfirmed);
    }

    private static FlightTelemetryEvidenceProcessor GoAroundProcessor() =>
        new(GoAroundOptions());

    private static FlightEvidenceProcessorOptions GoAroundOptions() =>
        new(
            StableTelemetrySamples: 1,
            AirborneConfirmationSamples: 1,
            GoAroundMinimumClimbFeetPerMinute: 300,
            GoAroundMinimumAglGainFeet: 100,
            GoAroundMinimumClimbSeconds: 2,
            GoAroundMaximumTelemetryGapSeconds: 2);

    private static FlightTrackingSnapshot AirborneTracking() =>
        new(
            FlightTrackingState.Airborne,
            SuspendedFrom: null,
            UpdatedAt: Epoch.AddSeconds(-1),
            TakeoffCount: 1,
            LandingEpisodeCount: 0,
            BounceCount: 0,
            TouchAndGoCount: 0,
            RejectedTakeoffCount: 0,
            CrashReported: false);

    private static FlightEvidenceObservation Observation(
        AircraftTelemetrySnapshot telemetry,
        bool operationCompleteConfirmed = false,
        bool continuityPlausible = true) =>
        new(
            SimulatorConnectionState.Connected,
            telemetry,
            ValidLoadedAircraft: true,
            ContinuityPlausible:
                continuityPlausible,
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
        double? verticalSpeed = null) =>
        new(
            Epoch.AddSeconds(seconds),
            LatitudeDegrees: 32.0,
            LongitudeDegrees: -97.0,
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
