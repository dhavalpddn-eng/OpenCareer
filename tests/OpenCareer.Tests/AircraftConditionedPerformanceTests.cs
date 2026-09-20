using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public sealed class AircraftConditionedPerformanceTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public void ThreeDimensionalTablePreservesDocumentedAxisOrder()
    {
        MsfsFlightPerformanceDispatchFacts facts =
            MsfsFlightPerformanceCfgParser.Parse(
                """
                [TAKEOFF_PERFORMANCE]
                takeoff_total_distance_table_by_weight_and_OAT_and_altitude = 1000, 2000 : 0, 20 : 0, 5000 :: 100, 200 : 110, 210 : 120, 220 : 130, 230
                """);

        AircraftPerformanceGrid3D table =
            Assert.IsType<AircraftPerformanceGrid3D>(
                facts.ConditionedPerformance!.TakeoffTotalDistanceFeet);

        Assert.Equal(100, table.TryGetExact(1000, 0, 0));
        Assert.Equal(200, table.TryGetExact(1000, 0, 5000));
        Assert.Equal(110, table.TryGetExact(1000, 20, 0));
        Assert.Equal(210, table.TryGetExact(1000, 20, 5000));
        Assert.Equal(120, table.TryGetExact(2000, 0, 0));
        Assert.Equal(230, table.TryGetExact(2000, 20, 5000));
    }

    [Fact]
    public void InterpolationIsTrilinearAndNeverExtrapolates()
    {
        var table = new AircraftPerformanceGrid3D(
            [1000, 2000],
            [0, 20],
            [0, 5000],
            [100, 200, 110, 210, 120, 220, 130, 230],
            AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius);

        Assert.Equal(165, table.Interpolate(1500, 10, 2500)!.Value, 6);
        Assert.Null(table.Interpolate(999, 10, 2500));
        Assert.Null(table.Interpolate(1500, 21, 2500));
        Assert.Null(table.Interpolate(1500, 10, 5001));
    }

    [Fact]
    public void MalformedTakeoffTableDoesNotDestroyIndependentLandingTable()
    {
        MsfsFlightPerformanceDispatchFacts facts =
            MsfsFlightPerformanceCfgParser.Parse(
                """
                [TAKEOFF_PERFORMANCE]
                takeoff_total_distance_table_by_weight_and_OAT_and_altitude = 1000, 2000 : 0 : 0, 5000 :: 100, 200

                [LANDING_PERFORMANCE]
                landing_total_distance_table_by_weight_and_OAT_and_altitude = 1000 : 0 : 0, 5000 :: 300, 400
                """);

        AircraftConditionedPerformanceProfile conditioned =
            Assert.IsType<AircraftConditionedPerformanceProfile>(
                facts.ConditionedPerformance);

        Assert.Null(conditioned.TakeoffTotalDistanceFeet);
        Assert.NotNull(conditioned.LandingTotalDistanceFeet);
        Assert.Equal(
            400,
            conditioned.LandingTotalDistanceFeet!.TryGetExact(1000, 0, 5000));
    }

    [Fact]
    public void NonMonotonicAxisAndNegativeOutputsFailClosed()
    {
        MsfsFlightPerformanceDispatchFacts facts =
            MsfsFlightPerformanceCfgParser.Parse(
                """
                [TAKEOFF_PERFORMANCE]
                takeoff_ground_roll_distance_table_by_weight_and_OAT_and_altitude = 2000, 1000 : 0 : 0 :: 500 : 400
                takeoff_total_distance_table_by_weight_and_OAT_and_altitude = 1000 : 0 : 0 :: -1
                """);

        Assert.Null(facts.ConditionedPerformance);
    }

    [Fact]
    public void CruiseProfilesPreserveIsaDeviationAndProfileIdentity()
    {
        MsfsFlightPerformanceDispatchFacts facts =
            MsfsFlightPerformanceCfgParser.Parse(
                """
                [CRUISE_PERFORMANCE.0]
                profile_name = "Maximum Cruise"
                fuel_type_idx = 1
                Mach = 0.62
                cruise_TAS_table_by_weight_and_ISA_dev_and_altitude = 5000, 6000 : -20, 0 : 10000, 20000 :: 220, 230 : 215, 225 : 210, 220 : 205, 215
                cruise_fuel_consumption_table_by_weight_and_ISA_dev_and_altitude = 5000, 6000 : -20, 0 : 10000, 20000 :: 40, 45 : 42, 47 : 44, 49 : 46, 51
                """);

        AircraftCruisePerformanceProfile cruise = Assert.Single(
            facts.ConditionedPerformance!.CruiseProfiles);

        Assert.Equal(0, cruise.Index);
        Assert.Equal("Maximum Cruise", cruise.ProfileName);
        Assert.Equal(1, cruise.FuelTypeIndex);
        Assert.Equal(0.62, cruise.TargetMach);

        Assert.Equal(
            AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius,
            cruise.TrueAirspeedKnots!.TemperatureAxisKind);

        Assert.Equal(
            225,
            cruise.TrueAirspeedKnots.TryGetExact(5000, 0, 20000));

        Assert.Equal(
            49,
            cruise.FuelConsumptionGallonsPerHour!.TryGetExact(
                6000,
                -20,
                20000));
    }

    [Fact]
    public void MissingFuelTypeIndexUsesDocumentedZeroDefault()
    {
        MsfsFlightPerformanceDispatchFacts facts =
            MsfsFlightPerformanceCfgParser.Parse(
                """
                [CRUISE_PERFORMANCE.0]
                cruise_TAS_table_by_weight_and_ISA_dev_and_altitude = 5000 : 0 : 10000 :: 200
                """);

        AircraftCruisePerformanceProfile cruise = Assert.Single(
            facts.ConditionedPerformance!.CruiseProfiles);

        Assert.Equal(0, cruise.FuelTypeIndex);
    }

    [Fact]
    public void InvalidFuelTypeIndexRejectsOnlyThatCruiseProfile()
    {
        MsfsFlightPerformanceDispatchFacts facts =
            MsfsFlightPerformanceCfgParser.Parse(
                """
                [TAKEOFF_PERFORMANCE]
                takeoff_total_distance_table_by_weight_and_OAT_and_altitude = 5000 : 15 : 0 :: 1800

                [CRUISE_PERFORMANCE.0]
                fuel_type_idx = invalid
                cruise_TAS_table_by_weight_and_ISA_dev_and_altitude = 5000 : 0 : 10000 :: 200
                """);

        AircraftConditionedPerformanceProfile conditioned =
            Assert.IsType<AircraftConditionedPerformanceProfile>(
                facts.ConditionedPerformance);

        Assert.NotNull(conditioned.TakeoffTotalDistanceFeet);
        Assert.Empty(conditioned.CruiseProfiles);
    }

    [Fact]
    public void NonConsecutiveCruiseProfilesFailClosedWithoutDiscardingTakeoff()
    {
        MsfsFlightPerformanceDispatchFacts facts =
            MsfsFlightPerformanceCfgParser.Parse(
                """
                [TAKEOFF_PERFORMANCE]
                takeoff_total_distance_table_by_weight_and_OAT_and_altitude = 5000 : 15 : 0 :: 1800

                [CRUISE_PERFORMANCE.1]
                cruise_TAS_table_by_weight_and_ISA_dev_and_altitude = 5000 : 0 : 10000 :: 200
                """);

        AircraftConditionedPerformanceProfile conditioned =
            Assert.IsType<AircraftConditionedPerformanceProfile>(
                facts.ConditionedPerformance);

        Assert.NotNull(conditioned.TakeoffTotalDistanceFeet);
        Assert.Empty(conditioned.CruiseProfiles);
    }

    [Fact]
    public async Task ExactAircraftSourceAttachesConditionedTablesWithoutInventingLegacyLimits()
    {
        string root = CreatePackageRoot("Community2024");

        WriteAircraftFile(
            root,
            "conditioned-fixture",
            "aircraft.cfg",
            """
            [FLTSIM.0]
            title = "Conditioned Fixture"
            """);

        WriteAircraftFile(
            root,
            "conditioned-fixture",
            "flight_performance.cfg",
            """
            [TAKEOFF_PERFORMANCE]
            takeoff_ground_roll_distance_table_by_weight_and_OAT_and_altitude = 5000, 6000 : 0, 20 : 0, 5000 :: 1000, 1200 : 1100, 1300 : 1150, 1350 : 1250, 1450
            takeoff_total_distance_table_by_weight_and_OAT_and_altitude = 5000, 6000 : 0, 20 : 0, 5000 :: 1500, 1800 : 1600, 1900 : 1700, 2000 : 1800, 2100

            [LANDING_PERFORMANCE]
            landing_total_distance_table_by_weight_and_OAT_and_altitude = 5000, 6000 : 0 : 0, 5000 :: 1400, 1600 : 1500, 1700

            [CRUISE_PERFORMANCE.0]
            profile_name = "Economy"
            cruise_TAS_table_by_weight_and_ISA_dev_and_altitude = 5000, 6000 : 0 : 10000, 20000 :: 190, 200 : 185, 195
            cruise_fuel_consumption_table_by_weight_and_ISA_dev_and_altitude = 5000, 6000 : 0 : 10000, 20000 :: 30, 32 : 34, 36
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftRegistryObservation observation = Assert.Single(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle(
                    "Conditioned Fixture")));

        AircraftDispatchPerformanceProfile dispatch =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                observation.DispatchPerformance);

        Assert.NotNull(dispatch.ConditionedPerformance);
        Assert.Null(dispatch.PayloadRangeEnvelope);
        Assert.Null(observation.RunwayPerformance);
        Assert.Null(observation.MaximumRangeNauticalMiles);

        AircraftRegistryResolution resolution =
            AircraftRegistryResolver.Resolve([observation]);

        Assert.Same(
            dispatch.ConditionedPerformance,
            resolution.DispatchPerformance!.ConditionedPerformance);

        Assert.Equal(
            MsfsAircraftCfgObservationSource.ProviderId,
            resolution.Provenance[AircraftRegistryField.DispatchPerformance].ProviderId);
    }

    [Fact]
    public void GridRejectsMutableOrMalformedDimensionsAtConstruction()
    {
        Assert.Throws<ArgumentException>(
            () => new AircraftPerformanceGrid3D(
                [1000, 1000],
                [0],
                [0],
                [1, 2],
                AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius));

        Assert.Throws<ArgumentException>(
            () => new AircraftPerformanceGrid3D(
                [1000, 2000],
                [0],
                [0, 5000],
                [1, 2, 3],
                AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius));
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
}
