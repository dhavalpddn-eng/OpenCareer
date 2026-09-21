using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Logbook;

public sealed record ProjectedRoutePoint(
    double X,
    double Y,
    DateTimeOffset Timestamp);

public sealed record ProjectedRouteLeg(
    int Sequence,
    IReadOnlyList<ProjectedRoutePoint> Points);

public static class RouteTrackProjection
{
    public static IReadOnlyList<ProjectedRouteLeg> Project(
        IReadOnlyList<FlightLegDebrief> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);

        FlightTrackPoint[] allPoints = legs
            .SelectMany(static leg => leg.RouteTrack)
            .ToArray();

        if (allPoints.Length == 0)
        {
            return legs
                .OrderBy(static leg => leg.Sequence)
                .Select(static leg =>
                    new ProjectedRouteLeg(
                        leg.Sequence,
                        Array.Empty<ProjectedRoutePoint>()))
                .ToArray();
        }

        double minLatitude = allPoints.Min(static point => point.LatitudeDegrees);
        double maxLatitude = allPoints.Max(static point => point.LatitudeDegrees);

        bool wrapLongitude = ShouldWrapLongitudes(allPoints);
        double[] normalizedLongitudes = allPoints
            .Select(point => NormalizeLongitude(point.LongitudeDegrees, wrapLongitude))
            .ToArray();

        double minLongitude = normalizedLongitudes.Min();
        double maxLongitude = normalizedLongitudes.Max();

        double latitudeSpan = maxLatitude - minLatitude;
        double longitudeSpan = maxLongitude - minLongitude;

        var result = new List<ProjectedRouteLeg>(legs.Count);
        foreach (FlightLegDebrief leg in legs.OrderBy(static leg => leg.Sequence))
        {
            ProjectedRoutePoint[] points = leg.RouteTrack
                .Select(point =>
                {
                    double longitude =
                        NormalizeLongitude(point.LongitudeDegrees, wrapLongitude);

                    double x = longitudeSpan <= double.Epsilon
                        ? 0.5
                        : (longitude - minLongitude) / longitudeSpan;

                    double y = latitudeSpan <= double.Epsilon
                        ? 0.5
                        : (maxLatitude - point.LatitudeDegrees) / latitudeSpan;

                    return new ProjectedRoutePoint(
                        Math.Clamp(x, 0, 1),
                        Math.Clamp(y, 0, 1),
                        point.Timestamp);
                })
                .ToArray();

            result.Add(new ProjectedRouteLeg(leg.Sequence, points));
        }

        return result;
    }

    private static bool ShouldWrapLongitudes(
        IReadOnlyCollection<FlightTrackPoint> points)
    {
        if (points.Count < 2)
            return false;

        double standardMin = points.Min(static point => point.LongitudeDegrees);
        double standardMax = points.Max(static point => point.LongitudeDegrees);
        double standardSpan = standardMax - standardMin;

        double wrappedMin = points.Min(static point =>
            point.LongitudeDegrees < 0
                ? point.LongitudeDegrees + 360
                : point.LongitudeDegrees);
        double wrappedMax = points.Max(static point =>
            point.LongitudeDegrees < 0
                ? point.LongitudeDegrees + 360
                : point.LongitudeDegrees);
        double wrappedSpan = wrappedMax - wrappedMin;

        return wrappedSpan < standardSpan;
    }

    private static double NormalizeLongitude(
        double longitude,
        bool wrap) =>
        wrap && longitude < 0
            ? longitude + 360
            : longitude;
}
