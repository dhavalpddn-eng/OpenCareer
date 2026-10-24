using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class FlightContinuityPolicyTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GroundRecoveryAcceptsSameAirportAndRejectsDistantTeleport()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            GroundSessionWithAnchor(
                32.0,
                -97.0,
                650);

        Assert.True(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddMinutes(10),
                    32.02,
                    -97.01,
                    660,
                    onGround: true)));

        Assert.False(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddMinutes(10),
                    33.0,
                    -96.0,
                    600,
                    onGround: true)));
    }

    [Fact]
    public void GroundRecoveryDoesNotAcceptNearbyAirborneAircraftWithoutPriorTakeoff()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            GroundSessionWithAnchor(
                32.0,
                -97.0,
                650);

        Assert.False(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddMinutes(5),
                    32.01,
                    -97.0,
                    675,
                    onGround: false)));
    }

    [Fact]
    public void GroundAnchorAllowsBriefAirborneBounceDuringApproach()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            ApproachSessionWithGroundAnchor(
                32.0,
                -97.0,
                650);

        Assert.True(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddSeconds(7),
                    32.0005,
                    -97.0005,
                    675,
                    onGround: false)));
    }

    [Fact]
    public void LandingEpisodeAllowsRepeatedPlausibleBounceTransitions()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            LandingEpisodeSessionWithGroundAnchor(
                32.0,
                -97.0,
                650);

        Assert.True(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddSeconds(7),
                    32.0005,
                    -97.0005,
                    675,
                    onGround: false)));

        var secondContact =
            new FlightContinuityAnchor(
                Epoch.AddSeconds(8),
                32.001,
                -97.001,
                650,
                OnGround: true);

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(8),
                        Connected: true,
                        ContinuityPlausible: true,
                        BounceRecontact: true),
                    ContinuityAnchor:
                        secondContact));

        Assert.Equal(
            FlightTrackingState.LandingEpisode,
            session.Tracking.State);
        Assert.Equal(1, session.Tracking.BounceCount);

        Assert.True(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddSeconds(9),
                    32.0015,
                    -97.0015,
                    670,
                    onGround: false)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LandingEpisodeAllowsTouchAndGoOrGoAroundContinuation(
        bool touchAndGo)
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            LandingEpisodeSessionWithGroundAnchor(
                32.0,
                -97.0,
                650);

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(7),
                        Connected: true,
                        ContinuityPlausible: true,
                        TouchAndGoConfirmed:
                            touchAndGo,
                        GoAroundConfirmed:
                            !touchAndGo)));

        Assert.Equal(
            FlightTrackingState.Airborne,
            session.Tracking.State);

        Assert.True(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddSeconds(8),
                    32.0005,
                    -97.0005,
                    675,
                    onGround: false)));
    }

    [Fact]
    public void LandingEpisodeRejectsDistantAirborneReposition()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            LandingEpisodeSessionWithGroundAnchor(
                32.0,
                -97.0,
                650);

        Assert.False(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddSeconds(7),
                    33.0,
                    -96.0,
                    675,
                    onGround: false)));
    }

    [Fact]
    public void AirborneRecoveryAllowsPlausibleTravelDuringAppOutage()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            AirborneSessionWithAnchor(
                32.0,
                -97.0,
                10_000);

        Assert.True(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddMinutes(30),
                    34.0,
                    -96.0,
                    18_000,
                    onGround: false)));
    }

    [Fact]
    public void AirborneRecoveryRejectsImplausibleLongDistanceJump()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            AirborneSessionWithAnchor(
                32.0,
                -97.0,
                10_000);

        Assert.False(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddMinutes(5),
                    40.7,
                    -74.0,
                    12_000,
                    onGround: false)));
    }

    [Fact]
    public void LegacyGroundCheckpointWithoutAnchorCanRecoverConservatively()
    {
        var policy =
            new FlightContinuityPolicy();

        FlightSession session =
            FlightSession.Start(Epoch);

        Assert.True(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddMinutes(1),
                    32.0,
                    -97.0,
                    650,
                    onGround: true)));

        Assert.False(
            policy.IsPlausible(
                session,
                Telemetry(
                    Epoch.AddMinutes(1),
                    32.0,
                    -97.0,
                    2_000,
                    onGround: false)));
    }

    private static FlightSession GroundSessionWithAnchor(
        double latitude,
        double longitude,
        double altitude) =>
        FlightSession.Start(Epoch)
        with
        {
            ContinuityAnchor =
                new FlightContinuityAnchor(
                    Epoch,
                    latitude,
                    longitude,
                    altitude,
                    OnGround: true)
        };

    private static FlightSession AirborneSessionWithAnchor(
        double latitude,
        double longitude,
        double altitude)
    {
        FlightSession session =
            FlightSession.Start(Epoch);

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true,
                        StableTelemetry: true,
                        ValidLoadedAircraft: true,
                        ContinuityPlausible: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(2),
                        Connected: true,
                        ContinuityPlausible: true,
                        SelfPoweredMovementForFlight: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(3),
                        Connected: true,
                        ContinuityPlausible: true,
                        TakeoffCandidate: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(4),
                        Connected: true,
                        ContinuityPlausible: true,
                        AirborneConfirmed: true)));

        return session with
        {
            ContinuityAnchor =
                new FlightContinuityAnchor(
                    Epoch.AddSeconds(4),
                    latitude,
                    longitude,
                    altitude,
                    OnGround: false)
        };
    }

    private static FlightSession ApproachSessionWithGroundAnchor(
        double latitude,
        double longitude,
        double altitude)
    {
        FlightSession session =
            AirborneSessionWithAnchor(
                latitude,
                longitude,
                altitude);

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(5),
                    Connected: true,
                    ContinuityPlausible: true,
                    ApproachConfirmed: true),
                ContinuityAnchor:
                    new FlightContinuityAnchor(
                        Epoch.AddSeconds(5),
                        latitude,
                        longitude,
                        altitude,
                        OnGround: true)));
    }

    private static FlightSession LandingEpisodeSessionWithGroundAnchor(
        double latitude,
        double longitude,
        double altitude)
    {
        FlightSession session =
            ApproachSessionWithGroundAnchor(
                latitude,
                longitude,
                altitude);

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(6),
                    Connected: true,
                    ContinuityPlausible: true,
                    TouchdownConfirmed: true),
                ContinuityAnchor:
                    new FlightContinuityAnchor(
                        Epoch.AddSeconds(6),
                        latitude,
                        longitude,
                        altitude,
                        OnGround: true)));
    }

    private static AircraftTelemetrySnapshot Telemetry(
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double altitudeMsl,
        bool onGround) =>
        new(
            timestamp,
            latitude,
            longitude,
            altitudeMsl,
            onGround ? 0 : 2_000,
            onGround ? 0 : 150,
            onGround ? 0 : 170,
            onGround ? 0 : 500,
            90,
            0,
            0,
            1,
            onGround,
            onGround,
            1,
            500,
            200,
            0,
            true,
            false,
            false);
}
