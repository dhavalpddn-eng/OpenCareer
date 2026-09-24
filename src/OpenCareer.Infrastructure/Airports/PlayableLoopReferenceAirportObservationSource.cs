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
                "04L/22R", 12_079, 200, RunwaySurface.Concrete),
            ["KRME"] = Airport(
                "KRME",
                "Griffiss International",
                43.2338,
                -75.4069,
                "15/33", 11_820, 200, RunwaySurface.Asphalt),
            ["KSYR"] = Airport(
                "KSYR",
                "Syracuse Hancock International",
                43.1112,
                -76.1063,
                "10/28", 9_013, 150, RunwaySurface.Asphalt),
            ["KALB"] = Airport(
                "KALB",
                "Albany International",
                42.7483,
                -73.8017,
                "01/19", 8_500, 150, RunwaySurface.Asphalt)
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
                    Revision: "faa-chart-supplement-2026-01"));

        observation.Validate();
        return Task.FromResult<AirportDataObservation?>(observation);
    }

    private static AirportRecord Airport(
        string icao,
        string name,
        double latitude,
        double longitude,
        string runwayDesignation,
        double runwayLengthFeet,
        double runwayWidthFeet,
        RunwaySurface runwaySurface) =>
        new(
            icao,
            name,
            // FAA Northeast Chart Supplement, 22 Jan–19 Mar 2026:
            // aeronav.faa.gov/afd/22JAN2026/NE_225_22JAN2026.pdf (KJFK)
            // aeronav.faa.gov/afd/22JAN2026/NE_240_22JAN2026.pdf (KRME)
            // aeronav.faa.gov/afd/22JAN2026/NE_249_22JAN2026.pdf (KSYR)
            // aeronav.faa.gov/afd/22JAN2026/NE_187_22JAN2026.pdf (KALB)
            [new RunwayRecord(
                runwayDesignation,
                runwayLengthFeet,
                runwayWidthFeet,
                runwaySurface)],
            latitude,
            longitude);
}
