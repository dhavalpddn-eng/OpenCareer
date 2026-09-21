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
                touchdown: true);

        session =
            Advance(
                session,
                7,
                bounce: true);

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
                touchdown: true);

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
        bool rollout = false) =>
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
                    LandingRolloutConfirmed: rollout)));
}
