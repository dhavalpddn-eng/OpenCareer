using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Infrastructure.Weather;

/// <summary>
/// Optional reference-weather adapter for current terminal observations from
/// AviationWeather.gov. It never substitutes for simulator-local weather and
/// only computes runway-relative wind when verified runway-end true headings
/// are available from the airport-data pipeline.
/// </summary>
public sealed class AviationWeatherMetarSource(
    IAirportDataSource airportData,
    HttpClient? httpClient = null,
    TimeProvider? clock = null)
    : IAirportDispatchWeatherSource
{
    public const string ProviderId = "aviationweather-gov-metar";

    private const string Endpoint =
        "https://aviationweather.gov/api/data/metar";
    private const double MetersPerSecondToKnots = 1.9438444924406;

    private static readonly TimeSpan MaximumObservationAge =
        TimeSpan.FromHours(2);
    private static readonly TimeSpan MaximumFutureSkew =
        TimeSpan.FromMinutes(5);

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private static readonly Regex HeaderPattern = new(
        @"^(?:(?:METAR|SPECI)\s+)?(?<icao>[A-Z0-9]{4})\s+(?<day>\d{2})(?<hour>\d{2})(?<minute>\d{2})Z\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WindPattern = new(
        @"\b(?<direction>\d{3}|VRB)(?<speed>\d{2,3})(?:G(?<gust>\d{2,3}))?(?<unit>KT|MPS)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FractionalVisibilityPattern = new(
        @"(?<![A-Z0-9])(?:(?<whole>\d+)\s+)?(?<numerator>\d+)/(?<denominator>\d+)SM\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DecimalVisibilityPattern = new(
        @"(?<![A-Z0-9])(?<value>\d+(?:\.\d+)?)SM\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient = httpClient ?? SharedHttpClient;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<AirportDispatchWeatherObservation?> FindWeatherAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        string normalizedIcao = icao.Trim().ToUpperInvariant();

        AirportRecord? airport = await airportData
            .FindAirportAsync(normalizedIcao, cancellationToken)
            .ConfigureAwait(false);

        if (airport is null)
            return null;

        airport.Validate();

        if (!HasUsableRunwayGeometry(airport))
            return null;

        string raw;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Endpoint}?ids={Uri.EscapeDataString(normalizedIcao)}&format=raw");

            request.Headers.UserAgent.ParseAdd(
                "OpenCareer/1.0 (+https://github.com/dhavalpddn-eng/OpenCareer)");

            using HttpResponseMessage response = await _httpClient
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            raw = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }

        ParsedMetar? metar = ParseLatestFreshMetar(
            raw,
            normalizedIcao,
            _clock.GetUtcNow());

        if (metar is null)
            return null;

        RunwayWindObservation[] runwayWinds = metar.Wind is null
            ? []
            : MapRunwayWinds(airport, metar.Wind)
                .OrderBy(
                    static wind => wind.RunwayIdentifier,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var observation = new AirportDispatchWeatherObservation(
            normalizedIcao,
            ProviderId,
            DispatchWeatherAuthority.Reference,
            metar.ObservedAt,
            runwayWinds,
            DensityAltitudeFeet: null,
            VisibilityStatuteMiles: metar.VisibilityStatuteMiles);

        observation.Validate();
        return observation;
    }

    private static bool HasUsableRunwayGeometry(AirportRecord airport) =>
        airport.Runways.Any(static runway =>
            !runway.IsClosed
            && runway.Ends is { Count: > 0 }
            && runway.Ends.Any(static end => !end.IsClosed));

    private static IEnumerable<RunwayWindObservation> MapRunwayWinds(
        AirportRecord airport,
        ParsedWind wind)
    {
        foreach (RunwayRecord runway in airport.Runways)
        {
            if (runway.IsClosed || runway.Ends is not { Count: > 0 })
                continue;

            RunwayEndRecord[] openEnds = runway.Ends
                .Where(static end => !end.IsClosed)
                .ToArray();

            if (openEnds.Length == 0)
                continue;

            if (wind.DirectionTrueDegrees is null)
            {
                if (wind.SpeedKnots != 0)
                    continue;

                yield return new(
                    runway.Identifier,
                    SustainedHeadwindKnots: 0,
                    SustainedCrosswindKnots: 0,
                    GustHeadwindKnots: wind.GustKnots is null ? null : 0,
                    GustCrosswindKnots: wind.GustKnots is null ? null : 0);

                continue;
            }

            WindComponents sustained = openEnds
                .Select(end => CalculateComponents(
                    wind.DirectionTrueDegrees.Value,
                    wind.SpeedKnots,
                    end.TrueHeadingDegrees))
                .OrderByDescending(static components => components.Headwind)
                .ThenBy(static components => components.Crosswind)
                .First();

            WindComponents? gust = wind.GustKnots is { } gustKnots
                ? openEnds
                    .Select(end => CalculateComponents(
                        wind.DirectionTrueDegrees.Value,
                        gustKnots,
                        end.TrueHeadingDegrees))
                    .OrderByDescending(static components => components.Headwind)
                    .ThenBy(static components => components.Crosswind)
                    .First()
                : null;

            yield return new(
                runway.Identifier,
                sustained.Headwind,
                sustained.Crosswind,
                gust?.Headwind,
                gust?.Crosswind);
        }
    }

    private static WindComponents CalculateComponents(
        double windFromTrueDegrees,
        double windSpeedKnots,
        double runwayHeadingTrueDegrees)
    {
        double deltaRadians =
            SignedHeadingDifference(
                windFromTrueDegrees,
                runwayHeadingTrueDegrees)
            * (Math.PI / 180.0);

        return new(
            windSpeedKnots * Math.Cos(deltaRadians),
            Math.Abs(windSpeedKnots * Math.Sin(deltaRadians)));
    }

    private static double SignedHeadingDifference(double first, double second)
    {
        double delta = NormalizeHeading(first) - NormalizeHeading(second);

        if (delta > 180)
            delta -= 360;
        else if (delta < -180)
            delta += 360;

        return delta;
    }

    private static double NormalizeHeading(double heading)
    {
        double normalized = heading % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static ParsedMetar? ParseLatestFreshMetar(
        string raw,
        string requestedIcao,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw
            .Split(
                new[] { '\r', '\n' },
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => TryParseMetar(line, requestedIcao, now))
            .Where(static metar => metar is not null)
            .Cast<ParsedMetar>()
            .Where(metar => IsFresh(metar.ObservedAt, now))
            .OrderByDescending(static metar => metar.ObservedAt)
            .FirstOrDefault();
    }

    private static ParsedMetar? TryParseMetar(
        string raw,
        string requestedIcao,
        DateTimeOffset now)
    {
        Match header = HeaderPattern.Match(raw.Trim());

        if (!header.Success
            || !string.Equals(
                header.Groups["icao"].Value,
                requestedIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!int.TryParse(
                header.Groups["day"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int day)
            || !int.TryParse(
                header.Groups["hour"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int hour)
            || !int.TryParse(
                header.Groups["minute"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minute))
        {
            return null;
        }

        DateTimeOffset? observedAt =
            ResolveObservationTime(day, hour, minute, now);

        if (observedAt is null)
            return null;

        Match windMatch = WindPattern.Match(raw);
        ParsedWind? wind = windMatch.Success
            ? ParseWind(windMatch)
            : null;

        return new(
            observedAt.Value,
            wind,
            ParseExactVisibilityStatuteMiles(raw));
    }

    private static double? ParseExactVisibilityStatuteMiles(string raw)
    {
        Match fraction = FractionalVisibilityPattern.Match(raw);

        if (fraction.Success)
        {
            if (!int.TryParse(
                    fraction.Groups["numerator"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int numerator)
                || !int.TryParse(
                    fraction.Groups["denominator"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int denominator)
                || denominator <= 0)
            {
                return null;
            }

            int whole = 0;
            if (fraction.Groups["whole"].Success
                && !int.TryParse(
                    fraction.Groups["whole"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out whole))
            {
                return null;
            }

            return whole + ((double)numerator / denominator);
        }

        Match decimalVisibility = DecimalVisibilityPattern.Match(raw);

        if (!decimalVisibility.Success
            || !double.TryParse(
                decimalVisibility.Groups["value"].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double visibility)
            || !double.IsFinite(visibility)
            || visibility < 0)
        {
            return null;
        }

        return visibility;
    }

    private static ParsedWind? ParseWind(Match match)
    {
        string directionText = match.Groups["direction"].Value;

        if (!double.TryParse(
                match.Groups["speed"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out double speed))
        {
            return null;
        }

        double? gust = null;

        if (match.Groups["gust"].Success)
        {
            if (!double.TryParse(
                    match.Groups["gust"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out double parsedGust))
            {
                return null;
            }

            gust = parsedGust;
        }

        if (string.Equals(
                match.Groups["unit"].Value,
                "MPS",
                StringComparison.Ordinal))
        {
            speed *= MetersPerSecondToKnots;

            if (gust is { } gustMetersPerSecond)
                gust = gustMetersPerSecond * MetersPerSecondToKnots;
        }

        double? direction = null;

        if (!string.Equals(directionText, "VRB", StringComparison.Ordinal))
        {
            if (!double.TryParse(
                    directionText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out double parsedDirection)
                || parsedDirection < 0
                || parsedDirection > 360)
            {
                return null;
            }

            direction = parsedDirection == 360 ? 0 : parsedDirection;
        }

        return new(direction, speed, gust);
    }

    private static DateTimeOffset? ResolveObservationTime(
        int day,
        int hour,
        int minute,
        DateTimeOffset now)
    {
        if (day is < 1 or > 31
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            return null;
        }

        var candidates = new List<DateTimeOffset>(3);

        foreach (int monthOffset in new[] { -1, 0, 1 })
        {
            DateTimeOffset month = now.AddMonths(monthOffset);
            int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

            if (day > daysInMonth)
                continue;

            candidates.Add(
                new DateTimeOffset(
                    month.Year,
                    month.Month,
                    day,
                    hour,
                    minute,
                    0,
                    TimeSpan.Zero));
        }

        return candidates
            .OrderBy(candidate => Math.Abs((candidate - now).TotalMinutes))
            .FirstOrDefault();
    }

    private static bool IsFresh(DateTimeOffset observedAt, DateTimeOffset now)
    {
        TimeSpan age = now - observedAt;

        return age >= -MaximumFutureSkew
            && age <= MaximumObservationAge;
    }

    private static HttpClient CreateHttpClient() =>
        new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

    private sealed record ParsedMetar(
        DateTimeOffset ObservedAt,
        ParsedWind? Wind,
        double? VisibilityStatuteMiles);

    private sealed record ParsedWind(
        double? DirectionTrueDegrees,
        double SpeedKnots,
        double? GustKnots);

    private sealed record WindComponents(
        double Headwind,
        double Crosswind);
}
