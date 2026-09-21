using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class OperationDispatchConditionedPerformanceTests
{
    [Fact]
    public async Task TakeoffTotalDistanceOverridesStaticBaseline()
    {
        AircraftConditionedPerformanceProfile conditioned = Conditioned(
            takeoffTotal: SinglePointGrid(
                weight: 6000,
                temperature: 20,
                pressureAltitude: 2000,
                value: 2500));

        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(
                    takeoff: 1200,
                    landing: 1200),
                conditioned: conditioned),
            Airport("KAAA", Runway("18", lengthFeet: 2000)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                takeoff: Conditions(6000, 20, 2000)));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);

        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue =>
                issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);

        Assert.Equal(2500, issue.RequiredFeet);
        Assert.Equal(2000, issue.AvailableFeet);
    }

    [Fact]
    public async Task InterpolatedTotalDistanceReceivesExplicitSafetyMargin()
    {
        var takeoff = new AircraftPerformanceGrid3D(
            weightsPounds: [5000, 7000],
            temperaturesCelsius: [0, 20],
            pressureAltitudesFeet: [0, 4000],
            values:
            [
                1600, 2000,
                1800, 2200,
                2000, 2400,
                2200, 2600
            ],
            AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius);

        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(
                    takeoff: 1000,
                    landing: 1000),
                conditioned: Conditioned(takeoffTotal: takeoff)),
            Airport("KAAA", Runway("18", lengthFeet: 2200)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                runwayMarginPercent: 10,
                takeoff: Conditions(6000, 10, 2000)));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);

        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue =>
                issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);

        Assert.Equal(2310, issue.RequiredFeet!.Value, 6);
        Assert.Equal(2200, issue.AvailableFeet);
    }

    [Fact]
    public async Task OutsideTakeoffEnvelopeIsInsufficientAndDoesNotFallBackToStaticLength()
    {
        AircraftConditionedPerformanceProfile conditioned = Conditioned(
            takeoffTotal: SinglePointGrid(
                weight: 6000,
                temperature: 20,
                pressureAltitude: 2000,
                value: 2500));

        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(
                    takeoff: 1000,
                    landing: 1000),
                conditioned: conditioned),
            Airport("KAAA", Runway("18", lengthFeet: 5000)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                takeoff: Conditions(7000, 20, 2000)));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);

        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue =>
                issue.Reason
                    == DispatchFeasibilityReason.AircraftTakeoffPerformanceOutsideEnvelope);

        Assert.Equal(7000, issue.RequiredPounds);
        Assert.Equal(20, issue.OutsideAirTemperatureCelsius);
        Assert.Equal(2000, issue.PressureAltitudeFeet);

        Assert.DoesNotContain(
            result.Issues,
            static issue =>
                issue.Reason == DispatchFeasibilityReason.AircraftTakeoffLengthUnknown);
    }

    [Fact]
    public async Task MissingTakeoffConditionsFailClosedWhenTotalDistanceTableExists()
    {
        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(
                    takeoff: 1000,
                    landing: 1000),
                conditioned: Conditioned(
                    takeoffTotal: SinglePointGrid(
                        6000,
                        20,
                        2000,
                        2500))),
            Airport("KAAA", Runway("18", lengthFeet: 5000)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements());

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue =>
                issue.Reason
                    == DispatchFeasibilityReason.AircraftTakeoffPerformanceConditionsMissing);

        Assert.DoesNotContain(
            result.Issues,
            static issue =>
                issue.Reason == DispatchFeasibilityReason.AircraftTakeoffLengthUnknown);
    }

    [Fact]
    public async Task LandingUsesIndependentPredictedWeightTemperatureAndPressureAltitude()
    {
        AircraftConditionedPerformanceProfile conditioned = Conditioned(
            takeoffTotal: SinglePointGrid(
                7000,
                25,
                1000,
                1800),
            landingTotal: SinglePointGrid(
                6200,
                10,
                3500,
                2300));

        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(
                    takeoff: 1000,
                    landing: 1000),
                conditioned: conditioned),
            Airport("KAAA", Runway("18", lengthFeet: 2000)),
            Airport("KBBB", Runway("36", lengthFeet: 2200)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                takeoff: Conditions(7000, 25, 1000),
                landing: Conditions(6200, 10, 3500)));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);

        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue =>
                issue.Endpoint == DispatchEndpoint.Destination
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);

        Assert.Equal(2300, issue.RequiredFeet);
        Assert.Equal(2200, issue.AvailableFeet);
    }

    [Fact]
    public async Task ConditionedTotalDistanceCanFillUnknownBaselineLength()
    {
        AircraftRunwayPerformanceProfile baseline = RunwayProfile(
            takeoff: null,
            landing: 1200);

        var service = Service(
            Resolution(
                runwayPerformance: baseline,
                conditioned: Conditioned(
                    takeoffTotal: SinglePointGrid(
                        6000,
                        15,
                        1000,
                        1800))),
            Airport("KAAA", Runway("18", lengthFeet: 2000)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                takeoff: Conditions(6000, 15, 1000)));

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Equal("18", result.OriginRunwayIdentifier);
    }

    [Fact]
    public async Task GroundRollAloneDoesNotReplaceMissingDispatchRunwayRequirement()
    {
        AircraftRunwayPerformanceProfile baseline = RunwayProfile(
            takeoff: null,
            landing: 1200);

        var service = Service(
            Resolution(
                runwayPerformance: baseline,
                conditioned: Conditioned(
                    takeoffGroundRoll: SinglePointGrid(
                        6000,
                        15,
                        1000,
                        900))),
            Airport("KAAA", Runway("18", lengthFeet: 5000)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                takeoff: Conditions(6000, 15, 1000)));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue =>
                issue.Reason == DispatchFeasibilityReason.AircraftTakeoffLengthUnknown);
    }

    [Fact]
    public async Task DefiniteRunwayFailureWinsOverConditionedPerformanceUnknown()
    {
        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(
                    takeoff: 1000,
                    landing: 1000,
                    surfaces: RunwaySurfaceSupport.Asphalt),
                conditioned: Conditioned(
                    takeoffTotal: SinglePointGrid(
                        6000,
                        15,
                        1000,
                        1800))),
            Airport(
                "KAAA",
                Runway(
                    "18",
                    lengthFeet: 5000,
                    surface: RunwaySurface.Grass)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                takeoff: Conditions(7000, 15, 1000)));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Contains(
            result.Issues,
            static issue =>
                issue.Reason
                    == DispatchFeasibilityReason.AircraftTakeoffPerformanceOutsideEnvelope);
        Assert.Contains(
            result.Issues,
            static issue =>
                issue.Reason == DispatchFeasibilityReason.RunwaySurfaceUnsupported);
    }

    [Fact]
    public async Task ZeroConditionedDistanceFailsClosedInsteadOfThrowing()
    {
        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(
                    takeoff: 1000,
                    landing: 1000),
                conditioned: Conditioned(
                    takeoffTotal: SinglePointGrid(
                        6000,
                        15,
                        1000,
                        0))),
            Airport("KAAA", Runway("18", lengthFeet: 5000)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            Requirements(
                takeoff: Conditions(6000, 15, 1000)));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);

        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue =>
                issue.Reason
                    == DispatchFeasibilityReason.AircraftTakeoffPerformanceValueInvalid);

        Assert.Equal(0, issue.RequiredFeet);
        Assert.DoesNotContain(
            result.Issues,
            static issue =>
                issue.Reason == DispatchFeasibilityReason.AircraftTakeoffLengthUnknown);
    }

    [Theory]
    [InlineData(0, 15, 1000)]
    [InlineData(-1, 15, 1000)]
    [InlineData(6000, double.NaN, 1000)]
    [InlineData(6000, 15, double.PositiveInfinity)]
    public async Task InvalidConditionedPerformanceInputsAreRejected(
        double weight,
        double oat,
        double pressureAltitude)
    {
        var service = Service(
            Resolution(
                runwayPerformance: RunwayProfile(),
                conditioned: Conditioned(
                    takeoffTotal: SinglePointGrid(
                        6000,
                        15,
                        1000,
                        1800))),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                Requirements(
                    takeoff: Conditions(
                        weight,
                        oat,
                        pressureAltitude))));
    }

    private static OperationDispatchRequirements Requirements(
        double runwayMarginPercent = 0,
        DispatchRunwayPerformanceConditions? takeoff = null,
        DispatchRunwayPerformanceConditions? landing = null) =>
        new(
            PayloadPounds: 1000,
            RequiredRangeNauticalMiles: 500,
            RunwayLengthSafetyMarginPercent: runwayMarginPercent,
            TakeoffPerformanceConditions: takeoff,
            LandingPerformanceConditions: landing);

    private static DispatchRunwayPerformanceConditions Conditions(
        double weight,
        double oat,
        double pressureAltitude) =>
        new(weight, oat, pressureAltitude);

    private static AircraftPerformanceGrid3D SinglePointGrid(
        double weight,
        double temperature,
        double pressureAltitude,
        double value) =>
        new(
            [weight],
            [temperature],
            [pressureAltitude],
            [value],
            AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius);

    private static AircraftConditionedPerformanceProfile Conditioned(
        AircraftPerformanceGrid3D? takeoffGroundRoll = null,
        AircraftPerformanceGrid3D? takeoffTotal = null,
        AircraftPerformanceGrid3D? landingGroundRoll = null,
        AircraftPerformanceGrid3D? landingTotal = null) =>
        new(
            takeoffGroundRoll,
            takeoffTotal,
            landingGroundRoll,
            landingTotal,
            Array.Empty<AircraftCruisePerformanceProfile>());

    private static AircraftRegistryResolution Resolution(
        AircraftRunwayPerformanceProfile? runwayPerformance,
        AircraftConditionedPerformanceProfile? conditioned) =>
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
                RunwayPerformance: runwayPerformance,
                DispatchPerformance: conditioned is null
                    ? null
                    : new(
                        OperatingEmptyWeightPounds: null,
                        MaximumTakeoffWeightPounds: null,
                        MaximumFuelWeightPounds: null,
                        PayloadRangeEnvelope: null,
                        Confidence: AircraftDataConfidence.Verified,
                        Source: "test",
                        ConditionedPerformance: conditioned))
        ]);

    private static AircraftRunwayPerformanceProfile RunwayProfile(
        double? takeoff = 1800,
        double? landing = 1600,
        double? width = 50,
        RunwaySurfaceSupport? surfaces =
            RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete) =>
        new(
            takeoff,
            landing,
            width,
            surfaces,
            AircraftDataConfidence.Verified,
            "test");

    private static OperationDispatchPlanningService Service(
        AircraftRegistryResolution resolution,
        params AirportRecord[] airports) =>
        new(
            new StubAircraftRegistrySource(resolution),
            new StubAirportDataSource(
                airports.ToDictionary(
                    static airport => airport.Icao,
                    StringComparer.OrdinalIgnoreCase)));

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

    private sealed class StubAircraftRegistrySource(AircraftRegistryResolution result)
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AircraftRegistryResolution?>(result);
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
}
