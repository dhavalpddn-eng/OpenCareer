using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionLandingEpisodeTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FullStopLandingCreatesEpisodeWithBounceCount()
    {
        FlightSession session =
            Airborne();

        session =
            Advance(
                session,
                6,
                touchdown: true,
                touchdownMetrics: Metrics(-240, 1.2, 72, 3, -1));

        session =
            Advance(
                session,
                7,
                bounce: true,
                touchdownMetrics: Metrics(-900, 2.5, 40, 10, 8));

        session =
            Advance(
                session,
                8,
                rollout: true);

        FlightSessionLandingEpisode episode =
            Assert.Single(
                session.EffectiveLandingEpisodes);

        Assert.Equal(
            1,
            episode.EpisodeNumber);

        Assert.Equal(
            FlightSessionLandingKind.FullStop,
            episode.Kind);

        Assert.Equal(
            1,
            episode.BounceCount);

        Assert.Equal(
            Epoch.AddSeconds(6),
            episode.TouchdownAt);

        Assert.Equal(
            Epoch.AddSeconds(8),
            episode.CompletedAt);
        Assert.Equal(-240, episode.VerticalSpeedFeetPerMinute);
        Assert.Equal(1.2, episode.NormalAccelerationG);
        Assert.Equal(72, episode.IndicatedAirspeedKnots);
        Assert.Equal(3, episode.PitchDegrees);
        Assert.Equal(-1, episode.BankDegrees);
    }

    [Fact]
    public void TouchAndGoClosesEpisodeWithoutPretendingFullStop()
    {
        FlightSession session =
            Airborne();

        session =
            Advance(
                session,
                6,
                touchdown: true,
                touchdownMetrics: Metrics(-180, 1.1, 68, 2, 0));

        session =
            Advance(
                session,
                7,
                touchAndGo: true);

        FlightSessionLandingEpisode episode =
            Assert.Single(
                session.EffectiveLandingEpisodes);

        Assert.Equal(
            FlightSessionLandingKind.TouchAndGo,
            episode.Kind);

        Assert.Equal(
            Epoch.AddSeconds(7),
            episode.CompletedAt);
        Assert.Equal(-180, episode.VerticalSpeedFeetPerMinute);
    }

    [Fact]
    public void SeparateLandingEpisodesRetainIndependentMetrics()
    {
        FlightSession session = Airborne();
        session = Advance(session, 6, touchdown: true, touchdownMetrics: Metrics(-150, 1.1, 70, 2, 0));
        session = Advance(session, 7, touchAndGo: true);
        session = Advance(session, 8, touchdown: true, touchdownMetrics: Metrics(-320, 1.4, 65, 4, 1));

        Assert.Equal(2, session.EffectiveLandingEpisodes.Count);
        Assert.Equal(-150, session.EffectiveLandingEpisodes[0].VerticalSpeedFeetPerMinute);
        Assert.Equal(-320, session.EffectiveLandingEpisodes[1].VerticalSpeedFeetPerMinute);
        Assert.Equal(1.1, session.EffectiveLandingEpisodes[0].NormalAccelerationG);
        Assert.Equal(1.4, session.EffectiveLandingEpisodes[1].NormalAccelerationG);
    }

    [Fact]
    public void MissingTouchdownMetricsRemainUnknown()
    {
        FlightSession session = Advance(Airborne(), 6, touchdown: true);
        FlightSessionLandingEpisode episode = Assert.Single(session.EffectiveLandingEpisodes);

        Assert.Null(episode.VerticalSpeedFeetPerMinute);
        Assert.Null(episode.NormalAccelerationG);
        Assert.Null(episode.IndicatedAirspeedKnots);
        Assert.Null(episode.PitchDegrees);
        Assert.Null(episode.BankDegrees);
    }

    private static FlightSession Airborne()
    {
        FlightSession session =
            FlightSession.Start(Epoch);

        session =
            Advance(
                session,
                1,
                stable: true,
                validAircraft: true);

        session =
            Advance(
                session,
                2,
                movement: true);

        session =
            Advance(
                session,
                3,
                takeoffCandidate: true);

        return Advance(
            session,
            4,
            airborne: true);
    }

    private static FlightSession Advance(
        FlightSession session,
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool touchdown = false,
        bool bounce = false,
        bool touchAndGo = false,
        bool rollout = false,
        FlightSessionTouchdownMetrics? touchdownMetrics = null) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(seconds),
                    Connected: true,
                    StableTelemetry: stable,
                    ValidLoadedAircraft: validAircraft,
                    ContinuityPlausible: true,
                    SelfPoweredMovementForFlight: movement,
                    TakeoffCandidate: takeoffCandidate,
                    AirborneConfirmed: airborne,
                    TouchdownConfirmed: touchdown,
                    BounceRecontact: bounce,
                    TouchAndGoConfirmed: touchAndGo,
                    LandingRolloutConfirmed: rollout),
                TouchdownMetrics: touchdownMetrics));

    private static FlightSessionTouchdownMetrics Metrics(
        double verticalSpeed,
        double normalG,
        double indicatedAirspeed,
        double pitch,
        double bank) =>
        new(verticalSpeed, normalG, indicatedAirspeed, pitch, bank);
}
