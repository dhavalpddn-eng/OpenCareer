using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionDetailsTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StatisticsAccumulateDistanceFuelAndSparseTrack()
    {
        FlightSessionStatistics statistics =
            FlightSessionStatistics.Empty;

        var first =
            new FlightSessionObservation(
                Epoch,
                32.0,
                -97.0,
                1_000,
                90,
                100,
                500,
                200,
                CaptureTrackPoint: true);

        statistics =
            statistics.Observe(
                first,
                previousAnchor: null);

        var second =
            new FlightSessionObservation(
                Epoch.AddMinutes(5),
                33.0,
                -97.0,
                8_000,
                150,
                170,
                450,
                190,
                CaptureTrackPoint: true);

        statistics =
            statistics.Observe(
                second,
                new FlightContinuityAnchor(
                    Epoch,
                    32.0,
                    -97.0,
                    1_000,
                    OnGround: false));

        Assert.InRange(
            statistics.DistanceNauticalMiles,
            59,
            61);

        Assert.Equal(
            50,
            statistics.FuelBurnedPounds,
            precision: 6);

        Assert.Equal(
            0,
            statistics.FuelAddedPounds,
            precision: 6);

        Assert.Equal(
            500,
            statistics.StartFuelPounds);

        Assert.Equal(
            450,
            statistics.LastFuelPounds);

        Assert.Equal(
            8_000,
            statistics.MaximumAltitudeMslFeet);

        Assert.Equal(
            2,
            statistics.RouteTrack.Count);
    }

    [Fact]
    public void RefuelIsRecordedSeparatelyFromFuelBurn()
    {
        FlightSessionStatistics statistics =
            FlightSessionStatistics.Empty
                .Observe(
                    Observation(
                        Epoch,
                        fuel: 300,
                        capture: false),
                    previousAnchor: null)
                .Observe(
                    Observation(
                        Epoch.AddMinutes(1),
                        fuel: 250,
                        capture: false),
                    previousAnchor: null)
                .Observe(
                    Observation(
                        Epoch.AddMinutes(2),
                        fuel: 400,
                        capture: false),
                    previousAnchor: null);

        Assert.Equal(
            50,
            statistics.FuelBurnedPounds,
            precision: 6);

        Assert.Equal(
            150,
            statistics.FuelAddedPounds,
            precision: 6);
    }

    [Fact]
    public void SessionStartPreservesProviderNeutralPlan()
    {
        var plan =
            new FlightSessionPlan(
                "KDFW",
                "KIAH",
                PlannedAlternate: "KAUS",
                PlannedRoute: "DCT TEST",
                SourceProvider: "phpVMS",
                SourceReference: "flight-42");

        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId: Guid.NewGuid(),
                sessionId: Guid.NewGuid(),
                plan: plan);

        Assert.Equal(
            plan,
            session.Plan);

        Assert.NotNull(
            session.Statistics);

        Assert.Empty(
            session.EffectiveLandingEpisodes);
    }

    private static FlightSessionObservation Observation(
        DateTimeOffset timestamp,
        double fuel,
        bool capture) =>
        new(
            timestamp,
            32,
            -97,
            600,
            0,
            0,
            fuel,
            100,
            capture);
}
