using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class CruisePerformancePlanningTests
{
    [Fact]
    public async Task ExplicitProfileAndFuelTypeResolveInterpolatedCruisePlan()
    {
        var tas = Grid(
            weights: [5000, 7000],
            isa: [-10, 10],
            altitudes: [10000, 20000],
            values:
            [
                200, 220,
                210, 230,
                180, 200,
                190, 210
            ]);

        var fuel = Grid(
            weights: [5000, 7000],
            isa: [-10, 10],
            altitudes: [10000, 20000],
            values:
            [
                30, 34,
                32, 36,
                40, 44,
                42, 46
            ]);

        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 2,
                    fuelTypeIndex: 1,
                    name: "Economy",
                    mach: 0.55,
                    tas: tas,
                    fuel: fuel)));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            new(
                ProfileIndex: 2,
                FuelTypeIndex: 1,
                PlannedCruiseWeightPounds: 6000,
                IsaDeviationCelsius: 0,
                PressureAltitudeFeet: 15000));

        Assert.Equal(CruisePerformancePlanningStatus.Planned, result.Status);
        Assert.Null(result.Reason);

        CruisePerformancePlan plan = Assert.IsType<CruisePerformancePlan>(result.Plan);
        Assert.Equal(2, plan.ProfileIndex);
        Assert.Equal("Economy", plan.ProfileName);
        Assert.Equal(1, plan.FuelTypeIndex);
        Assert.Equal(6000, plan.PlannedCruiseWeightPounds);
        Assert.Equal(0, plan.IsaDeviationCelsius);
        Assert.Equal(15000, plan.PressureAltitudeFeet);
        Assert.Equal(205, plan.TrueAirspeedKnots, 6);
        Assert.Equal(38, plan.FuelConsumptionGallonsPerHour, 6);
        Assert.Equal(0.55, plan.TargetMach);
    }

    [Fact]
    public async Task PlannerNeverChoosesAnotherProfileWhenRequestedProfileIsMissing()
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: SinglePointGrid(5000, 0, 10000, 200),
                    fuel: SinglePointGrid(5000, 0, 10000, 30)),
                Profile(
                    index: 1,
                    fuelTypeIndex: 0,
                    tas: SinglePointGrid(5000, 0, 10000, 220),
                    fuel: SinglePointGrid(5000, 0, 10000, 35))));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request(profileIndex: 2));

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(CruisePerformancePlanningReason.CruiseProfileNotFound, result.Reason);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task FuelTypeMustMatchSelectedProfileExactly()
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 1,
                    tas: SinglePointGrid(5000, 0, 10000, 200),
                    fuel: SinglePointGrid(5000, 0, 10000, 30))));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request(fuelTypeIndex: 0));

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(CruisePerformancePlanningReason.FuelTypeMismatch, result.Reason);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task MissingTrueAirspeedTableFailsClosed()
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: null,
                    fuel: SinglePointGrid(5000, 0, 10000, 30))));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request());

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.TrueAirspeedTableUnavailable,
            result.Reason);
    }

    [Fact]
    public async Task MissingFuelConsumptionTableFailsClosed()
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: SinglePointGrid(5000, 0, 10000, 200),
                    fuel: null)));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request());

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.FuelConsumptionTableUnavailable,
            result.Reason);
    }

    [Fact]
    public async Task TrueAirspeedLookupNeverExtrapolates()
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: SinglePointGrid(5000, 0, 10000, 200),
                    fuel: SinglePointGrid(5000, 0, 10000, 30))));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request(weight: 5001));

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.TrueAirspeedOutsideEnvelope,
            result.Reason);
    }

    [Fact]
    public async Task FuelLookupMustAlsoCoverRequestedCondition()
    {
        var tas = Grid(
            weights: [5000, 7000],
            isa: [0],
            altitudes: [10000],
            values: [200, 190]);

        var fuel = Grid(
            weights: [5000, 6000],
            isa: [0],
            altitudes: [10000],
            values: [30, 35]);

        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: tas,
                    fuel: fuel)));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request(weight: 6500));

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.FuelConsumptionOutsideEnvelope,
            result.Reason);
    }

    [Fact]
    public async Task ZeroTrueAirspeedFailsClosed()
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: SinglePointGrid(5000, 0, 10000, 0),
                    fuel: SinglePointGrid(5000, 0, 10000, 30))));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request());

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.TrueAirspeedValueInvalid,
            result.Reason);
    }

    [Fact]
    public async Task ZeroFuelConsumptionRemainsAValidPublishedValue()
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: SinglePointGrid(5000, 0, 10000, 200),
                    fuel: SinglePointGrid(5000, 0, 10000, 0))));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "fixture-aircraft",
            Request());

        Assert.Equal(CruisePerformancePlanningStatus.Planned, result.Status);
        Assert.Equal(0, result.Plan!.FuelConsumptionGallonsPerHour);
    }

    [Fact]
    public async Task MissingAircraftIsInsufficientData()
    {
        var service = new CruisePerformancePlanningService(
            new StubAircraftRegistrySource(null));

        CruisePerformancePlanningResult result = await service.PlanAsync(
            "missing-aircraft",
            Request());

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(CruisePerformancePlanningReason.AircraftNotFound, result.Reason);
    }

    [Fact]
    public async Task MissingDispatchPerformanceIsInsufficientData()
    {
        AircraftRegistryResolution resolution = AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: "fixture-aircraft",
                ProviderId: "test-source",
                ProviderRecordId: "fixture",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: true)
        ]);

        CruisePerformancePlanningResult result = await Service(resolution).PlanAsync(
            "fixture-aircraft",
            Request());

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.AircraftDispatchPerformanceUnknown,
            result.Reason);
    }

    [Fact]
    public async Task MissingConditionedPerformanceIsInsufficientData()
    {
        AircraftRegistryResolution resolution = AircraftRegistryResolver.Resolve(
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
                    Source: "test"))
        ]);

        CruisePerformancePlanningResult result = await Service(resolution).PlanAsync(
            "fixture-aircraft",
            Request());

        Assert.Equal(CruisePerformancePlanningStatus.InsufficientData, result.Status);
        Assert.Equal(
            CruisePerformancePlanningReason.AircraftConditionedPerformanceUnknown,
            result.Reason);
    }

    [Theory]
    [InlineData(-1, 0, 5000, 0, 10000)]
    [InlineData(100, 0, 5000, 0, 10000)]
    [InlineData(0, -1, 5000, 0, 10000)]
    [InlineData(0, 0, 0, 0, 10000)]
    [InlineData(0, 0, -1, 0, 10000)]
    [InlineData(0, 0, 5000, double.NaN, 10000)]
    [InlineData(0, 0, 5000, 0, double.PositiveInfinity)]
    public async Task InvalidPlanningInputsAreRejected(
        int profileIndex,
        int fuelTypeIndex,
        double weight,
        double isaDeviation,
        double pressureAltitude)
    {
        CruisePerformancePlanningService service = Service(
            Resolution(
                Profile(
                    index: 0,
                    fuelTypeIndex: 0,
                    tas: SinglePointGrid(5000, 0, 10000, 200),
                    fuel: SinglePointGrid(5000, 0, 10000, 30))));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.PlanAsync(
                "fixture-aircraft",
                new(
                    profileIndex,
                    fuelTypeIndex,
                    weight,
                    isaDeviation,
                    pressureAltitude)));
    }

    private static CruisePerformancePlanningRequest Request(
        int profileIndex = 0,
        int fuelTypeIndex = 0,
        double weight = 5000,
        double isaDeviation = 0,
        double pressureAltitude = 10000) =>
        new(
            profileIndex,
            fuelTypeIndex,
            weight,
            isaDeviation,
            pressureAltitude);

    private static AircraftPerformanceGrid3D SinglePointGrid(
        double weight,
        double isaDeviation,
        double pressureAltitude,
        double value) =>
        Grid(
            [weight],
            [isaDeviation],
            [pressureAltitude],
            [value]);

    private static AircraftPerformanceGrid3D Grid(
        IEnumerable<double> weights,
        IEnumerable<double> isa,
        IEnumerable<double> altitudes,
        IEnumerable<double> values) =>
        new(
            weights,
            isa,
            altitudes,
            values,
            AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius);

    private static AircraftCruisePerformanceProfile Profile(
        int index,
        int fuelTypeIndex,
        AircraftPerformanceGrid3D? tas,
        AircraftPerformanceGrid3D? fuel,
        string? name = null,
        double? mach = null) =>
        new(
            Index: index,
            ProfileName: name,
            FuelTypeIndex: fuelTypeIndex,
            TargetMach: mach,
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
                MaximumPayloadPounds: 3000,
                MaximumRangeNauticalMiles: 1200,
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

    private static CruisePerformancePlanningService Service(
        AircraftRegistryResolution resolution) =>
        new(new StubAircraftRegistrySource(resolution));

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
