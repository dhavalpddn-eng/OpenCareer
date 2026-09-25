using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public sealed class InstalledAircraftDiscoveryFallbackTests
{
    [Fact]
    public async Task PackageDiscoveryPublishesInstalledAircraftFromConfiguredRoot()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        string aircraftDirectory =
            Path.Combine(
                root,
                "Community",
                "fixture-aircraft");

        Directory.CreateDirectory(
            aircraftDirectory);

        string packageDirectory =
            Path.Combine(
                root,
                "Community",
                "fixture-aircraft");

        File.WriteAllText(
            Path.Combine(
                packageDirectory,
                "manifest.json"),
            "{}");
        File.WriteAllText(
            Path.Combine(
                packageDirectory,
                "layout.json"),
            "{}");

        string cfg =
            """
            [GENERAL]
            icao_type_designator = C172
            icao_manufacturer = Cessna
            icao_model = 172

            [FLTSIM.0]
            title = Cessna 172 Fixture
            isUserSelectable = 1
            atc_parking_types = RAMP

            [FLTSIM.1]
            title = Cessna 172 Fixture Blue
            isUserSelectable = 1
            atc_parking_types = RAMP
            """;

        File.WriteAllText(
            Path.Combine(
                aircraftDirectory,
                "aircraft.cfg"),
            cfg);

        try
        {
            var source =
                new MsfsPackageInstalledAircraftDiscoverySource(
                    [Path.Combine(root, "Community")]);

            await source.InitializeAsync();

            InstalledAircraftDiscoverySnapshot snapshot =
                source.Current;

            Assert.Equal(
                InstalledAircraftDiscoveryAvailability.Available,
                snapshot.Availability);

            Assert.Equal(
                2,
                snapshot.Observations.Count);

            Assert.All(
                snapshot.Observations,
                static observation =>
                {
                    Assert.True(
                        observation.IsInstalled);
                    Assert.Equal(
                        MsfsPackageInstalledAircraftDiscoverySource.ProviderId,
                        observation.ProviderId);
                    Assert.StartsWith(
                        AircraftCanonicalIdentity.MsfsTitlePrefix,
                        observation.CanonicalAircraftId,
                        StringComparison.Ordinal);
                    Assert.Equal(
                        AircraftAccess.Civilian,
                        observation.Access);
                });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageDiscoveryPublishesMilitaryAccessFromMilitaryParkingEvidence()
    {
        string root =
            CreateInstalledPackage(
                """
                [FLTSIM.0]
                title = Military Fixture
                isUserSelectable = 1
                isAirTraffic = 0
                atc_parking_types = MIL_COMBAT
                """);

        try
        {
            var source =
                new MsfsPackageInstalledAircraftDiscoverySource(
                    [Path.Combine(root, "Community")]);

            await source.InitializeAsync();

            AircraftRegistryObservation observation =
                Assert.Single(
                    source.Current.Observations);

            Assert.Equal(
                AircraftAccess.Military,
                observation.Access);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackageDiscoveryLeavesAmbiguousAccessUnknown()
    {
        string root =
            CreateInstalledPackage(
                """
                [FLTSIM.0]
                title = Ambiguous Fixture
                """);

        try
        {
            var source =
                new MsfsPackageInstalledAircraftDiscoverySource(
                    [Path.Combine(root, "Community")]);

            await source.InitializeAsync();

            AircraftRegistryObservation observation =
                Assert.Single(
                    source.Current.Observations);

            Assert.Null(
                observation.Access);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalDiscoveryRejectsNonSelectableAndUnpackagedCfgFiles()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        string packageRoot =
            Path.Combine(
                root,
                "Community");

        string unpackaged =
            Path.Combine(
                packageRoot,
                "loose-aircraft");

        string aiPackage =
            Path.Combine(
                packageRoot,
                "ai-aircraft");

        Directory.CreateDirectory(
            unpackaged);
        Directory.CreateDirectory(
            aiPackage);

        File.WriteAllText(
            Path.Combine(
                unpackaged,
                "aircraft.cfg"),
            """
            [FLTSIM.0]
            title = Loose Aircraft
            isUserSelectable = 1
            """);

        File.WriteAllText(
            Path.Combine(
                aiPackage,
                "manifest.json"),
            "{}");
        File.WriteAllText(
            Path.Combine(
                aiPackage,
                "layout.json"),
            "{}");
        File.WriteAllText(
            Path.Combine(
                aiPackage,
                "aircraft.cfg"),
            """
            [FLTSIM.0]
            title = AI Only Aircraft
            isUserSelectable = 1
            isAirTraffic = 1

            [FLTSIM.1]
            title = Nonselectable Aircraft
            isUserSelectable = 0
            isAirTraffic = 0
            """);

        try
        {
            var source =
                new MsfsPackageInstalledAircraftDiscoverySource(
                    [packageRoot]);

            await source.InitializeAsync();

            Assert.Equal(
                InstalledAircraftDiscoveryAvailability.Available,
                source.Current.Availability);
            Assert.Empty(
                source.Current.Observations);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompositeUsesLocalCatalogWhenLiveCatalogIsUnavailable()
    {
        AircraftRegistryObservation local =
            Observation(
                "Local Aircraft",
                "local");

        var composite =
            new CompositeInstalledAircraftDiscoverySource(
                new StubDiscovery(
                    InstalledAircraftDiscoverySnapshot.Unavailable),
                new StubDiscovery(
                    new(
                        InstalledAircraftDiscoveryAvailability.Available,
                        [local])));

        InstalledAircraftDiscoverySnapshot current =
            composite.Current;

        Assert.Equal(
            InstalledAircraftDiscoveryAvailability.Available,
            current.Availability);

        AircraftRegistryObservation aircraft =
            Assert.Single(
                current.Observations);

        Assert.Equal(
            local,
            aircraft);
    }

    [Fact]
    public void CompositeMergesLiveAndLocalEvidenceWithoutDuplicatingProviderRecord()
    {
        AircraftRegistryObservation live =
            Observation(
                "Live Aircraft",
                "live");

        AircraftRegistryObservation local =
            Observation(
                "Local Aircraft",
                "local");

        var composite =
            new CompositeInstalledAircraftDiscoverySource(
                new StubDiscovery(
                    new(
                        InstalledAircraftDiscoveryAvailability.Available,
                        [live, live])),
                new StubDiscovery(
                    new(
                        InstalledAircraftDiscoveryAvailability.Available,
                        [local])));

        InstalledAircraftDiscoverySnapshot current =
            composite.Current;

        Assert.Equal(
            2,
            current.Observations.Count);

        Assert.Contains(
            live,
            current.Observations);

        Assert.Contains(
            local,
            current.Observations);
    }

    private static AircraftRegistryObservation Observation(
        string title,
        string providerRecordId) =>
        new(
            AircraftCanonicalIdentity.FromMsfsTitle(title),
            "fixture",
            providerRecordId,
            AircraftDataConfidence.Reference,
            IsInstalled:
                true,
            DisplayName:
                title);

    private static string CreateInstalledPackage(
        string aircraftCfg)
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        string package =
            Path.Combine(
                root,
                "Community",
                "fixture-aircraft");

        Directory.CreateDirectory(package);
        File.WriteAllText(
            Path.Combine(package, "manifest.json"),
            "{}");
        File.WriteAllText(
            Path.Combine(package, "layout.json"),
            "{}");
        File.WriteAllText(
            Path.Combine(package, "aircraft.cfg"),
            aircraftCfg);

        return root;
    }

    private sealed class StubDiscovery(
        InstalledAircraftDiscoverySnapshot current)
        : IInstalledAircraftDiscoverySource
    {
        public InstalledAircraftDiscoverySnapshot Current { get; } =
            current;
    }
}
