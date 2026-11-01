using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class RouteFuelPlanningTests
{
    [Fact]
    public void EstimateUsesOnlyCruiseSegmentForCruiseTimeAndFuel()
    {
        CruisePerformancePlan cruise = CruisePlan(
            trueAirspeedKnots: 200,
            fuelGallonsPerHour: 40);

        RouteFuelEstimate estimate = RouteFuelEstimator.Estimate(
            cruise,
            Request(
                totalDistance: 500,
                cruiseDistance: 300,
                climbTime: 20,
                descentTime: 15,
                climbFuel: 10,
                descentFuel: 5,
                contingencyPercent: 0,
                reserveMinutes: 0));

        Assert.Equal(500, estimate.TotalRouteDistanceNauticalMiles);
        Assert.Equal(300, estimate.CruiseSegmentDistanceNauticalMiles);
        Assert.Equal(200, estimate.NonCruiseDistanceNauticalMiles);
        Assert.Equal(90, estimate.CruiseTimeMinutes, 6);
        Assert.Equal(60, estimate.CruiseFuelGallons, 6);
        Assert.Equal(125, estimate.EstimatedAirborneTimeMinutes, 6);
        Assert.Equal(75, estimate.TripFuelGallons, 6);
        Assert.Equal(75, estimate.RequiredFuelGallons, 6);
    }

    [Fact]
    public void ClimbAndDescentTimeDoNotConsumeCruiseFuelAutomatically()
    {
        CruisePerformancePlan cruise = CruisePlan(
            trueAirspeedKnots: 200,
            fuelGallonsPerHour: 40);

        RouteFuelEstimate estimate = RouteFuelEstimator.Estimate(
            cruise,
            Request(
                totalDistance: 300,
                cruiseDistance: 200,
                climbTime: 60,
                descentTime: 60,
                climbFuel: 0,
                descentFuel: 0,
                contingencyPercent: 0,
                reserveMinutes: 0));

        Assert.Equal(60, estimate.CruiseTimeMinutes, 6);
        Assert.Equal(40, estimate.CruiseFuelGallons, 6);
        Assert.Equal(180, estimate.EstimatedAirborneTimeMinutes, 6);
        Assert.Equal(40, estimate.TripFuelGallons, 6);
    }

    [Fact]
    public void ExplicitClimbAndDescentFuelAreAddedWithoutUsingCruiseRate()
    {
        CruisePerformancePlan cruise = CruisePlan(
            trueAirspeedKnots: 240,
            fuelGallonsPerHour: 48);

        RouteFuelEstimate estimate = RouteFuelEstimator.Estimate(
            cruise,
            Request(
                totalDistance: 500,
                cruiseDistance: 480,
                climbTime: 25,
                descentTime: 20,
                climbFuel: 12,
                descentFuel: 8,
                contingencyPercent: 0,
                reserveMinutes: 0));

        Assert.Equal(120, estimate.CruiseTimeMinutes, 6);
        Assert.Equal(96, estimate.CruiseFuelGallons, 6);
        Assert.Equal(116, estimate.TripFuelGallons, 6);
    }

    [Fact]
    public void ContingencyIsExplicitPercentageOfWholeTripFuel()
    {
        CruisePerformancePlan cruise = CruisePlan(
            trueAirspeedKnots: 200,
            fuelGallonsPerHour: 40);

        RouteFuelEstimate estimate = RouteFuelEstimator.Estimate(
            cruise,
            Request(
                totalDistance: 500,
                cruiseDistance: 400,
                climbTime: 20,
                descentTime: 15,
                climbFuel: 10,
                descentFuel: 5,
                contingencyPercent: 10,
                reserveMinutes: 0));

        Assert.Equal(95, estimate.TripFuelGallons, 6);
        Assert.Equal(9.5, estimate.ContingencyFuelGallons, 6);
        Assert.Equal(104.5, estimate.RequiredFuelGallons, 6);
    }

    [Fact]
    public void FinalReserveMinutesUseExplicitCruiseConsumptionBasis()
    {
        CruisePerformancePlan cruise = CruisePlan(
            trueAirspeedKnots: 200,
            fuelGallonsPerHour: 40);

        RouteFuelEstimate estimate = RouteFuelEstimator.Estimate(
            cruise,
            Request(
                totalDistance: 500,
                cruiseDistance: 400,
                climbTime: 20,
                descentTime: 15,
                climbFuel: 10,
                descentFuel: 5,
                contingencyPercent: 0,
                reserveMinutes: 30));

        Assert.Equal(20, estimate.FinalReserveFuelGallons, 6);
        Assert.Equal(115, estimate.RequiredFuelGallons, 6);
    }

    [Fact]
    public void AdditionalReserveFuelIsIndependentFromFinalReserveAndContingency()
    {
        CruisePerformancePlan cruise = CruisePlan(
            trueAirspeedKnots: 200,
            fuelGallonsPerHour: 40);

        RouteFuelEstimate estimate = RouteFuelEstimator.Estimate(
            cruise,
            Request(
                totalDistance: 500,
                cruiseDistance: 400,
                climbTime: 20,
                descentTime: 15,
                climbFuel: 10,
                descentFuel: 5,
                contingencyPercent: 10,
                reserveMinutes: 30,
                additionalReserve: 7));

        Assert.Equal(95, estimate.TripFuelGallons, 6);
        Assert.Equal(9.5, estimate.ContingencyFuelGallons, 6);
        Assert.Equal(20, estimate.FinalReserveFuelGallons, 6);
        Assert.Equal(7, estimate.AdditionalReserveFuelGallons, 6);
        Assert.Equal(131.5, estimate.RequiredFuelGallons, 6);
    }

    [Fact]
    public void ZeroReservePolicyAddsNoHiddenFuel()
    {
        CruisePerformancePlan cruise = CruisePlan(
            trueAirspeedKnots: 180,
            fuelGallonsPerHour: 30);

        RouteFuelEstimate estimate = RouteFuelEstimator.Estimate(
            cruise,
            Request(
                totalDistance: 180,
                cruiseDistance: 180,
                climbTime: 0,
                descentTime: 0,
                climbFuel: 0,
                descentFuel: 0,
                contingencyPercent: 0,
                reserveMinutes: 0));

        Assert.Equal(60, estimate.CruiseTimeMinutes, 6);
        Assert.Equal(30, estimate.TripFuelGallons, 6);
        Assert.Equal(0, estimate.ContingencyFuelGallons);
        Assert.Equal(0, estimate.FinalReserveFuelGallons);
        Assert.Equal(0, estimate.AdditionalReserveFuelGallons);
        Assert.Equal(30, estimate.RequiredFuelGallons, 6);
    }

    [Fact]
    public async Task ServiceComposesSlice15CruiseLookupWithRouteEstimate()
    {
        RouteFuelPlanningService service = Service(
            Resolution(
                Profile(
                    tas: SinglePointGrid(6000, 0, 12000, 210),
                    fuel: SinglePointGrid(6000, 0, 12000, 42))));

        RouteFuelPlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            new(
                ProfileIndex: 0,
                FuelTypeIndex: 0,
                PlannedCruiseWeightPounds: 6000,
                IsaDeviationCelsius: 0,
                PressureAltitudeFeet: 12000),
            Request(
                totalDistance: 450,
                cruiseDistance: 420,
                climbTime: 20,
                descentTime: 15,
                climbFuel: 9,
                descentFuel: 5,
                contingencyPercent: 5,
                reserveMinutes: 30));

        Assert.Equal(RouteFuelPlanningStatus.Planned, result.Status);
        Assert.Null(result.Reason);
        Assert.Null(result.CruisePerformanceReason);

        RouteFuelEstimate estimate = Assert.IsType<RouteFuelEstimate>(result.Estimate);
        Assert.Equal(120, estimate.CruiseTimeMinutes, 6);
        Assert.Equal(84, estimate.CruiseFuelGallons, 6);
        Assert.Equal(98, estimate.TripFuelGallons, 6);
        Assert.Equal(4.9, estimate.ContingencyFuelGallons, 6);
        Assert.Equal(21, estimate.FinalReserveFuelGallons, 6);
        Assert.Equal(123.9, estimate.RequiredFuelGallons, 6);
    }

    [Fact]
    public async Task ServicePreservesSlice15FailureReason()
    {
        RouteFuelPlanningService service = Service(
            Resolution(
                Profile(
                    tas: SinglePointGrid(6000, 0, 12000, 210),
                    fuel: SinglePointGrid(6000, 0, 12000, 42))));

        RouteFuelPlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            new(
                ProfileIndex: 0,
                FuelTypeIndex: 1,
                PlannedCruiseWeightPounds: 6000,
                IsaDeviationCelsius: 0,
                PressureAltitudeFeet: 12000),
            Request());

        Assert.Equal(RouteFuelPlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            RouteFuelPlanningReason.CruisePerformanceUnavailable,
            result.Reason);
        Assert.Equal(
            CruisePerformancePlanningReason.FuelTypeMismatch,
            result.CruisePerformanceReason);
        Assert.Null(result.Estimate);
    }

    [Fact]
    public async Task ServiceDoesNotFallbackWhenCruiseConditionIsOutsideEnvelope()
    {
        RouteFuelPlanningService service = Service(
            Resolution(
                Profile(
                    tas: SinglePointGrid(6000, 0, 12000, 210),
                    fuel: SinglePointGrid(6000, 0, 12000, 42))));

        RouteFuelPlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            new(
                ProfileIndex: 0,
                FuelTypeIndex: 0,
                PlannedCruiseWeightPounds: 6100,
                IsaDeviationCelsius: 0,
                PressureAltitudeFeet: 12000),
            Request());

        Assert.Equal(RouteFuelPlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.TrueAirspeedOutsideEnvelope,
            result.CruisePerformanceReason);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(100, 101)]
    [InlineData(-1, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(100, double.PositiveInfinity)]
    public void InvalidRouteDistancesAreRejected(
        double totalDistance,
        double cruiseDistance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Request(
                totalDistance: totalDistance,
                cruiseDistance: cruiseDistance).Validate());
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0, 0)]
    public void InvalidNonCruiseInputsAreRejected(
        double climbTime,
        double descentTime,
        double climbFuel,
        double descentFuel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Request(
                climbTime: climbTime,
                descentTime: descentTime,
                climbFuel: climbFuel,
                descentFuel: descentFuel).Validate());
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(101, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(double.NaN, 0, 0)]
    public void InvalidReservePolicyIsRejected(
        double contingencyPercent,
        double reserveMinutes,
        double additionalReserve)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RouteFuelReservePolicy(
                contingencyPercent,
                reserveMinutes,
                additionalReserve).Validate());
    }

    private static RouteFuelPlanningRequest Request(
        double totalDistance = 500,
        double cruiseDistance = 400,
        double climbTime = 20,
        double descentTime = 15,
        double climbFuel = 10,
        double descentFuel = 5,
        double contingencyPercent = 5,
        double reserveMinutes = 30,
        double additionalReserve = 0) =>
        new(
            TotalRouteDistanceNauticalMiles: totalDistance,
            CruiseSegmentDistanceNauticalMiles: cruiseDistance,
            ClimbTimeMinutes: climbTime,
            DescentTimeMinutes: descentTime,
            ClimbFuelGallons: climbFuel,
            DescentFuelGallons: descentFuel,
            ReservePolicy: new(
                ContingencyPercentOfTripFuel: contingencyPercent,
                FinalReserveMinutesAtCruiseConsumption: reserveMinutes,
                AdditionalReserveFuelGallons: additionalReserve));

    private static CruisePerformancePlan CruisePlan(
        double trueAirspeedKnots,
        double fuelGallonsPerHour) =>
        new(
            ProfileIndex: 0,
            ProfileName: "Fixture",
            FuelTypeIndex: 0,
            PlannedCruiseWeightPounds: 6000,
            IsaDeviationCelsius: 0,
            PressureAltitudeFeet: 12000,
            TrueAirspeedKnots: trueAirspeedKnots,
            FuelConsumptionGallonsPerHour: fuelGallonsPerHour,
            TargetMach: null);

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

    private static AircraftCruisePerformanceProfile Profile(
        AircraftPerformanceGrid3D? tas,
        AircraftPerformanceGrid3D? fuel) =>
        new(
            Index: 0,
            ProfileName: "Fixture",
            FuelTypeIndex: 0,
            TargetMach: null,
            TrueAirspeedKnots: tas,
            FuelConsumptionGallonsPerHour: fuel);

    private static AircraftRegistryResolution Resolution(
        params AircraftCruisePerformanceProfile[] profiles) =>
        AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: "fixture-aircraft",
                ProviderId: "test-source",
                ProviderRecordId: "fixture",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: true,
                DispatchPerformance: new(
                    OperatingEmptyWeightPounds: null,
                    MaximumTakeoffWeightPounds: null,
                    MaximumFuelWeightPounds: null,
                    PayloadRangeEnvelope: null,
                    Confidence: AircraftDataConfidence.Verified,
                    Source: "test",
                    ConditionedPerformance: new(
                        TakeoffGroundRollDistanceFeet: null,
                        TakeoffTotalDistanceFeet: null,
                        LandingGroundRollDistanceFeet: null,
                        LandingTotalDistanceFeet: null,
                        CruiseProfiles: profiles)))
        ]);

    private static RouteFuelPlanningService Service(
        AircraftRegistryResolution resolution)
    {
        var registry = new StubAircraftRegistrySource(resolution);
        var cruise = new CruisePerformancePlanningService(registry);
        return new RouteFuelPlanningService(cruise);
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
