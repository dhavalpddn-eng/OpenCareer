using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class FlightEvidenceProcessorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LoadingSentinelCannotCreateFalseFlight()
    {
        var processor = new FlightEvidenceProcessor();
        var state = FlightTrackingSnapshot.Start(Epoch);

        for (int second = 0; second <= 15; second++)
        {
            var telemetry = Sample(
                second,
                latitude: 0,
                longitude: 90,
                agl: 206,
                onGround: false,
                fuel: 0,
                engines: 0);

            var evidence = processor.Process(
                connected: true,
                telemetry,
                telemetry.Timestamp);
            state = FlightTrackingStateMachine.Advance(state, evidence);
        }

        Assert.Equal(FlightTrackingState.Observing, state.State);
        Assert.Equal(0, state.TakeoffCount);
    }

    [Fact]
    public void NormalFlightProducesStableStateProgression()
    {
        var processor = new FlightEvidenceProcessor();
        var state = FlightTrackingSnapshot.Start(Epoch);

        for (int second = 0; second <= 4; second++)
            state = Step(processor, state, Sample(second, fuel: 100));

        Assert.Equal(FlightTrackingState.Preflight, state.State);

        state = Step(processor, state, Sample(5, fuel: 100, engines: 1));
        Assert.Equal(FlightTrackingState.EngineStart, state.State);

        for (int second = 6; second <= 9; second++)
            state = Step(processor, state, Sample(second, fuel: 100, engines: 1, gs: 8));

        Assert.Equal(FlightTrackingState.TaxiOut, state.State);

        for (int second = 10; second <= 13; second++)
            state = Step(processor, state, Sample(second, fuel: 100, engines: 1, gs: 35));

        Assert.Equal(FlightTrackingState.TakeoffRoll, state.State);

        for (int second = 14; second <= 17; second++)
        {
            state = Step(
                processor,
                state,
                Sample(second, fuel: 100, engines: 1, gs: 70, ias: 65, agl: 40, onGround: false));
        }

        Assert.Equal(FlightTrackingState.Airborne, state.State);
        Assert.Equal(1, state.TakeoffCount);

        for (int second = 18; second <= 21; second++)
        {
            state = Step(
                processor,
                state,
                Sample(second, fuel: 95, engines: 1, gs: 75, ias: 70, agl: 700, vs: -500, onGround: false));
        }

        Assert.Equal(FlightTrackingState.Approach, state.State);

        state = Step(
            processor,
            state,
            Sample(22, fuel: 94, engines: 1, gs: 55, ias: 50, agl: 5, onGround: true));

        Assert.Equal(FlightTrackingState.LandingEpisode, state.State);
        Assert.Equal(1, state.LandingEpisodeCount);

        for (int second = 23; second <= 27; second++)
            state = Step(processor, state, Sample(second, fuel: 94, engines: 1, gs: 30, agl: 5));

        Assert.Equal(FlightTrackingState.TaxiIn, state.State);

        for (int second = 28; second <= 32; second++)
        {
            state = Step(
                processor,
                state,
                Sample(second, fuel: 94, engines: 1, gs: 0, agl: 5, parkingBrake: true));
        }

        Assert.Equal(FlightTrackingState.Parked, state.State);
    }

    [Fact]
    public void InitialClimbSinkDoesNotPrematurelyArmApproach()
    {
        var processor = new FlightEvidenceProcessor();
        var state = FlightTrackingSnapshot.Start(Epoch);

        for (int second = 0; second <= 4; second++)
            state = Step(processor, state, Sample(second, fuel: 100, engines: 1));

        for (int second = 5; second <= 8; second++)
            state = Step(processor, state, Sample(second, fuel: 100, engines: 1, gs: 35));

        for (int second = 9; second <= 12; second++)
        {
            state = Step(
                processor,
                state,
                Sample(second, fuel: 100, engines: 1, gs: 75, ias: 70, agl: 50 + (second - 9) * 100, vs: 900, onGround: false));
        }

        Assert.Equal(FlightTrackingState.Airborne, state.State);

        for (int second = 13; second <= 16; second++)
        {
            state = Step(
                processor,
                state,
                Sample(second, fuel: 98, engines: 1, gs: 75, ias: 70, agl: 380 - (second - 13) * 8, vs: -300, onGround: false));
        }

        Assert.Equal(FlightTrackingState.Airborne, state.State);

        for (int second = 17; second <= 20; second++)
        {
            state = Step(
                processor,
                state,
                Sample(second, fuel: 96, engines: 1, gs: 70, ias: 65, agl: 120, vs: -500, onGround: false));
        }

        Assert.Equal(FlightTrackingState.Approach, state.State);
    }

    [Fact]
    public void RejectedTakeoffDoesNotCreateTakeoff()
    {
        var processor = new FlightEvidenceProcessor();
        var state = FlightTrackingSnapshot.Start(Epoch);

        for (int second = 0; second <= 4; second++)
            state = Step(processor, state, Sample(second, fuel: 100, engines: 1));

        for (int second = 5; second <= 8; second++)
            state = Step(processor, state, Sample(second, fuel: 100, engines: 1, gs: 35));

        Assert.Equal(FlightTrackingState.TakeoffRoll, state.State);

        for (int second = 9; second <= 13; second++)
            state = Step(processor, state, Sample(second, fuel: 100, engines: 1, gs: 5));

        Assert.Equal(FlightTrackingState.TaxiOut, state.State);
        Assert.Equal(0, state.TakeoffCount);
        Assert.Equal(1, state.RejectedTakeoffCount);
    }

    [Fact]
    public void DisconnectRequiresPlausibleContinuityBeforeResume()
    {
        var processor = new FlightEvidenceProcessor(
            new FlightEvidenceProcessorOptions
            {
                StableTelemetryDuration = TimeSpan.Zero
            });

        var first = Sample(0, latitude: 43.2, longitude: -75.4, fuel: 100);
        var connected = processor.Process(true, first, first.Timestamp);
        Assert.True(connected.ContinuityPlausible);

        var disconnected = processor.Process(
            false,
            null,
            Epoch.AddSeconds(1));
        Assert.False(disconnected.Connected);

        var nearby = Sample(60, latitude: 43.21, longitude: -75.39, fuel: 100);
        var resumed = processor.Process(true, nearby, nearby.Timestamp);
        Assert.True(resumed.ContinuityPlausible);
    }

    private static FlightTrackingSnapshot Step(
        FlightEvidenceProcessor processor,
        FlightTrackingSnapshot state,
        AircraftTelemetrySnapshot telemetry)
    {
        var evidence = processor.Process(
            connected: true,
            telemetry,
            telemetry.Timestamp);
        return FlightTrackingStateMachine.Advance(state, evidence);
    }

    private static AircraftTelemetrySnapshot Sample(
        int seconds,
        double latitude = 43.21935,
        double longitude = -75.39547,
        double agl = 5,
        double ias = 0,
        double gs = 0,
        double vs = 0,
        bool onGround = true,
        bool parkingBrake = false,
        int engines = 0,
        double fuel = 0) =>
        new(
            Epoch.AddSeconds(seconds),
            latitude,
            longitude,
            AltitudeMslFeet: 485 + agl,
            AltitudeAglFeet: agl,
            IndicatedAirspeedKnots: ias,
            GroundSpeedKnots: gs,
            VerticalSpeedFeetPerMinute: vs,
            HeadingDegrees: 130,
            PitchDegrees: 0,
            BankDegrees: 0,
            NormalAccelerationG: 1,
            OnGround: onGround,
            ParkingBrakeSet: parkingBrake,
            EnginesRunning: engines,
            FuelTotalPounds: fuel,
            PayloadPounds: 250,
            FlapsPositionPercent: 0,
            GearDown: true,
            Paused: false,
            SlewActive: false);
}
