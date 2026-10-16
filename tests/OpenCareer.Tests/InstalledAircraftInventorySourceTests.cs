using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class InstalledAircraftInventorySourceTests
{
    [Fact]
    public async Task UnavailableDiscoveryFailsClosedWithoutRegistryReads()
    {
        var registry = new StubRegistry();
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(InstalledAircraftDiscoverySnapshot.Unavailable),
            registry,
            new StubAvailability());

        InstalledAircraftInventorySnapshot result = await source.ReadAsync();

        Assert.Equal(InstalledAircraftInventoryStatus.DiscoveryUnavailable, result.Status);
        Assert.Empty(result.Aircraft);
        Assert.Empty(registry.Requests);
        Assert.Contains("will not guess", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyAvailableDiscoveryReportsNoInstalledAircraft()
    {
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [])),
            new StubRegistry(),
            new StubAvailability());

        InstalledAircraftInventorySnapshot result = await source.ReadAsync();

        Assert.Equal(InstalledAircraftInventoryStatus.NoInstalledAircraft, result.Status);
        Assert.Empty(result.Aircraft);
    }

    [Fact]
    public async Task InventoryDeduplicatesAndOrdersInstalledCanonicalRecords()
    {
        const string alphaId = "msfs-title:Alpha Livery";
        const string zuluId = "msfs-title:Zulu Livery";
        var registry = new StubRegistry(
            (alphaId, Resolution(
                alphaId,
                "Alpha Aircraft",
                new("icao-type:C172", "C172"),
                new(AircraftEngineType.Piston, 1, AircraftEngineMultiplicity.Single),
                new("icao-family:CESSNA/172%20SKYHAWK", "CESSNA", "172 SKYHAWK"),
                new("powerplant-class:piston:single", AircraftEngineType.Piston, AircraftEngineMultiplicity.Single),
                passengerCapacity: 3,
                maximumPayloadPounds: 950,
                maximumRangeNauticalMiles: 640,
                seats: 4,
                ifrCapable: true,
                pressurized: false,
                retractableGear: false)),
            (zuluId, Resolution(zuluId, "Zulu Aircraft", null, null)));
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [
                    Observation(zuluId, "one"),
                    Observation(alphaId, "two"),
                    Observation(alphaId, "three")
                ])),
            registry,
            new StubAvailability());

        InstalledAircraftInventorySnapshot result = await source.ReadAsync();

        Assert.Equal(InstalledAircraftInventoryStatus.Ready, result.Status);
        Assert.Equal(2, result.Aircraft.Count);
        Assert.Equal([alphaId, zuluId], result.Aircraft.Select(static item => item.CanonicalAircraftId));
        Assert.Equal([alphaId, zuluId], registry.Requests.Order(StringComparer.Ordinal));

        InstalledAircraftInventoryItem alpha = result.Aircraft[0];
        Assert.Equal("Alpha Aircraft", alpha.DisplayName);
        Assert.Equal("INSTALLED", alpha.InstallationText);
        Assert.Equal("ICAO type C172", alpha.TypeIdentityText);
        Assert.Equal("CESSNA 172 SKYHAWK", alpha.FamilyIdentityText);
        Assert.Equal("single-engine piston", alpha.PowerplantClassText);
        Assert.Equal("Single piston engine", alpha.PowerplantText);
        Assert.Equal("Maximum payload 950 lb", alpha.MaximumPayloadText);
        Assert.Equal("Maximum range 640 nm", alpha.MaximumRangeText);
        Assert.Equal("4 seat(s)", alpha.SeatsText);
        Assert.Equal(
            "Passenger capacity 3 (cabin value; crew and total seats not inferred)",
            alpha.PassengerCapacityText);
        Assert.Equal("IFR capable", alpha.IfrCapabilityText);
        Assert.Equal("Not pressurized", alpha.PressurizationText);
        Assert.Equal("Fixed/non-retractable gear", alpha.LandingGearText);
        Assert.Contains("payload — test-registry (Verified)", alpha.CapabilityEvidenceText, StringComparison.Ordinal);
        Assert.Contains("IFR — test-registry (Verified)", alpha.CapabilityEvidenceText, StringComparison.Ordinal);
        Assert.Contains("passenger capacity — test-registry (Verified)", alpha.CapabilityEvidenceText, StringComparison.Ordinal);
        Assert.Contains("access unknown", alpha.AccessText, StringComparison.OrdinalIgnoreCase);

        InstalledAircraftInventoryItem zulu = result.Aircraft[1];
        Assert.Equal("ICAO type unknown", zulu.TypeIdentityText);
        Assert.Equal("ICAO family unknown", zulu.FamilyIdentityText);
        Assert.Equal("Powerplant class unknown", zulu.PowerplantClassText);
        Assert.Equal("Powerplant unknown", zulu.PowerplantText);
        Assert.Equal("Maximum payload unknown", zulu.MaximumPayloadText);
        Assert.Equal("Passenger capacity unknown", zulu.PassengerCapacityText);
        Assert.Equal("IFR capability unknown", zulu.IfrCapabilityText);
    }

    [Fact]
    public async Task MissingRegistryMetadataUsesExactDiscoveredIdentityAndExplicitUnknowns()
    {
        const string id = "msfs-title:Exact Installed Title";
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation(id, "one")])),
            new StubRegistry(),
            new StubAvailability());

        InstalledAircraftInventoryItem item = Assert.Single(
            (await source.ReadAsync()).Aircraft);

        Assert.Equal("Exact Installed Title", item.DisplayName);
        Assert.Equal("Registry metadata unavailable", item.CapabilityText);
        Assert.Equal("ICAO type unknown", item.TypeIdentityText);
        Assert.Equal("ICAO family unknown", item.FamilyIdentityText);
        Assert.Equal("Powerplant class unknown", item.PowerplantClassText);
        Assert.Equal("Powerplant unknown", item.PowerplantText);
        Assert.Equal("Maximum payload unknown", item.MaximumPayloadText);
        Assert.Equal("Maximum range unknown", item.MaximumRangeText);
        Assert.Equal("Seats unknown", item.SeatsText);
        Assert.Equal("Passenger capacity unknown", item.PassengerCapacityText);
        Assert.Equal("IFR capability unknown", item.IfrCapabilityText);
        Assert.Equal("Pressurization unknown", item.PressurizationText);
        Assert.Equal("Landing gear configuration unknown", item.LandingGearText);
        Assert.Equal("Capability evidence sources unavailable", item.CapabilityEvidenceText);
        Assert.Equal("Fleet status not tracked", item.FleetStatusText);
        Assert.Equal("No operational availability record exists", item.ReservationText);
    }

    [Theory]
    [InlineData(AircraftAvailabilityStatus.Available, null, "AVAILABLE", "No reservation recorded")]
    [InlineData(AircraftAvailabilityStatus.Unavailable, "job:alpha", "RESERVED", "Authoritative Fleet reservation recorded")]
    [InlineData(AircraftAvailabilityStatus.Unavailable, null, "UNAVAILABLE", "No reservation identity recorded")]
    public async Task FleetAvailabilityIsProjectedWithoutInferringAccess(
        AircraftAvailabilityStatus status,
        string? reservationId,
        string expectedStatus,
        string expectedReservation)
    {
        const string id = "msfs-title:Fleet Aircraft";
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation(id, "one")])),
            new StubRegistry(),
            new StubAvailability(new(id, status, reservationId)));

        InstalledAircraftInventoryItem item = Assert.Single(
            (await source.ReadAsync()).Aircraft);

        Assert.Equal(expectedStatus, item.FleetStatusText);
        Assert.Equal(expectedReservation, item.ReservationText);
        Assert.Contains("access unknown", item.AccessText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MismatchedAvailabilityIdentityIsRejected()
    {
        const string id = "msfs-title:Expected";
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation(id, "one")])),
            new StubRegistry(),
            new StubAvailability(new(
                "msfs-title:Different",
                AircraftAvailabilityStatus.Available)));

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync());
    }

    [Fact]
    public async Task CapabilityValueWithoutFieldProvenanceFailsClosedAsUnknown()
    {
        const string id = "msfs-title:Unproven";
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation(id, "one")])),
            new StubRegistry((id, Resolution(
                id,
                "Unproven",
                null,
                null,
                passengerCapacity: 5,
                maximumPayloadPounds: 1200,
                maximumRangeNauticalMiles: 800,
                seats: 6,
                includeCapabilityProvenance: false))),
            new StubAvailability());

        InstalledAircraftInventoryItem item = Assert.Single(
            (await source.ReadAsync()).Aircraft);

        Assert.Equal("Maximum payload unknown", item.MaximumPayloadText);
        Assert.Equal("Maximum range unknown", item.MaximumRangeText);
        Assert.Equal("Seats unknown", item.SeatsText);
        Assert.Equal("Passenger capacity unknown", item.PassengerCapacityText);
        Assert.Equal("Capability evidence sources unavailable", item.CapabilityEvidenceText);
    }

    [Fact]
    public async Task MismatchedRegistryIdentityIsRejected()
    {
        const string id = "msfs-title:Expected";
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation(id, "one")])),
            new StubRegistry((id, Resolution(
                "msfs-title:Different",
                "Different",
                null,
                null))),
            new StubAvailability());

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync());
    }

    [Fact]
    public async Task NonInstalledDiscoveryObservationIsRejected()
    {
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation("msfs-title:Not Installed", "one") with { IsInstalled = false }])),
            new StubRegistry(),
            new StubAvailability());

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync());
    }

    private static AircraftRegistryObservation Observation(
        string aircraftId,
        string recordId) =>
        new(
            aircraftId,
            "test-discovery",
            recordId,
            AircraftDataConfidence.Verified,
            IsInstalled: true);

    private static AircraftRegistryResolution Resolution(
        string aircraftId,
        string displayName,
        AircraftTypeIdentity? type,
        AircraftPowerplantClassification? powerplant,
        AircraftFamilyIdentity? family = null,
        AircraftPowerplantClassIdentity? powerplantClass = null,
        int? passengerCapacity = null,
        double? maximumPayloadPounds = null,
        double? maximumRangeNauticalMiles = null,
        int? seats = null,
        bool? ifrCapable = null,
        bool? pressurized = null,
        bool? retractableGear = null,
        bool includeCapabilityProvenance = true)
    {
        var provenance =
            new Dictionary<AircraftRegistryField, AircraftRegistryFieldProvenance>();

        Add(AircraftRegistryField.MaximumPayloadPounds, maximumPayloadPounds is not null);
        Add(AircraftRegistryField.MaximumRangeNauticalMiles, maximumRangeNauticalMiles is not null);
        Add(AircraftRegistryField.Seats, seats is not null);
        Add(AircraftRegistryField.IfrCapable, ifrCapable is not null);
        Add(AircraftRegistryField.Pressurized, pressurized is not null);
        Add(AircraftRegistryField.RetractableGear, retractableGear is not null);
        Add(AircraftRegistryField.ReferenceMetadata, passengerCapacity is not null);

        var resolution = new AircraftRegistryResolution(
            aircraftId,
            AircraftInstallationStatus.Installed,
            new(
                displayName,
                null,
                null,
                maximumPayloadPounds,
                maximumRangeNauticalMiles,
                null,
                seats,
                powerplant?.EngineCount,
                ifrCapable,
                pressurized,
                retractableGear),
            null,
            passengerCapacity is null
                ? null
                : new AircraftReferenceMetadata(PassengerCapacity: passengerCapacity),
            type,
            powerplant,
            null,
            [
                AircraftRegistryField.Capabilities,
                AircraftRegistryField.Access
            ],
            provenance);
        return resolution with
        {
            FamilyIdentity = family,
            PowerplantClassIdentity = powerplantClass
        };

        void Add(AircraftRegistryField field, bool hasValue)
        {
            if (!includeCapabilityProvenance || !hasValue)
                return;

            provenance.Add(
                field,
                new(
                    field,
                    "test-registry",
                    "fixture",
                    AircraftDataConfidence.Verified,
                    InstalledEvidence: true));
        }
    }

    private sealed class StubDiscovery(InstalledAircraftDiscoverySnapshot current)
        : IInstalledAircraftDiscoverySource
    {
        public InstalledAircraftDiscoverySnapshot Current { get; } = current;
    }

    private sealed class StubRegistry(
        params (string Id, AircraftRegistryResolution Resolution)[] resolutions)
        : IAircraftRegistrySource
    {
        private readonly Dictionary<string, AircraftRegistryResolution> _resolutions =
            resolutions.ToDictionary(
                static item => item.Id,
                static item => item.Resolution,
                StringComparer.OrdinalIgnoreCase);

        public List<string> Requests { get; } = [];

        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(aircraftId);
            _resolutions.TryGetValue(aircraftId, out AircraftRegistryResolution? resolution);
            return Task.FromResult(resolution);
        }
    }

    private sealed class StubAvailability(AircraftAvailabilityState? current = null)
        : IAircraftAvailabilityStore
    {
        public Task<AircraftAvailabilityState?> FindAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(current);

        public Task SetAsync(
            AircraftAvailabilityState state,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Inventory projection must not write fleet availability.");
    }
}
