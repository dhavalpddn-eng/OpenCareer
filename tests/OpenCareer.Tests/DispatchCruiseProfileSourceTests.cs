using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class DispatchCruiseProfileSourceTests
{
    [Fact]
    public async Task ProjectsOnlyCompleteRegistryProvenProfilesInStableOrder()
    {
        AircraftRegistryResolution resolution = Resolution(
            AircraftInstallationStatus.Installed,
            DispatchProfile(
                [
                    Profile(
                        2,
                        "Economy",
                        1,
                        Grid([4000, 7000], 205),
                        Grid([5000, 8000], 38)),
                    Profile(0, null, 0, Grid(190), Grid(32)),
                    Profile(1, "Missing fuel table", 0, Grid(195), null),
                    Profile(3, "Missing density", 3, Grid(210), Grid(42))
                ],
                [new(0, 6), new(1, 6.7)]));
        var source = new DispatchCruiseProfileSource(
            new StubRegistry(resolution));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        Assert.Equal(DispatchCruiseProfileCatalogStatus.Ready, result.Status);
        Assert.Equal([0, 2], result.Profiles.Select(static profile => profile.ProfileIndex));
        Assert.Equal([0, 1], result.Profiles.Select(static profile => profile.FuelTypeIndex));
        Assert.Equal("Profile 0 · fuel type 0", result.Profiles[0].DisplayName);
        Assert.Equal("Economy · fuel type 1", result.Profiles[1].DisplayName);
        Assert.Contains("Verified", result.Profiles[0].EvidenceDetail, StringComparison.Ordinal);
        Assert.Equal(5000, result.Profiles[1].Envelope.MinimumWeightPounds);
        Assert.Equal(7000, result.Profiles[1].Envelope.MaximumWeightPounds);
        Assert.Contains("common envelope", result.Profiles[1].EvidenceDetail, StringComparison.Ordinal);
        Assert.Contains("2 incomplete", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvenCertifiedCeilingCapsSelectableAltitudeEnvelope()
    {
        AircraftRegistryResolution resolution = Resolution(
            AircraftInstallationStatus.Installed,
            DispatchProfile(
                [Profile(
                    0,
                    "Economy",
                    0,
                    GridAtAltitudes([10000, 20000], 190),
                    GridAtAltitudes([10000, 20000], 32))],
                [new(0, 6)]),
            certifiedCeilingFeet: 18000);
        var source = new DispatchCruiseProfileSource(new StubRegistry(resolution));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        DispatchCruiseProfileOption profile = Assert.Single(result.Profiles);
        Assert.Equal(18000, profile.Envelope.MaximumPressureAltitudeFeet);
        Assert.Contains("certified ceiling", profile.EvidenceDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("18000 ft", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnprovenCertifiedCeilingDoesNotChangeProfileEnvelope()
    {
        AircraftRegistryResolution resolution = Resolution(
            AircraftInstallationStatus.Installed,
            DispatchProfile(
                [Profile(
                    0,
                    "Economy",
                    0,
                    GridAtAltitudes([10000, 20000], 190),
                    GridAtAltitudes([10000, 20000], 32))],
                [new(0, 6)])) with
        {
            CapabilityValues = new(
                null, null, null, null, null, null, null, null, null, null, null,
                CertifiedCeilingFeet: 18000)
        };
        var source = new DispatchCruiseProfileSource(new StubRegistry(resolution));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        DispatchCruiseProfileOption profile = Assert.Single(result.Profiles);
        Assert.Equal(20000, profile.Envelope.MaximumPressureAltitudeFeet);
        Assert.DoesNotContain("certified ceiling", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CertifiedCeilingBelowProfileMinimumExcludesProfile()
    {
        AircraftRegistryResolution resolution = Resolution(
            AircraftInstallationStatus.Installed,
            DispatchProfile(
                [Profile(
                    0,
                    "High altitude only",
                    0,
                    GridAtAltitudes([20000, 30000], 190),
                    GridAtAltitudes([20000, 30000], 32))],
                [new(0, 6)]),
            certifiedCeilingFeet: 18000);
        var source = new DispatchCruiseProfileSource(new StubRegistry(resolution));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        Assert.Equal(
            DispatchCruiseProfileCatalogStatus.NoUsableCruiseProfiles,
            result.Status);
        Assert.Empty(result.Profiles);
    }

    [Fact]
    public async Task MissingDispatchPerformanceIsExplicitlyUnavailable()
    {
        var source = new DispatchCruiseProfileSource(
            new StubRegistry(Resolution(AircraftInstallationStatus.Installed, null)));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        Assert.Equal(
            DispatchCruiseProfileCatalogStatus.DispatchPerformanceUnavailable,
            result.Status);
        Assert.Empty(result.Profiles);
        Assert.Contains("unavailable", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncompleteProfilesCannotBecomeSelectable()
    {
        AircraftRegistryResolution resolution = Resolution(
            AircraftInstallationStatus.Installed,
            DispatchProfile(
                [Profile(0, "Partial", 0, Grid(190), null)],
                [new(0, 6)]));
        var source = new DispatchCruiseProfileSource(
            new StubRegistry(resolution));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        Assert.Equal(
            DispatchCruiseProfileCatalogStatus.NoUsableCruiseProfiles,
            result.Status);
        Assert.Empty(result.Profiles);
    }

    [Fact]
    public async Task ProfilesWithoutACommonTableEnvelopeCannotBecomeSelectable()
    {
        AircraftRegistryResolution resolution = Resolution(
            AircraftInstallationStatus.Installed,
            DispatchProfile(
                [Profile(
                    0,
                    "Disjoint",
                    0,
                    Grid([4000], 190),
                    Grid([5000], 32))],
                [new(0, 6)]));
        var source = new DispatchCruiseProfileSource(
            new StubRegistry(resolution));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        Assert.Equal(
            DispatchCruiseProfileCatalogStatus.NoUsableCruiseProfiles,
            result.Status);
        Assert.Empty(result.Profiles);
    }

    [Fact]
    public async Task KnownOnlyAircraftCannotSupplyDispatchChoices()
    {
        var source = new DispatchCruiseProfileSource(
            new StubRegistry(Resolution(
                AircraftInstallationStatus.KnownOnly,
                DispatchProfile(
                    [Profile(0, "Economy", 0, Grid(190), Grid(32))],
                    [new(0, 6)]))));

        DispatchCruiseProfileCatalog result = await source.ReadAsync(AircraftId);

        Assert.Equal(
            DispatchCruiseProfileCatalogStatus.AircraftNotInstalled,
            result.Status);
        Assert.Empty(result.Profiles);
    }

    [Fact]
    public async Task MismatchedRegistryIdentityIsRejected()
    {
        AircraftRegistryResolution resolution = Resolution(
            AircraftInstallationStatus.Installed,
            null) with
        {
            CanonicalAircraftId = "msfs-title:Different"
        };
        var source = new DispatchCruiseProfileSource(
            new StubRegistry(resolution));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => source.ReadAsync(AircraftId));
    }

    private const string AircraftId = "msfs-title:Fixture";

    private static AircraftRegistryResolution Resolution(
        AircraftInstallationStatus installation,
        AircraftDispatchPerformanceProfile? dispatch,
        double? certifiedCeilingFeet = null) =>
        new(
            AircraftId,
            installation,
            new(
                null, null, null, null, null, null, null, null, null, null, null,
                CertifiedCeilingFeet: certifiedCeilingFeet),
            null,
            null,
            null,
            null,
            dispatch,
            [],
            certifiedCeilingFeet is null
                ? new Dictionary<AircraftRegistryField, AircraftRegistryFieldProvenance>()
                : new Dictionary<AircraftRegistryField, AircraftRegistryFieldProvenance>
                {
                    [AircraftRegistryField.CertifiedCeilingFeet] = new(
                        AircraftRegistryField.CertifiedCeilingFeet,
                        "test-source",
                        "fixture",
                        AircraftDataConfidence.Verified,
                        InstalledEvidence: true)
                });

    private static AircraftDispatchPerformanceProfile DispatchProfile(
        IReadOnlyList<AircraftCruisePerformanceProfile> profiles,
        IReadOnlyList<AircraftFuelDensity> densities) =>
        new(
            OperatingEmptyWeightPounds: 4000,
            MaximumTakeoffWeightPounds: 7000,
            MaximumFuelWeightPounds: 1000,
            PayloadRangeEnvelope: null,
            Confidence: AircraftDataConfidence.Verified,
            Source: "test flight-performance.cfg",
            ConditionedPerformance: new(null, null, null, null, profiles),
            FuelCapacityGallons: 100,
            FuelDensities: densities);

    private static AircraftCruisePerformanceProfile Profile(
        int index,
        string? name,
        int fuelType,
        AircraftPerformanceGrid3D? tas,
        AircraftPerformanceGrid3D? fuel) =>
        new(index, name, fuelType, null, tas, fuel);

    private static AircraftPerformanceGrid3D Grid(double value) =>
        Grid([5000], value);

    private static AircraftPerformanceGrid3D Grid(
        IReadOnlyList<double> weights,
        double value) =>
        new(
            weights,
            [0],
            [10000],
            weights.Select(_ => value),
            AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius);

    private static AircraftPerformanceGrid3D GridAtAltitudes(
        IReadOnlyList<double> altitudes,
        double value) =>
        new(
            [5000],
            [0],
            altitudes,
            altitudes.Select(_ => value),
            AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius);

    private sealed class StubRegistry(AircraftRegistryResolution? resolution)
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(resolution);
    }
}
