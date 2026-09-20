using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public class AircraftRegistryResolutionTests
{
    [Fact]
    public void HigherConfidenceWinsPerFieldWithoutUnioningCapabilities()
    {
        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            Observation(
                provider: "reference",
                record: "a",
                confidence: AircraftDataConfidence.Reference,
                displayName: "Reference Name",
                capabilities: AircraftCapability.Cargo | AircraftCapability.ShortField,
                payload: 1200),
            Observation(
                provider: "verified",
                record: "b",
                confidence: AircraftDataConfidence.Verified,
                displayName: "Verified Name",
                capabilities: AircraftCapability.Cargo,
                payload: 1000)
        ]);

        Assert.Equal("Verified Name", result.CapabilityValues.DisplayName);
        Assert.Equal(AircraftCapability.Cargo, result.CapabilityValues.Capabilities);
        Assert.Equal(1000, result.CapabilityValues.MaximumPayloadPounds);
        Assert.Equal(
            "verified",
            result.Provenance[AircraftRegistryField.Capabilities].ProviderId);
    }

    [Fact]
    public void FieldsResolveIndependentlyAcrossProviders()
    {
        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            Observation(
                provider: "installed",
                record: "local",
                confidence: AircraftDataConfidence.Verified,
                installed: true,
                displayName: "Installed Aircraft",
                engineCount: 2),
            Observation(
                provider: "reference",
                record: "perf",
                confidence: AircraftDataConfidence.Reference,
                range: 850,
                cruise: 165,
                payload: 1800)
        ]);

        Assert.Equal("Installed Aircraft", result.CapabilityValues.DisplayName);
        Assert.Equal(2, result.CapabilityValues.EngineCount);
        Assert.Equal(850, result.CapabilityValues.MaximumRangeNauticalMiles);
        Assert.Equal("installed", result.Provenance[AircraftRegistryField.EngineCount].ProviderId);
        Assert.Equal("reference", result.Provenance[AircraftRegistryField.MaximumRangeNauticalMiles].ProviderId);
    }

    [Fact]
    public void AnyInstalledObservationMarksAircraftInstalled()
    {
        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            Observation("catalog", "known", AircraftDataConfidence.Reference),
            Observation("installed", "local", AircraftDataConfidence.Inferred, installed: true)
        ]);

        Assert.Equal(AircraftInstallationStatus.Installed, result.InstallationStatus);
    }

    [Fact]
    public void ReferenceOnlyAircraftRemainsKnownOnly()
    {
        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            Observation("catalog", "known", AircraftDataConfidence.Reference)
        ]);

        Assert.Equal(AircraftInstallationStatus.KnownOnly, result.InstallationStatus);
    }

    [Fact]
    public void MissingFieldsRemainExplicitAndDoNotBecomeZerosOrFalse()
    {
        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            Observation(
                provider: "partial",
                record: "one",
                confidence: AircraftDataConfidence.Reference,
                displayName: "Partial Aircraft",
                engineCount: 1)
        ]);

        Assert.False(result.HasCompleteCapabilityProfile);
        Assert.Null(result.CapabilityValues.MaximumPayloadPounds);
        Assert.Null(result.CapabilityValues.IfrCapable);
        Assert.Contains(
            AircraftRegistryField.MaximumPayloadPounds,
            result.UnresolvedCapabilityFields);
        Assert.Contains(
            AircraftRegistryField.IfrCapable,
            result.UnresolvedCapabilityFields);
        Assert.Null(result.TryCreateRegistryRecord());
    }

    [Fact]
    public void CompleteResolutionCreatesExistingRegistryRecordShape()
    {
        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            CompleteObservation(
                "installed",
                "local",
                AircraftDataConfidence.Verified,
                installed: true)
        ]);

        AircraftRegistryRecord record = Assert.IsType<AircraftRegistryRecord>(
            result.TryCreateRegistryRecord());

        Assert.True(record.IsInstalled);
        Assert.Equal("fixture-aircraft", record.AircraftId);
        Assert.Equal(2000, record.Capabilities.MaximumPayloadPounds);
        Assert.NotNull(record.RunwayPerformance);
    }

    [Fact]
    public void EqualConfidenceTieBreakIsStableAcrossInputOrder()
    {
        AircraftRegistryObservation a = CompleteObservation(
            "alpha",
            "two",
            AircraftDataConfidence.Reference,
            displayName: "Alpha");
        AircraftRegistryObservation b = CompleteObservation(
            "bravo",
            "one",
            AircraftDataConfidence.Reference,
            displayName: "Bravo");

        AircraftRegistryResolution first = AircraftRegistryResolver.Resolve([b, a]);
        AircraftRegistryResolution second = AircraftRegistryResolver.Resolve([a, b]);

        Assert.Equal("Alpha", first.CapabilityValues.DisplayName);
        Assert.Equal(first.CapabilityValues, second.CapabilityValues);
        Assert.Equal(
            first.Provenance[AircraftRegistryField.DisplayName],
            second.Provenance[AircraftRegistryField.DisplayName]);
    }

    [Fact]
    public void InstalledEvidenceBreaksEqualConfidenceTieDeterministically()
    {
        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            CompleteObservation(
                "reference",
                "one",
                AircraftDataConfidence.Reference,
                displayName: "Reference",
                installed: false),
            CompleteObservation(
                "local",
                "two",
                AircraftDataConfidence.Reference,
                displayName: "Installed",
                installed: true)
        ]);

        Assert.Equal("Installed", result.CapabilityValues.DisplayName);
        Assert.True(result.Provenance[AircraftRegistryField.DisplayName].InstalledEvidence);
    }

    [Fact]
    public void RunwayPerformanceUsesItsOwnConfidence()
    {
        var inferred = new AircraftRunwayPerformanceProfile(
            3000,
            2800,
            75,
            RunwaySurfaceSupport.Asphalt,
            AircraftDataConfidence.Inferred,
            "local-inference");

        var verified = inferred with
        {
            MinimumTakeoffRunwayFeet = 3200,
            Confidence = AircraftDataConfidence.Verified,
            Source = "verified-reference"
        };

        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            CompleteObservation(
                "provider-a",
                "one",
                AircraftDataConfidence.Verified,
                runwayPerformance: inferred),
            CompleteObservation(
                "provider-b",
                "two",
                AircraftDataConfidence.Reference,
                runwayPerformance: verified)
        ]);

        Assert.Equal(3200, result.RunwayPerformance!.MinimumTakeoffRunwayFeet);
        Assert.Equal(
            AircraftDataConfidence.Verified,
            result.Provenance[AircraftRegistryField.RunwayPerformance].Confidence);
        Assert.Equal(
            "provider-b",
            result.Provenance[AircraftRegistryField.RunwayPerformance].ProviderId);
    }

    [Fact]
    public void DuplicateProviderRecordIdentityIsRejected()
    {
        AircraftRegistryObservation a = Observation(
            "same-provider",
            "same-record",
            AircraftDataConfidence.Reference);

        Assert.Throws<ArgumentException>(
            () => AircraftRegistryResolver.Resolve([a, a with { DisplayName = "Conflict" }]));
    }

    [Fact]
    public void ResolverDoesNotFuzzyMergeDifferentCanonicalAircraft()
    {
        IReadOnlyList<AircraftRegistryResolution> results = AircraftRegistryResolver.ResolveAll(
        [
            CompleteObservation("provider", "a", AircraftDataConfidence.Reference)
                with { CanonicalAircraftId = "aircraft-a" },
            CompleteObservation("provider", "b", AircraftDataConfidence.Reference)
                with { CanonicalAircraftId = "aircraft-b" }
        ]);

        Assert.Equal(2, results.Count);
        Assert.Equal("aircraft-a", results[0].CanonicalAircraftId);
        Assert.Equal("aircraft-b", results[1].CanonicalAircraftId);
    }

    [Fact]
    public async Task CatalogMergesProviderObservationsAndPreservesUnknowns()
    {
        var catalog = new AircraftRegistryCatalogService(
        [
            new StubObservationSource(
            [
                Observation(
                    "installed",
                    "local",
                    AircraftDataConfidence.Verified,
                    installed: true,
                    displayName: "Installed Aircraft")
            ]),
            new StubObservationSource(
            [
                Observation(
                    "reference",
                    "perf",
                    AircraftDataConfidence.Reference,
                    payload: 1200,
                    range: 700)
            ])
        ]);

        AircraftRegistryResolution result = Assert.IsType<AircraftRegistryResolution>(
            await catalog.FindAircraftAsync("fixture-aircraft"));

        Assert.Equal(AircraftInstallationStatus.Installed, result.InstallationStatus);
        Assert.Equal(1200, result.CapabilityValues.MaximumPayloadPounds);
        Assert.Contains(AircraftRegistryField.Access, result.UnresolvedCapabilityFields);
        Assert.Null(result.TryCreateRegistryRecord());
    }

    [Fact]
    public async Task CatalogRejectsProviderReturningDifferentCanonicalIdentity()
    {
        var catalog = new AircraftRegistryCatalogService(
        [
            new StubObservationSource(
            [
                CompleteObservation(
                    "provider",
                    "one",
                    AircraftDataConfidence.Reference)
                    with { CanonicalAircraftId = "different-aircraft" }
            ])
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.FindAircraftAsync("fixture-aircraft"));
    }

    private static AircraftRegistryObservation Observation(
        string provider,
        string record,
        AircraftDataConfidence confidence,
        bool installed = false,
        string? displayName = null,
        AircraftCapability? capabilities = null,
        AircraftAccess? access = null,
        double? payload = null,
        double? range = null,
        double? cruise = null,
        int? seats = null,
        int? engineCount = null,
        bool? ifr = null,
        bool? pressurized = null,
        bool? retractable = null,
        AircraftRunwayPerformanceProfile? runwayPerformance = null) =>
        new(
            "fixture-aircraft",
            provider,
            record,
            confidence,
            installed,
            displayName,
            capabilities,
            access,
            payload,
            range,
            cruise,
            seats,
            engineCount,
            ifr,
            pressurized,
            retractable,
            runwayPerformance);

    private static AircraftRegistryObservation CompleteObservation(
        string provider,
        string record,
        AircraftDataConfidence confidence,
        bool installed = false,
        string displayName = "Fixture Aircraft",
        AircraftRunwayPerformanceProfile? runwayPerformance = null) =>
        Observation(
            provider,
            record,
            confidence,
            installed,
            displayName,
            AircraftCapability.Cargo,
            AircraftAccess.Civilian,
            payload: 2000,
            range: 800,
            cruise: 150,
            seats: 6,
            engineCount: 1,
            ifr: true,
            pressurized: false,
            retractable: false,
            runwayPerformance: runwayPerformance ?? new(
                1800,
                1600,
                50,
                RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete,
                AircraftDataConfidence.Reference,
                "test"));

    private sealed class StubObservationSource(
        IReadOnlyList<AircraftRegistryObservation> observations)
        : IAircraftRegistryObservationSource
    {
        public Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(observations);
    }
}
