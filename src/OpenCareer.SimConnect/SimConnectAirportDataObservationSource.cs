using System.Globalization;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Airports;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.SimConnect;

public sealed class SimConnectAirportDataObservationSource(
    SimConnectConnection connection,
    TimeProvider? clock = null)
    : IAirportDataObservationSource
{
    private const double FeetPerMeter = 3.280839895013123;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string SourceId => "msfs-simconnect-facility";
    public AirportDataAuthority Authority => AirportDataAuthority.LocalSimulator;

    public async Task<AirportDataObservation?> FindAirportObservationAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        string normalizedIcao = icao.Trim().ToUpperInvariant();
        SimConnectAirportFacilitySnapshot? snapshot = await connection
            .RequestAirportFacilityAsync(normalizedIcao, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
            return null;

        RunwayRecord[] runways = snapshot.Runways
            .Select(MapRunway)
            .Where(static runway => runway is not null)
            .Cast<RunwayRecord>()
            .OrderBy(static runway => runway.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (runways
            .GroupBy(static runway => runway.Identifier, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1))
        {
            return null;
        }

        string name = string.IsNullOrWhiteSpace(snapshot.Name)
            ? snapshot.Icao
            : snapshot.Name;

        var observation = new AirportDataObservation(
            new AirportRecord(snapshot.Icao, name, runways),
            new AirportDataProvenance(
                SourceId,
                Authority,
                _clock.GetUtcNow()));

        observation.Validate();
        return observation;
    }

    private static RunwayRecord? MapRunway(SimConnectRunwayFacilityData runway)
    {
        string? primary = FormatRunwayEnd(runway.PrimaryNumber, runway.PrimaryDesignator);
        string? secondary = FormatRunwayEnd(runway.SecondaryNumber, runway.SecondaryDesignator);

        if (primary is null && secondary is null)
            return null;

        string identifier = primary is not null && secondary is not null
            ? string.Equals(primary, secondary, StringComparison.OrdinalIgnoreCase)
                ? primary
                : $"{primary}/{secondary}"
            : primary ?? secondary!;

        bool isClosed = primary is not null && secondary is not null
            ? runway.PrimaryClosed && runway.SecondaryClosed
            : primary is not null
                ? runway.PrimaryClosed
                : runway.SecondaryClosed;

        return new(
            identifier,
            ToPositiveFeet(runway.LengthMeters),
            ToPositiveFeet(runway.WidthMeters),
            MapSurface(runway.Surface),
            isClosed);
    }

    private static double? ToPositiveFeet(float meters)
    {
        if (!float.IsFinite(meters) || meters <= 0)
            return null;

        return meters * FeetPerMeter;
    }

    private static RunwaySurface MapSurface(int surface) =>
        surface switch
        {
            0 => RunwaySurface.Concrete,
            1 or 3 or 5 or 6 or 7 => RunwaySurface.Grass,
            2 or 26 or 27 or 28 or 29 or 30 or 31 => RunwaySurface.Water,
            4 or 15 or 17 or 19 or 23 => RunwaySurface.Asphalt,
            8 or 9 => RunwaySurface.SnowOrIce,
            12 => RunwaySurface.Dirt,
            14 => RunwaySurface.Gravel,
            10 or 11 or 13 or 16 or 18 or 20 or 21 or 22 or 24 or 32 => RunwaySurface.Other,
            254 or 255 => RunwaySurface.Unknown,
            _ => RunwaySurface.Unknown
        };

    private static string? FormatRunwayEnd(int number, int designator)
    {
        string? numberText = number switch
        {
            >= 1 and <= 36 => number.ToString("00", CultureInfo.InvariantCulture),
            37 => "N",
            38 => "NE",
            39 => "E",
            40 => "SE",
            41 => "S",
            42 => "SW",
            43 => "W",
            44 => "NW",
            _ => null
        };

        if (numberText is null)
            return null;

        string? designatorText = designator switch
        {
            0 => string.Empty,
            1 => "L",
            2 => "R",
            3 => "C",
            4 => "W",
            5 => "A",
            6 => "B",
            _ => null
        };

        return designatorText is null ? null : numberText + designatorText;
    }
}
