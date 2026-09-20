using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class OperationDispatchWeatherTests
{
    [Fact]
    public async Task WeatherIsNotQueriedWhenNoWeatherLimitsAreRequested()
    {
        var weather = new StubWeatherSource([]);
        var service = Service(
            weather,
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Empty(weather.RequestedIcaos);
    }

    [Fact]
    public async Task MissingWeatherSourceFailsClosedOnlyWhenWeatherGateIsRequested()
    {
        var service = new OperationDispatchPlanningService(
            new StubAircraftRegistrySource(Resolution()),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("18")),
                    ["KBBB"] = Airport("KBBB", Runway("36"))
                }));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements());

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.AirportWeatherUnavailable);
        Assert.Contains(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Destination
                && issue.Reason == DispatchFeasibilityReason.AirportWeatherUnavailable);
    }

    [Fact]
    public async Task DefiniteRunwayFailureWinsOverMissingWeather()
    {
        var service = new OperationDispatchPlanningService(
            new StubAircraftRegistrySource(Resolution()),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("18", lengthFeet: 1500)),
                    ["KBBB"] = Airport("KBBB", Runway("36"))
                }));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements());

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.AirportWeatherUnavailable);
    }

    [Fact]
    public async Task CrosswindAboveLimitIsInfeasibleWithObservedAndLimitValues()
    {
        var service = Service(
            WeatherSource(
                Weather("KAAA", Wind("18", headwind: 5, crosswind: 22)),
                Weather("KBBB", Wind("36", headwind: 5, crosswind: 3))),
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(maximumCrosswind: 15));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.CrosswindExceedsLimit);

        Assert.Equal("18", issue.RunwayIdentifier);
        Assert.Equal(22, issue.ObservedKnots);
        Assert.Equal(15, issue.LimitKnots);
    }

    [Fact]
    public async Task GustCrosswindIsUsedConservatively()
    {
        var service = Service(
            WeatherSource(
                Weather("KAAA", Wind("18", headwind: 8, crosswind: 8, gustCrosswind: 19)),
                Weather("KBBB", Wind("36", headwind: 5, crosswind: 3))),
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(maximumCrosswind: 15));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.CrosswindExceedsLimit
                && issue.ObservedKnots == 19);
    }

    [Fact]
    public async Task WorstGustTailwindIsUsed()
    {
        var service = Service(
            WeatherSource(
                Weather(
                    "KAAA",
                    Wind(
                        "18",
                        headwind: 2,
                        crosswind: 3,
                        gustHeadwind: -9,
                        gustCrosswind: 4)),
                Weather("KBBB", Wind("36", headwind: 5, crosswind: 3))),
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(maximumTailwind: 5));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.TailwindExceedsLimit);

        Assert.Equal(9, issue.ObservedKnots);
        Assert.Equal(5, issue.LimitKnots);
    }

    [Fact]
    public async Task WeatherCanSelectShorterPhysicallyCompatibleRunway()
    {
        var service = Service(
            WeatherSource(
                Weather(
                    "KAAA",
                    Wind("18", headwind: 5, crosswind: 25),
                    Wind("09", headwind: 8, crosswind: 4)),
                Weather("KBBB", Wind("36", headwind: 5, crosswind: 3))),
            Resolution(),
            Airport(
                "KAAA",
                Runway("18", lengthFeet: 6000),
                Runway("09", lengthFeet: 4000)),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(maximumCrosswind: 15));

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Equal("09", result.OriginRunwayIdentifier);
        Assert.Equal("36", result.DestinationRunwayIdentifier);
    }

    [Fact]
    public async Task MissingSelectedRunwayWindDataRemainsInsufficient()
    {
        var service = Service(
            WeatherSource(
                Weather("KAAA"),
                Weather("KBBB", Wind("36", headwind: 5, crosswind: 3))),
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements());

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.RunwayIdentifier == "18"
                && issue.Reason == DispatchFeasibilityReason.RunwayWindDataUnavailable);
    }

    [Fact]
    public async Task UnknownDensityAltitudeFailsClosedWhenLimitIsConfigured()
    {
        var service = Service(
            WeatherSource(
                Weather("KAAA", Wind("18", 5, 3), densityAltitudeFeet: null),
                Weather("KBBB", Wind("36", 5, 3), densityAltitudeFeet: 2000)),
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(maximumDensityAltitude: 7000));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.DensityAltitudeUnknown
                && issue.MaximumDensityAltitudeFeet == 7000);
    }

    [Fact]
    public async Task DensityAltitudeAboveExplicitLimitIsInfeasible()
    {
        var service = Service(
            WeatherSource(
                Weather("KAAA", Wind("18", 5, 3), densityAltitudeFeet: 8500),
                Weather("KBBB", Wind("36", 5, 3), densityAltitudeFeet: 2000)),
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(maximumDensityAltitude: 7000));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.DensityAltitudeExceedsLimit);

        Assert.Equal(8500, issue.ObservedDensityAltitudeFeet);
        Assert.Equal(7000, issue.MaximumDensityAltitudeFeet);
    }

    [Fact]
    public async Task DensityAltitudeIsIgnoredWhenNoDensityLimitIsConfigured()
    {
        var service = Service(
            WeatherSource(
                Weather("KAAA", Wind("18", 5, 3), densityAltitudeFeet: null),
                Weather("KBBB", Wind("36", 5, 3), densityAltitudeFeet: null)),
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(maximumDensityAltitude: null));

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
    }

    [Fact]
    public async Task SameOriginAndDestinationQueriesWeatherOnce()
    {
        StubWeatherSource weather = WeatherSource(
            Weather("KAAA", Wind("18", 5, 3)));

        var service = Service(
            weather,
            Resolution(),
            Airport("KAAA", Runway("18")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KAAA",
            Requirements());

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Equal(["KAAA"], weather.RequestedIcaos);
    }

    [Fact]
    public void MismatchedWeatherIcaoIsRejected()
    {
        AircraftRegistryResolution aircraft = Resolution();
        AirportRecord origin = Airport("KAAA", Runway("18"));
        AirportRecord destination = Airport("KBBB", Runway("36"));

        Assert.Throws<ArgumentException>(
            () => OperationDispatchPhysicalEvaluator.Evaluate(
                aircraft,
                Requirements(),
                origin,
                destination,
                Weather("KZZZ", Wind("18", 5, 3)),
                Weather("KBBB", Wind("36", 5, 3))));
    }

    [Fact]
    public void DuplicateRunwayWindIdentifiersAreRejected()
    {
        AirportDispatchWeatherObservation weather = Weather(
            "KAAA",
            Wind("18", 5, 3),
            Wind("18", 7, 4));

        Assert.Throws<ArgumentException>(weather.Validate);
    }

    private static OperationDispatchRequirements Requirements(
        double maximumCrosswind = 15,
        double maximumTailwind = 5,
        double? maximumDensityAltitude = null) =>
        new(
            PayloadPounds: 1000,
            RequiredRangeNauticalMiles: 500,
            WeatherLimits: new(
                MaximumCrosswindKnots: maximumCrosswind,
                MaximumTailwindKnots: maximumTailwind,
                MaximumDensityAltitudeFeet: maximumDensityAltitude));

    private static OperationDispatchPlanningService Service(
        StubWeatherSource weather,
        AircraftRegistryResolution resolution,
        params AirportRecord[] airports) =>
        new(
            new StubAircraftRegistrySource(resolution),
            new StubAirportDataSource(
                airports.ToDictionary(
                    static airport => airport.Icao,
                    StringComparer.OrdinalIgnoreCase)),
            weather);

    private static AircraftRegistryResolution Resolution() =>
        AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: "fixture-aircraft",
                ProviderId: "test-source",
                ProviderRecordId: "fixture",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: true,
                MaximumPayloadPounds: 3000,
                MaximumRangeNauticalMiles: 1200,
                RunwayPerformance: new(
                    MinimumTakeoffRunwayFeet: 1800,
                    MinimumLandingRunwayFeet: 1600,
                    MinimumRunwayWidthFeet: 50,
                    SupportedSurfaces:
                        RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete,
                    Confidence: AircraftDataConfidence.Verified,
                    Source: "test"))
        ]);

    private static AirportRecord Airport(
        string icao,
        params RunwayRecord[] runways) =>
        new(icao, $"{icao} Fixture", runways);

    private static RunwayRecord Runway(
        string identifier,
        double? lengthFeet = 5000,
        double? widthFeet = 100,
        RunwaySurface surface = RunwaySurface.Asphalt) =>
        new(identifier, lengthFeet, widthFeet, surface);

    private static AirportDispatchWeatherObservation Weather(
        string icao,
        params RunwayWindObservation[] winds) =>
        Weather(icao, winds, densityAltitudeFeet: null);

    private static AirportDispatchWeatherObservation Weather(
        string icao,
        RunwayWindObservation[] winds,
        double? densityAltitudeFeet) =>
        new(
            icao,
            "test-weather",
            DispatchWeatherAuthority.LocalSimulator,
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
            winds,
            densityAltitudeFeet);

    private static AirportDispatchWeatherObservation Weather(
        string icao,
        RunwayWindObservation wind,
        double? densityAltitudeFeet) =>
        Weather(icao, [wind], densityAltitudeFeet);

    private static RunwayWindObservation Wind(
        string runway,
        double? headwind,
        double? crosswind,
        double? gustHeadwind = null,
        double? gustCrosswind = null) =>
        new(runway, headwind, crosswind, gustHeadwind, gustCrosswind);

    private static StubWeatherSource WeatherSource(
        params AirportDispatchWeatherObservation[] weather) =>
        new(
            weather.ToDictionary(
                static observation => observation.Icao,
                StringComparer.OrdinalIgnoreCase));

    private sealed class StubAircraftRegistrySource(AircraftRegistryResolution? result)
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class StubAirportDataSource(
        IReadOnlyDictionary<string, AirportRecord> airports)
        : IAirportDataSource
    {
        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            airports.TryGetValue(icao, out AirportRecord? airport);
            return Task.FromResult(airport);
        }
    }

    private sealed class StubWeatherSource(
        IReadOnlyDictionary<string, AirportDispatchWeatherObservation> weather)
        : IAirportDispatchWeatherSource
    {
        public List<string> RequestedIcaos { get; } = [];

        public Task<AirportDispatchWeatherObservation?> FindWeatherAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedIcaos.Add(icao);
            weather.TryGetValue(icao, out AirportDispatchWeatherObservation? observation);
            return Task.FromResult(observation);
        }
    }
}
