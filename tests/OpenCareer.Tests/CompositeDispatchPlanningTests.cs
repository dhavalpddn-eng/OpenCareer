using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class CompositeDispatchPlanningTests
{
    [Fact]
    public async Task SuccessfulFuelPlanFlowsIntoExistingFuelCapacityGate()
    {
        AircraftRegistryResolution resolution = Resolution(
            fuelCapacityGallons: 20,
            fuelDensityPoundsPerGallon: 6.0);

        CompositeDispatchPlanningResult result = await Service(
            resolution,
            Airport("KAAA", 5000),
            Airport("KBBB", 5000))
            .EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                CruiseRequest(),
                RouteRequest(cruiseDistance: 300),
                Requirements());

        Assert.Equal(CompositeDispatchPlanningStatus.Evaluated, result.Status);
        Assert.Null(result.Reason);

        RouteFuelWeightPlan fuel = Assert.IsType<RouteFuelWeightPlan>(
            result.FuelWeightPlan);
        Assert.Equal(30, fuel.RequiredFuelGallons, 6);
        Assert.Equal(180, fuel.RequiredFuelPounds, 6);
        Assert.Equal(0, fuel.FuelTypeIndex);

        DispatchFeasibilityResult dispatch =
            Assert.IsType<DispatchFeasibilityResult>(result.DispatchResult);

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, dispatch.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            dispatch.Issues,
            static issue =>
                issue.Reason == DispatchFeasibilityReason.FuelExceedsAircraftMaximum);

        Assert.Equal(180, issue.RequiredPounds);
        Assert.Equal(120, issue.AvailablePounds);
    }

    [Fact]
    public async Task CompositePreservesExistingRunwayPerformanceConditions()
    {
        AircraftRegistryResolution resolution = Resolution(
            fuelCapacityGallons: 100,
            fuelDensityPoundsPerGallon: 6.0,
            takeoffTotalDistance: SinglePointGrid(
                weight: 5800,
                temperature: 20,
                altitude: 1000,
                value: 2500,
                AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius),
            landingTotalDistance: SinglePointGrid(
                weight: 5600,
                temperature: 20,
                altitude: 1000,
                value: 2200,
                AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius));

        OperationDispatchRequirements requirements = Requirements() with
        {
            TakeoffPerformanceConditions = new(
                WeightPounds: 5800,
                OutsideAirTemperatureCelsius: 20,
                PressureAltitudeFeet: 1000),
            LandingPerformanceConditions = new(
                WeightPounds: 5600,
                OutsideAirTemperatureCelsius: 20,
                PressureAltitudeFeet: 1000)
        };

        CompositeDispatchPlanningResult result = await Service(
            resolution,
            Airport("KAAA", 3000),
            Airport("KBBB", 2600))
            .EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                CruiseRequest(),
                RouteRequest(cruiseDistance: 100),
                requirements);

        Assert.Equal(CompositeDispatchPlanningStatus.Evaluated, result.Status);
        DispatchFeasibilityResult dispatch =
            Assert.IsType<DispatchFeasibilityResult>(result.DispatchResult);
        Assert.Equal(DispatchFeasibilityStatus.Feasible, dispatch.Status);
        Assert.Empty(dispatch.Issues);
    }

    [Fact]
    public async Task MatchingReservationFlowsThroughCompositeDispatch()
    {
        AircraftRegistryResolution resolution = Resolution(
            fuelCapacityGallons: 100,
            fuelDensityPoundsPerGallon: 6.0);

        var registry = new StubAircraftRegistrySource(resolution);
        var cruise = new CruisePerformancePlanningService(registry);
        var route = new RouteFuelPlanningService(cruise);
        var fuelWeight = new RouteFuelWeightPlanningService(registry, route);
        var dispatch = new OperationDispatchPlanningService(
            registry,
            new CountingAirportDataSource(
            [
                Airport("KAAA", 5000),
                Airport("KBBB", 5000)
            ]),
            weatherSource: null,
            new StubAircraftAvailabilityStore(
                new AircraftAvailabilityState(
                    resolution.CanonicalAircraftId,
                    AircraftAvailabilityStatus.Unavailable,
                    "dispatch:alpha")));
        var service = new CompositeDispatchPlanningService(
            fuelWeight,
            dispatch);

        CompositeDispatchPlanningResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            CruiseRequest(),
            RouteRequest(cruiseDistance: 100),
            Requirements(),
            "dispatch:alpha");

        Assert.Equal(CompositeDispatchPlanningStatus.Evaluated, result.Status);
        DispatchFeasibilityResult dispatchResult =
            Assert.IsType<DispatchFeasibilityResult>(result.DispatchResult);
        Assert.Equal(DispatchFeasibilityStatus.Feasible, dispatchResult.Status);
        Assert.Empty(dispatchResult.Issues);
    }

    [Fact]
    public async Task FuelPlanningFailureShortCircuitsAirportDispatchLookup()
    {
        AircraftRegistryResolution resolution = Resolution(
            fuelCapacityGallons: 100,
            fuelDensityPoundsPerGallon: null);

        var airportSource = new CountingAirportDataSource(
        [
            Airport("KAAA", 5000),
            Airport("KBBB", 5000)
        ]);

        CompositeDispatchPlanningResult result = await Service(
            resolution,
            airportSource)
            .EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                CruiseRequest(),
                RouteRequest(cruiseDistance: 100),
                Requirements());

        Assert.Equal(
            CompositeDispatchPlanningStatus.InsufficientData,
            result.Status);
        Assert.Equal(
            CompositeDispatchPlanningReason.FuelWeightPlanUnavailable,
            result.Reason);
        Assert.Equal(
            RouteFuelWeightPlanningReason.FuelDensityDataUnavailable,
            result.FuelWeightReason);
        Assert.Null(result.DispatchResult);
        Assert.Equal(0, airportSource.LookupCount);
    }

    [Fact]
    public async Task CruiseFailureReasonPropagatesWithoutDispatch()
    {
        AircraftRegistryResolution resolution = Resolution(
            fuelCapacityGallons: 100,
            fuelDensityPoundsPerGallon: 6.0);

        var airportSource = new CountingAirportDataSource(
        [
            Airport("KAAA", 5000),
            Airport("KBBB", 5000)
        ]);

        CompositeDispatchPlanningResult result = await Service(
            resolution,
            airportSource)
            .EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                CruiseRequest(fuelTypeIndex: 1),
                RouteRequest(cruiseDistance: 100),
                Requirements());

        Assert.Equal(
            CompositeDispatchPlanningStatus.InsufficientData,
            result.Status);
        Assert.Equal(
            RouteFuelWeightPlanningReason.RouteFuelUnavailable,
            result.FuelWeightReason);
        Assert.Equal(
            RouteFuelPlanningReason.CruisePerformanceUnavailable,
            result.RouteFuelReason);
        Assert.Equal(
            CruisePerformancePlanningReason.FuelTypeMismatch,
            result.CruisePerformanceReason);
        Assert.Null(result.DispatchResult);
        Assert.Equal(0, airportSource.LookupCount);
    }

    [Fact]
    public async Task DispatchInsufficientDataRemainsDispatchOutcome()
    {
        AircraftRegistryResolution resolution = Resolution(
            fuelCapacityGallons: 100,
            fuelDensityPoundsPerGallon: 6.0);

        CompositeDispatchPlanningResult result = await Service(
            resolution,
            Airport("KAAA", 5000))
            .EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                CruiseRequest(),
                RouteRequest(cruiseDistance: 100),
                Requirements());

        Assert.Equal(CompositeDispatchPlanningStatus.Evaluated, result.Status);
        Assert.Null(result.Reason);

        DispatchFeasibilityResult dispatch =
            Assert.IsType<DispatchFeasibilityResult>(result.DispatchResult);

        Assert.Equal(
            DispatchFeasibilityStatus.InsufficientData,
            dispatch.Status);
        Assert.Contains(
            dispatch.Issues,
            static issue =>
                issue.Endpoint == DispatchEndpoint.Destination
                && issue.Reason == DispatchFeasibilityReason.AirportNotFound);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CallerCannotPrepopulateCompositeOwnedFuelFields(
        bool plannedFuel,
        bool fuelType)
    {
        AircraftRegistryResolution resolution = Resolution(
            fuelCapacityGallons: 100,
            fuelDensityPoundsPerGallon: 6.0);

        OperationDispatchRequirements requirements = Requirements() with
        {
            PlannedFuelPounds = plannedFuel ? 100 : null,
            PlannedFuelTypeIndex = fuelType ? 0 : null
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service(
                resolution,
                Airport("KAAA", 5000),
                Airport("KBBB", 5000))
                .EvaluateAsync(
                    "fixture-aircraft",
                    "KAAA",
                    "KBBB",
                    CruiseRequest(),
                    RouteRequest(cruiseDistance: 100),
                    requirements));
    }

    private static CompositeDispatchPlanningService Service(
        AircraftRegistryResolution resolution,
        params AirportRecord[] airports) =>
        Service(
            resolution,
            new CountingAirportDataSource(airports));

    private static CompositeDispatchPlanningService Service(
        AircraftRegistryResolution resolution,
        IAirportDataSource airportSource)
    {
        var registry = new StubAircraftRegistrySource(resolution);
        var cruise = new CruisePerformancePlanningService(registry);
        var route = new RouteFuelPlanningService(cruise);
        var fuelWeight = new RouteFuelWeightPlanningService(registry, route);
        var dispatch = new OperationDispatchPlanningService(
            registry,
            airportSource);

        return new CompositeDispatchPlanningService(fuelWeight, dispatch);
    }

    private static CruisePerformancePlanningRequest CruiseRequest(
        int fuelTypeIndex = 0) =>
        new(
            ProfileIndex: 0,
            FuelTypeIndex: fuelTypeIndex,
            PlannedCruiseWeightPounds: 6000,
            IsaDeviationCelsius: 0,
            PressureAltitudeFeet: 12000);

    private static RouteFuelPlanningRequest RouteRequest(
        double cruiseDistance) =>
        new(
            TotalRouteDistanceNauticalMiles: cruiseDistance,
            CruiseSegmentDistanceNauticalMiles: cruiseDistance,
            ClimbTimeMinutes: 0,
            DescentTimeMinutes: 0,
            ClimbFuelGallons: 0,
            DescentFuelGallons: 0,
            ReservePolicy: new(
                ContingencyPercentOfTripFuel: 0,
                FinalReserveMinutesAtCruiseConsumption: 0));

    private static OperationDispatchRequirements Requirements() =>
        new(
            PayloadPounds: 500,
            RequiredRangeNauticalMiles: 300);

    private static AircraftRegistryResolution Resolution(
        double fuelCapacityGallons,
        double? fuelDensityPoundsPerGallon,
        AircraftPerformanceGrid3D? takeoffTotalDistance = null,
        AircraftPerformanceGrid3D? landingTotalDistance = null)
    {
        IReadOnlyList<AircraftFuelDensity>? densities =
            fuelDensityPoundsPerGallon is { } density
                ? [new(0, density)]
                : null;

        var conditioned = new AircraftConditionedPerformanceProfile(
            TakeoffGroundRollDistanceFeet: null,
            TakeoffTotalDistanceFeet: takeoffTotalDistance,
            LandingGroundRollDistanceFeet: null,
            LandingTotalDistanceFeet: landingTotalDistance,
            CruiseProfiles:
            [
                new(
                    Index: 0,
                    ProfileName: "Fixture",
                    FuelTypeIndex: 0,
                    TargetMach: null,
                    TrueAirspeedKnots: SinglePointGrid(
                        6000,
                        0,
                        12000,
                        100,
                        AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius),
                    FuelConsumptionGallonsPerHour: SinglePointGrid(
                        6000,
                        0,
                        12000,
                        10,
                        AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius))
            ]);

        return AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: "fixture-aircraft",
                ProviderId: "test-source",
                ProviderRecordId: "fixture",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: true,
                MaximumPayloadPounds: 2000,
                MaximumRangeNauticalMiles: 1000,
                RunwayPerformance: new(
                    MinimumTakeoffRunwayFeet: 1800,
                    MinimumLandingRunwayFeet: 1600,
                    MinimumRunwayWidthFeet: 50,
                    SupportedSurfaces:
                        RunwaySurfaceSupport.Asphalt
                        | RunwaySurfaceSupport.Concrete,
                    Confidence: AircraftDataConfidence.Verified,
                    Source: "test"),
                DispatchPerformance: new(
                    OperatingEmptyWeightPounds: 5000,
                    MaximumTakeoffWeightPounds: 8000,
                    MaximumFuelWeightPounds: null,
                    PayloadRangeEnvelope: null,
                    Confidence: AircraftDataConfidence.Verified,
                    Source: "test",
                    ConditionedPerformance: conditioned,
                    FuelCapacityGallons: fuelCapacityGallons,
                    FuelDensities: densities))
        ]);
    }

    private static AircraftPerformanceGrid3D SinglePointGrid(
        double weight,
        double temperature,
        double altitude,
        double value,
        AircraftPerformanceTemperatureAxisKind temperatureKind) =>
        new(
            [weight],
            [temperature],
            [altitude],
            [value],
            temperatureKind);

    private static AirportRecord Airport(
        string icao,
        double runwayLengthFeet) =>
        new(
            icao,
            $"{icao} Fixture",
            [
                new(
                    Identifier: "18",
                    UsableLengthFeet: runwayLengthFeet,
                    WidthFeet: 100,
                    Surface: RunwaySurface.Asphalt)
            ]);

    private sealed class StubAircraftRegistrySource(
        AircraftRegistryResolution? result)
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

    private sealed class StubAircraftAvailabilityStore(
        AircraftAvailabilityState? state)
        : IAircraftAvailabilityStore
    {
        public Task<AircraftAvailabilityState?> FindAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AircraftAvailabilityState? result =
                state is not null
                && string.Equals(
                    state.CanonicalAircraftId,
                    canonicalAircraftId,
                    StringComparison.OrdinalIgnoreCase)
                    ? state
                    : null;

            return Task.FromResult(result);
        }

        public Task SetAsync(
            AircraftAvailabilityState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingAirportDataSource
        : IAirportDataSource
    {
        private readonly IReadOnlyDictionary<string, AirportRecord> _airports;

        public CountingAirportDataSource(IEnumerable<AirportRecord> airports)
        {
            _airports = airports.ToDictionary(
                static airport => airport.Icao,
                StringComparer.OrdinalIgnoreCase);
        }

        public int LookupCount { get; private set; }

        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LookupCount++;
            _airports.TryGetValue(icao, out AirportRecord? airport);
            return Task.FromResult(airport);
        }
    }
}
