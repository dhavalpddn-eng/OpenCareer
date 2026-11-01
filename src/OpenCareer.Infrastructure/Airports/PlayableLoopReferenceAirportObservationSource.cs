using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Airports;

namespace OpenCareer.Infrastructure.Airports;

public sealed class PlayableLoopReferenceAirportObservationSource(
    TimeProvider? clock = null)
    : IAirportDataObservationSource,
      ICareerJobMarketAirportSource
{
    public const string ProviderId = "opencareer-playable-loop-airports";

    private static readonly IReadOnlyDictionary<string, AirportRecord> Airports =
        new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["KJFK"] = Airport(
                "KJFK",
                "John F Kennedy International",
                40.6399,
                -73.7787,
                [
                    Runway(
                        "04L/22R",
                        12_079,
                        RunwaySurface.Concrete,
                        new("04L", 31),
                        new("22R", 211)),
                    Runway(
                        "04R/22L",
                        8_400,
                        RunwaySurface.Asphalt,
                        new("04R", 31),
                        new("22L", 211)),
                    Runway(
                        "13L/31R",
                        10_000,
                        RunwaySurface.Concrete,
                        new("13L", 121),
                        new("31R", 301)),
                    Runway(
                        "13R/31L",
                        14_511,
                        RunwaySurface.Concrete,
                        new("13R", 121),
                        new("31L", 301))
                ]),
            ["KRME"] = Airport(
                "KRME",
                "Griffiss International",
                43.2338,
                -75.4069),
            ["KSYR"] = Airport(
                "KSYR",
                "Syracuse Hancock International",
                43.1112,
                -76.1063),
            ["KALB"] = Airport(
                "KALB",
                "Albany International",
                42.7483,
                -73.8017)
        };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string SourceId => ProviderId;

    public AirportDataAuthority Authority => AirportDataAuthority.Reference;

    public Task<AirportRecord?> FindAirportAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);
        cancellationToken.ThrowIfCancellationRequested();

        Airports.TryGetValue(
            icao.Trim().ToUpperInvariant(),
            out AirportRecord? airport);

        return Task.FromResult(airport);
    }

    public Task<AirportDataObservation?> FindAirportObservationAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);
        cancellationToken.ThrowIfCancellationRequested();

        string normalized =
            icao.Trim().ToUpperInvariant();

        if (!Airports.TryGetValue(
                normalized,
                out AirportRecord? airport))
        {
            return Task.FromResult<AirportDataObservation?>(null);
        }

        var observation =
            new AirportDataObservation(
                airport,
                new AirportDataProvenance(
                    ProviderId,
                    AirportDataAuthority.Reference,
                    _clock.GetUtcNow(),
                    Revision: "faa-reference-2026-09"));

        observation.Validate();
        return Task.FromResult<AirportDataObservation?>(observation);
    }

    private static AirportRecord Airport(
        string icao,
        string name,
        double latitude,
        double longitude,
        IReadOnlyList<RunwayRecord>? runways = null) =>
        new(
            icao,
            name,
            runways ?? Array.Empty<RunwayRecord>(),
            latitude,
            longitude);

    private static RunwayRecord Runway(
        string identifier,
        double lengthFeet,
        RunwaySurface surface,
        params RunwayEndRecord[] ends) =>
        new(
            identifier,
            lengthFeet,
            WidthFeet: 200,
            Surface: surface,
            Ends: ends);
}
