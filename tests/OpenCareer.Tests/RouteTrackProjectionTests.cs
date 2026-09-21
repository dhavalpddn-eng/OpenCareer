using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class RouteTrackProjectionTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProjectsTrackIntoNormalizedCoordinates()
    {
        FlightLegDebrief leg = Leg(
            [
                new(Start, 30, -100, 1000),
                new(Start.AddMinutes(30), 35, -95, 5000),
                new(Start.AddHours(1), 40, -90, 1000)
            ]);

        ProjectedRouteLeg projected = Assert.Single(
            RouteTrackProjection.Project([leg]));

        Assert.Equal(0, projected.Points[0].X, 8);
        Assert.Equal(1, projected.Points[0].Y, 8);
        Assert.Equal(0.5, projected.Points[1].X, 8);
        Assert.Equal(0.5, projected.Points[1].Y, 8);
        Assert.Equal(1, projected.Points[2].X, 8);
        Assert.Equal(0, projected.Points[2].Y, 8);
    }

    [Fact]
    public void AntimeridianCrossingUsesShortWrappedSpan()
    {
        FlightLegDebrief leg = Leg(
            [
                new(Start, 10, 179, 1000),
                new(Start.AddMinutes(10), 10, -179, 1000)
            ]);

        ProjectedRouteLeg projected = Assert.Single(
            RouteTrackProjection.Project([leg]));

        Assert.Equal(0, projected.Points[0].X, 8);
        Assert.Equal(1, projected.Points[1].X, 8);
        Assert.All(projected.Points, point => Assert.Equal(0.5, point.Y, 8));
    }

    [Fact]
    public void EmptyTracksRemainEmpty()
    {
        FlightLegDebrief leg = Leg(Array.Empty<FlightTrackPoint>());

        ProjectedRouteLeg projected = Assert.Single(
            RouteTrackProjection.Project([leg]));

        Assert.Empty(projected.Points);
    }

    private static FlightLegDebrief Leg(
        IReadOnlyList<FlightTrackPoint> points)
    {
        FlightTimeLedger ledger = FlightTimeLedger.Empty;

        return new(
            Guid.NewGuid(),
            1,
            Start,
            Start.AddHours(1),
            new("A", "B", "A", "B", null, null),
            ledger,
            points,
            Array.Empty<int>());
    }
}
