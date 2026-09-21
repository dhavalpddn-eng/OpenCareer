using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public sealed class AircraftCfgEnrichmentTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExactTitleEnrichesRangeEngineAndReferenceMetadata()
    {
        string root = CreatePackageRoot("Community2024");
        WriteAircraftCfg(
            root,
            "vendor-aircraft",
            """
            [Version]
            major = 1
            minor = 0

            [GENERAL]
            icao_type_designator = "DA62"
            icao_manufacturer = "DIAMOND"
            icao_model = "DA 62"
            icao_engine_type = "Piston"
            icao_engine_count = 2

            [FLTSIM.0]
            title = "DA62 Asobo"
            ui_max_range = 1283.5
            capacity = 5 ; cabin/passenger capacity, not crew
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);
        string id = AircraftCanonicalIdentity.FromMsfsTitle("DA62 Asobo");

        AircraftRegistryObservation observation = Assert.Single(
            await source.FindAircraftObservationsAsync(id));

        Assert.Equal(MsfsAircraftCfgObservationSource.ProviderId, observation.ProviderId);
        Assert.Equal(AircraftDataConfidence.Reference, observation.Confidence);
        Assert.False(observation.IsInstalled);
        Assert.Equal(1283.5, observation.MaximumRangeNauticalMiles);
        Assert.Equal(2, observation.EngineCount);
        Assert.Null(observation.Seats);

        AircraftReferenceMetadata metadata = Assert.IsType<AircraftReferenceMetadata>(
            observation.ReferenceMetadata);
        Assert.Equal("DA62", metadata.IcaoTypeDesignator);
        Assert.Equal("DIAMOND", metadata.IcaoManufacturer);
        Assert.Equal("DA 62", metadata.IcaoModel);
        Assert.Equal(AircraftEngineType.Piston, metadata.EngineType);
        Assert.Equal(5, metadata.PassengerCapacity);
        Assert.Null(observation.DispatchPerformance);
        Assert.DoesNotContain(_tempRoot, observation.ProviderRecordId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultipleVariationsUseOnlyTheExactSimConnectTitle()
    {
        string root = CreatePackageRoot("Community2024");
        WriteAircraftCfg(
            root,
            "multi-variation",
            """
            [GENERAL]
            icao_engine_type = "Jet"
            icao_engine_count = 2

            [FLTSIM.0]
            title = "A320neo Default"
            ui_max_range = 3300
            capacity = 180

            [FLTSIM.1]
            title = "A320neo Cargo"
            ui_max_range = 3100
            capacity = 2
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftRegistryObservation cargo = Assert.Single(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle("A320neo Cargo")));

        Assert.Equal(3100, cargo.MaximumRangeNauticalMiles);
        Assert.Equal(2, cargo.ReferenceMetadata!.PassengerCapacity);

        Assert.Empty(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle("A320neo")));
    }

    [Fact]
    public async Task InvalidOrMissingValuesStayUnknownInsteadOfBecomingDefaults()
    {
        string root = CreatePackageRoot("Official2024");
        WriteAircraftCfg(
            root,
            "bad-metadata",
            """
            [GENERAL]
            icao_type_designator = "TEST"
            icao_engine_type = "Warp"
            icao_engine_count = not-a-number

            [FLTSIM.0]
            title = "Test Aircraft"
            ui_max_range = -1
            capacity = invalid
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftRegistryObservation observation = Assert.Single(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle("Test Aircraft")));

        Assert.Null(observation.MaximumRangeNauticalMiles);
        Assert.Null(observation.EngineCount);
        Assert.Null(observation.Seats);
        Assert.Null(observation.ReferenceMetadata!.EngineType);
        Assert.Null(observation.ReferenceMetadata.PassengerCapacity);
        Assert.Equal("TEST", observation.ReferenceMetadata.IcaoTypeDesignator);
    }

    [Fact]
    public async Task EmptyGliderEngineTypeIsKnownAsNoEngine()
    {
        string root = CreatePackageRoot("Community2024");
        WriteAircraftCfg(
            root,
            "glider",
            """
            [GENERAL]
            icao_engine_type = ""
            icao_engine_count = 0

            [FLTSIM.0]
            title = "Fixture Glider"
            ui_max_range = 250
            capacity = 1
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);
        AircraftRegistryObservation observation = Assert.Single(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle("Fixture Glider")));

        Assert.Equal(0, observation.EngineCount);
        Assert.Equal(AircraftEngineType.None, observation.ReferenceMetadata!.EngineType);
    }

    [Fact]
    public async Task DuplicateLocalDefinitionsFailClosed()
    {
        string community = CreatePackageRoot("Community2024");
        string official = CreatePackageRoot("Official2024");

        const string cfg = """
            [GENERAL]
            icao_engine_count = 1

            [FLTSIM.0]
            title = "Duplicate Aircraft"
            ui_max_range = 500
            """;

        WriteAircraftCfg(community, "package-a", cfg);
        WriteAircraftCfg(official, "package-b", cfg);

        var source = new MsfsAircraftCfgObservationSource([community, official]);

        Assert.Empty(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle("Duplicate Aircraft")));
    }

    [Fact]
    public async Task MissingOrStreamedWithoutCfgProducesNoFabricatedObservation()
    {
        string streamed = CreatePackageRoot("StreamedPackages");
        Directory.CreateDirectory(Path.Combine(streamed, "streamed-aircraft"));
        File.WriteAllText(
            Path.Combine(streamed, "streamed-aircraft", "manifest.json"),
            "{}");

        var source = new MsfsAircraftCfgObservationSource(
        [
            streamed,
            Path.Combine(_tempRoot, "does-not-exist")
        ]);

        Assert.Empty(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle("Streamed Aircraft")));
    }

    [Fact]
    public async Task FlightModelWeightFactsPopulateDispatchProfileWithoutRelabelingEmptyWeight()
    {
        string root = CreatePackageRoot("Official2024");

        WriteAircraftCfg(
            root,
            "weight-fixture",
            """
            [GENERAL]
            icao_engine_count = 2

            [FLTSIM.0]
            title = "Weight Fixture"
            """);

        WriteAircraftFile(
            root,
            "weight-fixture",
            "flight_model.cfg",
            """
            [WEIGHT_AND_BALANCE]
            max_gross_weight = 9000
            max_takeoff_weight = 8500
            max_landing_weight = 8200
            max_zero_fuel_weight = 7000
            empty_weight = 5000
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftRegistryObservation observation = Assert.Single(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle("Weight Fixture")));

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                observation.DispatchPerformance);

        Assert.Null(profile.OperatingEmptyWeightPounds);
        Assert.Equal(5000, profile.ConfiguredEmptyWeightPounds);
        Assert.Equal(8500, profile.MaximumTakeoffWeightPounds);
        Assert.Equal(8200, profile.MaximumLandingWeightPounds);
        Assert.Equal(7000, profile.MaximumZeroFuelWeightPounds);
        Assert.Null(profile.MaximumFuelWeightPounds);
        Assert.Equal(AircraftDataConfidence.Reference, profile.Confidence);
    }

    [Fact]
    public async Task MissingTakeoffAndLandingWeightsUseDocumentedGrossWeightFallback()
    {
        string root = CreatePackageRoot("Official2024");

        WriteAircraftCfg(
            root,
            "fallback-fixture",
            """
            [FLTSIM.0]
            title = "Fallback Fixture"
            """);

        WriteAircraftFile(
            root,
            "fallback-fixture",
            "flight_model.cfg",
            """
            [WEIGHT_AND_BALANCE]
            max_gross_weight = 9100
            empty_weight = 5000
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                Assert.Single(
                    await source.FindAircraftObservationsAsync(
                        AircraftCanonicalIdentity.FromMsfsTitle(
                            "Fallback Fixture")))
                .DispatchPerformance);

        Assert.Equal(9100, profile.MaximumTakeoffWeightPounds);
        Assert.Equal(9100, profile.MaximumLandingWeightPounds);
        Assert.Equal(9100, profile.MaximumZeroFuelWeightPounds);
    }

    [Fact]
    public async Task InvalidExplicitTakeoffWeightDoesNotSilentlyUseGrossWeight()
    {
        string root = CreatePackageRoot("Official2024");

        WriteAircraftCfg(
            root,
            "invalid-takeoff",
            """
            [FLTSIM.0]
            title = "Invalid Takeoff Fixture"
            """);

        WriteAircraftFile(
            root,
            "invalid-takeoff",
            "flight_model.cfg",
            """
            [WEIGHT_AND_BALANCE]
            max_gross_weight = 9000
            max_takeoff_weight = invalid
            empty_weight = 5000
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                Assert.Single(
                    await source.FindAircraftObservationsAsync(
                        AircraftCanonicalIdentity.FromMsfsTitle(
                            "Invalid Takeoff Fixture")))
                .DispatchPerformance);

        Assert.Null(profile.MaximumTakeoffWeightPounds);
        Assert.Equal(9000, profile.MaximumLandingWeightPounds);
        Assert.Equal(9000, profile.MaximumZeroFuelWeightPounds);
        Assert.Equal(5000, profile.ConfiguredEmptyWeightPounds);
    }

    [Fact]
    public async Task SingleFuelDensityConvertsDocumentedCapacityToMaximumFuelWeight()
    {
        string root = CreatePackageRoot("Community2024");

        WriteAircraftCfg(
            root,
            "fuel-fixture",
            """
            [FLTSIM.0]
            title = "Fuel Fixture"
            """);

        WriteAircraftFile(
            root,
            "fuel-fixture",
            "flight_performance.cfg",
            """
            [ENGINE_PERFORMANCE]
            fuel_density_table = 6.7

            [AIRCRAFT_LOADING]
            fuel_capacity = 100
            passenger_capacity = 4
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                Assert.Single(
                    await source.FindAircraftObservationsAsync(
                        AircraftCanonicalIdentity.FromMsfsTitle(
                            "Fuel Fixture")))
                .DispatchPerformance);

        Assert.Equal(670, profile.MaximumFuelWeightPounds);
        Assert.Null(profile.MaximumTakeoffWeightPounds);
    }

    [Fact]
    public async Task MultipleFuelDensitiesRemainUnknownInsteadOfChoosingOne()
    {
        string root = CreatePackageRoot("Community2024");

        WriteAircraftCfg(
            root,
            "multi-fuel-fixture",
            """
            [FLTSIM.0]
            title = "Multi Fuel Fixture"
            """);

        WriteAircraftFile(
            root,
            "multi-fuel-fixture",
            "flight_model.cfg",
            """
            [WEIGHT_AND_BALANCE]
            max_takeoff_weight = 8000
            empty_weight = 4500
            """);

        WriteAircraftFile(
            root,
            "multi-fuel-fixture",
            "flight_performance.cfg",
            """
            [ENGINE_PERFORMANCE]
            fuel_density_table = 6.0, 6.7

            [AIRCRAFT_LOADING]
            fuel_capacity = 120
            passenger_capacity = 4
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                Assert.Single(
                    await source.FindAircraftObservationsAsync(
                        AircraftCanonicalIdentity.FromMsfsTitle(
                            "Multi Fuel Fixture")))
                .DispatchPerformance);

        Assert.Equal(8000, profile.MaximumTakeoffWeightPounds);
        Assert.Null(profile.MaximumFuelWeightPounds);
    }

    [Fact]
    public async Task ZeroFuelCapacityIsPreservedAsKnownZero()
    {
        string root = CreatePackageRoot("Community2024");

        WriteAircraftCfg(
            root,
            "electric-fixture",
            """
            [FLTSIM.0]
            title = "Electric Fixture"
            """);

        WriteAircraftFile(
            root,
            "electric-fixture",
            "flight_performance.cfg",
            """
            [ENGINE_PERFORMANCE]
            fuel_density_table = 6.0

            [AIRCRAFT_LOADING]
            fuel_capacity = 0
            passenger_capacity = 2
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftDispatchPerformanceProfile profile =
            Assert.IsType<AircraftDispatchPerformanceProfile>(
                Assert.Single(
                    await source.FindAircraftObservationsAsync(
                        AircraftCanonicalIdentity.FromMsfsTitle(
                            "Electric Fixture")))
                .DispatchPerformance);

        Assert.Equal(0, profile.MaximumFuelWeightPounds);
    }

    [Fact]
    public async Task ContradictoryConfiguredEmptyAndTakeoffWeightsFailClosed()
    {
        string root = CreatePackageRoot("Community2024");

        WriteAircraftCfg(
            root,
            "contradictory-fixture",
            """
            [FLTSIM.0]
            title = "Contradictory Fixture"
            """);

        WriteAircraftFile(
            root,
            "contradictory-fixture",
            "flight_model.cfg",
            """
            [WEIGHT_AND_BALANCE]
            max_takeoff_weight = 4000
            empty_weight = 5000
            """);

        var source = new MsfsAircraftCfgObservationSource([root]);

        AircraftRegistryObservation observation = Assert.Single(
            await source.FindAircraftObservationsAsync(
                AircraftCanonicalIdentity.FromMsfsTitle(
                    "Contradictory Fixture")));

        Assert.Null(observation.DispatchPerformance);
    }

    [Fact]
    public async Task CfgEnrichmentCombinesWithInstalledIdentityWithoutInventingSeats()
    {
        string root = CreatePackageRoot("Community2024");
        WriteAircraftCfg(
            root,
            "fixture",
            """
            [GENERAL]
            icao_engine_type = "Turboprop/Turboshaft"
            icao_engine_count = 2

            [FLTSIM.0]
            title = "Fixture Twin"
            ui_max_range = 900
            capacity = 8
            """);

        string id = AircraftCanonicalIdentity.FromMsfsTitle("Fixture Twin");
        var source = new MsfsAircraftCfgObservationSource([root]);
        AircraftRegistryObservation cfg = Assert.Single(
            await source.FindAircraftObservationsAsync(id));

        var installed = new AircraftRegistryObservation(
            id,
            "msfs-simconnect",
            "Fixture Twin",
            AircraftDataConfidence.Verified,
            IsInstalled: true,
            DisplayName: "Fixture Twin");

        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve([installed, cfg]);

        Assert.Equal(AircraftInstallationStatus.Installed, result.InstallationStatus);
        Assert.Equal(900, result.CapabilityValues.MaximumRangeNauticalMiles);
        Assert.Equal(2, result.CapabilityValues.EngineCount);
        Assert.Null(result.CapabilityValues.Seats);
        Assert.Contains(AircraftRegistryField.Seats, result.UnresolvedCapabilityFields);
        Assert.Equal(8, result.ReferenceMetadata!.PassengerCapacity);
        Assert.Equal(
            MsfsAircraftCfgObservationSource.ProviderId,
            result.Provenance[AircraftRegistryField.ReferenceMetadata].ProviderId);
    }

    [Fact]
    public void UserCfgUsesInstalledPackagesPathAndOnlyExistingDocumentedRoots()
    {
        string packages = Path.Combine(_tempRoot, "Packages");
        string community = Path.Combine(packages, "Community2024");
        string streamed = Path.Combine(packages, "StreamedPackages");
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(streamed);

        string userCfg = Path.Combine(_tempRoot, "UserCfg.opt");
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(
            userCfg,
            $"InstalledPackagesPath \"{packages}\"{Environment.NewLine}");

        IReadOnlyList<string> roots = MsfsPackageRootLocator.FromUserConfig(userCfg);

        Assert.Equal(
            [Path.GetFullPath(community), Path.GetFullPath(streamed)],
            roots.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void MissingUserCfgReturnsNoPackageRoots()
    {
        Assert.Empty(
            MsfsPackageRootLocator.FromUserConfig(
                Path.Combine(_tempRoot, "missing", "UserCfg.opt")));
    }

    [Fact]
    public void ProductionUserConfigLocatorFindsExistingSteamAndStoreConfigs()
    {
        string roaming = Path.Combine(_tempRoot, "Roaming");
        string local = Path.Combine(_tempRoot, "Local");

        string steam = Path.Combine(
            roaming,
            "Microsoft Flight Simulator 2024",
            "UserCfg.opt");

        string store = Path.Combine(
            local,
            "Packages",
            "Microsoft.Limitless_8wekyb3d8bbwe",
            "LocalCache",
            "UserCfg.opt");

        Directory.CreateDirectory(Path.GetDirectoryName(steam)!);
        Directory.CreateDirectory(Path.GetDirectoryName(store)!);
        File.WriteAllText(steam, "InstalledPackagesPath \"X:\\SteamPackages\"");
        File.WriteAllText(store, "InstalledPackagesPath \"X:\\StorePackages\"");

        IReadOnlyList<string> found = MsfsUserConfigLocator.FindExisting(
            configuredUserConfigPath: null,
            roamingAppData: roaming,
            localAppData: local);

        Assert.Equal(
            new[] { Path.GetFullPath(store), Path.GetFullPath(steam) }
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            found);
    }

    [Fact]
    public void ProductionUserConfigLocatorResolvesPackageRootsWithoutInventingMissingFolders()
    {
        string configured = Path.Combine(_tempRoot, "Configured", "UserCfg.opt");
        string packages = Path.Combine(_tempRoot, "ConfiguredPackages");
        string community = Path.Combine(packages, "Community2024");
        string official = Path.Combine(packages, "Official2024");

        Directory.CreateDirectory(Path.GetDirectoryName(configured)!);
        Directory.CreateDirectory(community);
        Directory.CreateDirectory(official);
        File.WriteAllText(
            configured,
            $"InstalledPackagesPath \"{packages}\"{Environment.NewLine}");

        IReadOnlyList<string> roots = MsfsUserConfigLocator.FindPackageRoots(
            configured,
            roamingAppData: Path.Combine(_tempRoot, "missing-roaming"),
            localAppData: Path.Combine(_tempRoot, "missing-local"));

        Assert.Equal(
            new[] { Path.GetFullPath(community), Path.GetFullPath(official) }
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            roots);
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

    private static void WriteAircraftCfg(
        string root,
        string packageName,
        string content) =>
        WriteAircraftFile(
            root,
            packageName,
            "aircraft.cfg",
            content);

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
