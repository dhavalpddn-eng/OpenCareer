using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public sealed class FuelDensityWeightBridgeTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LocalParserPreservesFuelDensityIndices()
    {
        string root = CreatePackageRoot("Community2024");

        WriteAircraftFile(
            root,
            "multi-fuel",
            "aircraft.cfg",
            """
            [FLTSIM.0]
            title = "Multi Fuel Density Fixture"
            """);

        WriteAircraftFile(
            root,
            "multi-fuel",
            "flight_performance.cfg",
            """
            [ENGINE_PERFORMANCE]
            fuel_density_table = 6.0, 6.7, 4.2

            [AIRCRAFT_LOADING]
            fuel_capacity = 120

            [CRUISE_PERFORMANCE.0]
            fuel_type_idx = 1
            cruise_TAS_table_by_weight_and_ISA_dev_and_altitude = 6000 : 0 : 12000 :: 200
            cruise_fuel_consumption_table_by_weight_and_ISA_dev_and_altitude = 6000 : 0 : 12000 :: 40
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                Assert.Single(
                    await source.FindAircraftObservationsAsync(
                        AircraftCanonicalIdentity.FromMsfsTitle(
                            "Multi Fuel Density Fixture")))
                .DispatchPerformance);

        Assert.Equal(120, profile.FuelCapacityGallons);
        Assert.Null(profile.MaximumFuelWeightPounds);
        Assert.Equal(3, profile.FuelDensities!.Count);
        Assert.Equal(6.0, profile.FuelDensities[0].PoundsPerGallon);
        Assert.Equal(6.7, profile.FuelDensities[1].PoundsPerGallon);
        Assert.Equal(4.2, profile.FuelDensities[2].PoundsPerGallon);
        Assert.Equal(1, profile.ConditionedPerformance!.CruiseProfiles.Single().FuelTypeIndex);
    }

    [Fact]
    public async Task InvalidDensityListFailsClosedWithoutInventingFuelWeight()
    {
        string root = CreatePackageRoot("Community2024");

        WriteAircraftFile(
            root,
            "invalid-fuel",
            "aircraft.cfg",
            """
            [FLTSIM.0]
            title = "Invalid Fuel Density Fixture"
            """);

        WriteAircraftFile(
            root,
            "invalid-fuel",
            "flight_performance.cfg",
            """
            [ENGINE_PERFORMANCE]
            fuel_density_table = 6.0, invalid

            [AIRCRAFT_LOADING]
            fuel_capacity = 100
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                Assert.Single(
                    await source.FindAircraftObservationsAsync(
                        AircraftCanonicalIdentity.FromMsfsTitle(
                            "Invalid Fuel Density Fixture")))
                .DispatchPerformance);

        Assert.Equal(100, profile.FuelCapacityGallons);
        Assert.Null(profile.FuelDensities);
        Assert.Null(profile.MaximumFuelWeightPounds);
    }

    [Fact]
    public void BridgeUsesExactSelectedFuelTypeDensity()
    {
        RouteFuelEstimate estimate = Estimate(
            fuelTypeIndex: 1,
            requiredGallons: 50);

        AircraftDispatchPerformanceProfile profile = DispatchProfile(
            fuelCapacityGallons: 100,
            fuelDensities:
            [
                new(0, 6.0),
                new(1, 6.7)
            ]);

        FuelWeightConversionResult result =
            RouteFuelWeightBridge.Convert(estimate, profile);

        Assert.Equal(FuelWeightConversionStatus.Converted, result.Status);
        RouteFuelWeightPlan plan =
            Assert.IsType<RouteFuelWeightPlan>(result.Plan);

        Assert.Equal(1, plan.FuelTypeIndex);
        Assert.Equal(6.7, plan.FuelDensityPoundsPerGallon);
        Assert.Equal(50, plan.RequiredFuelGallons);
        Assert.Equal(335, plan.RequiredFuelPounds, 6);
        Assert.Equal(100, plan.MaximumFuelGallons);
        Assert.Equal(670, plan.MaximumFuelPounds, 6);
    }

    [Fact]
    public void BridgeNeverSubstitutesAnotherFuelDensity()
    {
        RouteFuelEstimate estimate = Estimate(
            fuelTypeIndex: 1,
            requiredGallons: 50);

        AircraftDispatchPerformanceProfile profile = DispatchProfile(
            fuelCapacityGallons: 100,
            fuelDensities:
            [
                new(0, 6.0)
            ]);

        FuelWeightConversionResult result =
            RouteFuelWeightBridge.Convert(estimate, profile);

        Assert.Equal(
            FuelWeightConversionStatus.InsufficientData,
            result.Status);
        Assert.Equal(
            FuelWeightConversionReason.FuelTypeDensityUnavailable,
            result.Reason);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void BridgeFailsClosedWhenDensityDataIsMissing()
    {
        FuelWeightConversionResult result =
            RouteFuelWeightBridge.Convert(
                Estimate(fuelTypeIndex: 0, requiredGallons: 50),
                DispatchProfile(
                    fuelCapacityGallons: 100,
                    fuelDensities: null));

        Assert.Equal(
            FuelWeightConversionStatus.InsufficientData,
            result.Status);
        Assert.Equal(
            FuelWeightConversionReason.FuelDensityDataUnavailable,
            result.Reason);
    }

    [Fact]
    public void IndependentFuelWeightLimitCannotBeWeakenedByDensityCapacity()
    {
        AircraftDispatchPerformanceProfile profile = DispatchProfile(
            maximumFuelWeightPounds: 620,
            fuelCapacityGallons: 100,
            fuelDensities:
            [
                new(0, 6.7)
            ]);

        Assert.Equal(
            620,
            profile.ResolveMaximumFuelWeightPounds(0));
    }

    [Fact]
    public void WeightPlanAppliesPoundsAndFuelTypeToDispatchRequirements()
    {
        var plan = new RouteFuelWeightPlan(
            FuelTypeIndex: 1,
            FuelDensityPoundsPerGallon: 6.7,
            RequiredFuelGallons: 50,
            RequiredFuelPounds: 335,
            MaximumFuelGallons: 100,
            MaximumFuelPounds: 670);

        OperationDispatchRequirements updated = plan.ApplyTo(
            new(
                PayloadPounds: 500,
                RequiredRangeNauticalMiles: 300));

        Assert.Equal(335, updated.PlannedFuelPounds);
        Assert.Equal(1, updated.PlannedFuelTypeIndex);
    }

    [Fact]
    public void ExistingDispatchGateUsesSelectedFuelTypeCapacity()
    {
        AircraftRegistryResolution aircraft = Resolution(
            DispatchProfile(
                operatingEmptyWeight: 5000,
                maximumTakeoffWeight: 9000,
                maximumFuelWeightPounds: null,
                fuelCapacityGallons: 100,
                fuelDensities:
                [
                    new(0, 6.0),
                    new(1, 7.0)
                ]));

        DispatchFeasibilityResult typeZero =
            OperationDispatchPhysicalEvaluator.Evaluate(
                aircraft,
                new(
                    PayloadPounds: 500,
                    RequiredRangeNauticalMiles: 300,
                    PlannedFuelPounds: 650,
                    PlannedFuelTypeIndex: 0),
                Airport("KAAA"),
                Airport("KBBB"));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, typeZero.Status);
        DispatchFeasibilityIssue overFuel = Assert.Single(
            typeZero.Issues,
            static issue =>
                issue.Reason == DispatchFeasibilityReason.FuelExceedsAircraftMaximum);
        Assert.Equal(650, overFuel.RequiredPounds);
        Assert.Equal(600, overFuel.AvailablePounds);

        DispatchFeasibilityResult typeOne =
            OperationDispatchPhysicalEvaluator.Evaluate(
                aircraft,
                new(
                    PayloadPounds: 500,
                    RequiredRangeNauticalMiles: 300,
                    PlannedFuelPounds: 650,
                    PlannedFuelTypeIndex: 1),
                Airport("KAAA"),
                Airport("KBBB"));

        Assert.Equal(DispatchFeasibilityStatus.Feasible, typeOne.Status);
    }

    [Fact]
    public void MultiFuelDispatchWithoutFuelTypeRemainsInsufficient()
    {
        AircraftRegistryResolution aircraft = Resolution(
            DispatchProfile(
                operatingEmptyWeight: 5000,
                maximumTakeoffWeight: 9000,
                maximumFuelWeightPounds: null,
                fuelCapacityGallons: 100,
                fuelDensities:
                [
                    new(0, 6.0),
                    new(1, 7.0)
                ]));

        DispatchFeasibilityResult result =
            OperationDispatchPhysicalEvaluator.Evaluate(
                aircraft,
                new(
                    PayloadPounds: 500,
                    RequiredRangeNauticalMiles: 300,
                    PlannedFuelPounds: 500),
                Airport("KAAA"),
                Airport("KBBB"));

        Assert.Equal(
            DispatchFeasibilityStatus.InsufficientData,
            result.Status);
        Assert.Contains(
            result.Issues,
            static issue =>
                issue.Reason
                == DispatchFeasibilityReason.AircraftMaximumFuelWeightUnknown);
    }

    [Fact]
    public async Task PlanningServiceConvertsSlice16FuelWithSelectedDensity()
    {
        AircraftRegistryResolution resolution = Resolution(
            DispatchProfile(
                fuelCapacityGallons: 100,
                fuelDensities:
                [
                    new(0, 6.0),
                    new(1, 6.7)
                ],
                conditionedPerformance: new(
                    TakeoffGroundRollDistanceFeet: null,
                    TakeoffTotalDistanceFeet: null,
                    LandingGroundRollDistanceFeet: null,
                    LandingTotalDistanceFeet: null,
                    CruiseProfiles:
                    [
                        new(
                            Index: 0,
                            ProfileName: "Fixture",
                            FuelTypeIndex: 1,
                            TargetMach: null,
                            TrueAirspeedKnots: SinglePointGrid(6000, 0, 12000, 200),
                            FuelConsumptionGallonsPerHour: SinglePointGrid(6000, 0, 12000, 40))
                    ])));

        RouteFuelWeightPlanningService service = Service(resolution);

        RouteFuelWeightPlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            new(
                ProfileIndex: 0,
                FuelTypeIndex: 1,
                PlannedCruiseWeightPounds: 6000,
                IsaDeviationCelsius: 0,
                PressureAltitudeFeet: 12000),
            new(
                TotalRouteDistanceNauticalMiles: 100,
                CruiseSegmentDistanceNauticalMiles: 100,
                ClimbTimeMinutes: 0,
                DescentTimeMinutes: 0,
                ClimbFuelGallons: 0,
                DescentFuelGallons: 0,
                ReservePolicy: new(
                    ContingencyPercentOfTripFuel: 0,
                    FinalReserveMinutesAtCruiseConsumption: 0)));

        Assert.Equal(RouteFuelWeightPlanningStatus.Planned, result.Status);
        RouteFuelWeightPlan plan =
            Assert.IsType<RouteFuelWeightPlan>(result.FuelWeightPlan);

        Assert.Equal(20, plan.RequiredFuelGallons, 6);
        Assert.Equal(134, plan.RequiredFuelPounds, 6);
        Assert.Equal(1, plan.FuelTypeIndex);
    }

    [Fact]
    public async Task PlanningServicePreservesSlice15FailureBeforeDensityLookup()
    {
        AircraftRegistryResolution resolution = Resolution(
            DispatchProfile(
                fuelCapacityGallons: 100,
                fuelDensities:
                [
                    new(0, 6.0)
                ],
                conditionedPerformance: new(
                    TakeoffGroundRollDistanceFeet: null,
                    TakeoffTotalDistanceFeet: null,
                    LandingGroundRollDistanceFeet: null,
                    LandingTotalDistanceFeet: null,
                    CruiseProfiles:
                    [
                        new(
                            Index: 0,
                            ProfileName: "Fixture",
                            FuelTypeIndex: 0,
                            TargetMach: null,
                            TrueAirspeedKnots: SinglePointGrid(6000, 0, 12000, 200),
                            FuelConsumptionGallonsPerHour: SinglePointGrid(6000, 0, 12000, 40))
                    ])));

        RouteFuelWeightPlanningResult result = await Service(resolution).PlanAsync(
            "fixture-aircraft",
            new(
                ProfileIndex: 0,
                FuelTypeIndex: 1,
                PlannedCruiseWeightPounds: 6000,
                IsaDeviationCelsius: 0,
                PressureAltitudeFeet: 12000),
            new(
                TotalRouteDistanceNauticalMiles: 100,
                CruiseSegmentDistanceNauticalMiles: 100,
                ClimbTimeMinutes: 0,
                DescentTimeMinutes: 0,
                ClimbFuelGallons: 0,
                DescentFuelGallons: 0,
                ReservePolicy: new(0, 0)));

        Assert.Equal(
            RouteFuelWeightPlanningStatus.InsufficientData,
            result.Status);
        Assert.Equal(
            RouteFuelWeightPlanningReason.RouteFuelUnavailable,
            result.Reason);
        Assert.Equal(
            CruisePerformancePlanningReason.FuelTypeMismatch,
            result.CruisePerformanceReason);
        Assert.Null(result.FuelWeightPlan);
    }

    [Fact]
    public void FuelDensityIndicesMustBeUniqueAndPositive()
    {
        AircraftDispatchPerformanceProfile duplicate = DispatchProfile(
            fuelDensities:
            [
                new(0, 6.0),
                new(0, 6.7)
            ]);

        Assert.Throws<ArgumentException>(duplicate.Validate);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AircraftFuelDensity(0, 0).Validate());
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempRoot))
            return;

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private RouteFuelWeightPlanningService Service(
        AircraftRegistryResolution resolution)
    {
        var registry = new StubAircraftRegistrySource(resolution);
        var cruise = new CruisePerformancePlanningService(registry);
        var route = new RouteFuelPlanningService(cruise);
        return new RouteFuelWeightPlanningService(registry, route);
    }

    private static RouteFuelEstimate Estimate(
        int fuelTypeIndex,
        double requiredGallons)
    {
        var cruise = new CruisePerformancePlan(
            ProfileIndex: 0,
            ProfileName: "Fixture",
            FuelTypeIndex: fuelTypeIndex,
            PlannedCruiseWeightPounds: 6000,
            IsaDeviationCelsius: 0,
            PressureAltitudeFeet: 12000,
            TrueAirspeedKnots: 200,
            FuelConsumptionGallonsPerHour: 40,
            TargetMach: null);

        return new RouteFuelEstimate(
            CruisePerformance: cruise,
            TotalRouteDistanceNauticalMiles: 100,
            CruiseSegmentDistanceNauticalMiles: 100,
            NonCruiseDistanceNauticalMiles: 0,
            ClimbTimeMinutes: 0,
            CruiseTimeMinutes: 30,
            DescentTimeMinutes: 0,
            EstimatedAirborneTimeMinutes: 30,
            ClimbFuelGallons: 0,
            CruiseFuelGallons: requiredGallons,
            DescentFuelGallons: 0,
            TripFuelGallons: requiredGallons,
            ContingencyFuelGallons: 0,
            FinalReserveFuelGallons: 0,
            AdditionalReserveFuelGallons: 0,
            RequiredFuelGallons: requiredGallons);
    }

    private static AircraftDispatchPerformanceProfile DispatchProfile(
        double? operatingEmptyWeight = 5000,
        double? maximumTakeoffWeight = 9000,
        double? maximumFuelWeightPounds = null,
        double? fuelCapacityGallons = null,
        IReadOnlyList<AircraftFuelDensity>? fuelDensities = null,
        AircraftConditionedPerformanceProfile? conditionedPerformance = null) =>
        new(
            OperatingEmptyWeightPounds: operatingEmptyWeight,
            MaximumTakeoffWeightPounds: maximumTakeoffWeight,
            MaximumFuelWeightPounds: maximumFuelWeightPounds,
            PayloadRangeEnvelope: null,
            Confidence: AircraftDataConfidence.Verified,
            Source: "test",
            ConditionedPerformance: conditionedPerformance,
            FuelCapacityGallons: fuelCapacityGallons,
            FuelDensities: fuelDensities);

    private static AircraftRegistryResolution Resolution(
        AircraftDispatchPerformanceProfile dispatch) =>
        AircraftRegistryResolver.Resolve(
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
                    MinimumTakeoffRunwayFeet: 1500,
                    MinimumLandingRunwayFeet: 1400,
                    MinimumRunwayWidthFeet: 50,
                    SupportedSurfaces:
                        RunwaySurfaceSupport.Asphalt
                        | RunwaySurfaceSupport.Concrete,
                    Confidence: AircraftDataConfidence.Verified,
                    Source: "test"),
                DispatchPerformance: dispatch)
        ]);

    private static AirportRecord Airport(string icao) =>
        new(
            icao,
            $"{icao} Fixture",
            [
                new(
                    Identifier: "18",
                    UsableLengthFeet: 5000,
                    WidthFeet: 100,
                    Surface: RunwaySurface.Asphalt)
            ]);

    private static AircraftPerformanceGrid3D SinglePointGrid(
        double weight,
        double isaDeviation,
        double pressureAltitude,
        double value) =>
        new(
            [weight],
            [isaDeviation],
            [pressureAltitude],
            [value],
            AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius);

    private string CreatePackageRoot(string name)
    {
        string root = Path.Combine(_tempRoot, "Packages", name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteAircraftFile(
        string root,
        string packageName,
        string fileName,
        string content)
    {
        string aircraftDirectory = Path.Combine(
            root,
            packageName,
            "SimObjects",
            "Airplanes",
            "Aircraft");

        Directory.CreateDirectory(aircraftDirectory);
        File.WriteAllText(
            Path.Combine(aircraftDirectory, fileName),
            content);
    }

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
}
