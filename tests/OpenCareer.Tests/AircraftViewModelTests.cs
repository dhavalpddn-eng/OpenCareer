using OpenCareer.App.ViewModels;
using OpenCareer.Application.Planning;

namespace OpenCareer.Tests;

public sealed class AircraftViewModelTests
{
    [Fact]
    public async Task RefreshProjectsReadOnlyInventorySnapshot()
    {
        var source = new StubInventorySource(new(
            InstalledAircraftInventoryStatus.Ready,
            "1 installed aircraft record(s) reported by current discovery.",
            [
                new(
                    "msfs-title:Cessna 172 Skyhawk",
                    "Cessna 172 Skyhawk",
                    "INSTALLED",
                    "ICAO type C172",
                    "CESSNA 172 SKYHAWK",
                    "single-engine piston",
                    "Single piston engine",
                    "Capability profile available",
                    "AVAILABLE",
                    "No reservation recorded",
                    "Career access unknown",
                    MaximumPayloadText: "Maximum payload 950 lb",
                    MaximumRangeText: "Maximum range 640 nm",
                    SeatsText: "4 seat(s)",
                    TypicalCruiseText: "Typical cruise 122.5 kt",
                    IfrCapabilityText: "IFR capable",
                    PressurizationText: "Not pressurized",
                    LandingGearText: "Fixed/non-retractable gear",
                    CapabilityEvidenceText: "Evidence: payload — cfg (Verified)",
                    PassengerCapacityText: "Passenger capacity 3 (cabin value; crew and total seats not inferred)",
                    ObjectClassText: "Aircraft class Airplane",
                    CertifiedCeilingText: "Certified ceiling 14000 ft",
                    OperatingEmptyWeightText: "Operating empty weight 1680 lb",
                    ConfiguredEmptyWeightText: "Configured empty weight 1650 lb",
                    MaximumTakeoffWeightText: "Maximum takeoff weight 2550 lb",
                    MaximumLandingWeightText: "Maximum landing weight 2450 lb",
                    MaximumZeroFuelWeightText: "Maximum zero-fuel weight 2200 lb",
                    MaximumFuelWeightText: "Maximum fuel weight 318 lb",
                    FuelCapacityText: "Fuel capacity 53 gal")
            ]));
        var viewModel = new AircraftViewModel(source);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasAircraft);
        Assert.True(viewModel.CanRefresh);
        Assert.False(viewModel.IsRefreshing);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal("Cessna 172 Skyhawk", Assert.Single(viewModel.Aircraft).DisplayName);
        Assert.Equal("CESSNA 172 SKYHAWK", Assert.Single(viewModel.Aircraft).FamilyIdentityText);
        Assert.Equal("single-engine piston", Assert.Single(viewModel.Aircraft).PowerplantClassText);
        Assert.Equal("AVAILABLE", Assert.Single(viewModel.Aircraft).FleetStatusText);
        Assert.Equal("Career access unknown", Assert.Single(viewModel.Aircraft).AccessText);
        Assert.Equal("Maximum payload 950 lb", Assert.Single(viewModel.Aircraft).MaximumPayloadText);
        Assert.Equal("Typical cruise 122.5 kt", Assert.Single(viewModel.Aircraft).TypicalCruiseText);
        Assert.Equal("IFR capable", Assert.Single(viewModel.Aircraft).IfrCapabilityText);
        Assert.Equal("Aircraft class Airplane", Assert.Single(viewModel.Aircraft).ObjectClassText);
        Assert.Equal("Certified ceiling 14000 ft", Assert.Single(viewModel.Aircraft).CertifiedCeilingText);
        Assert.Equal("Operating empty weight 1680 lb", Assert.Single(viewModel.Aircraft).OperatingEmptyWeightText);
        Assert.Equal("Configured empty weight 1650 lb", Assert.Single(viewModel.Aircraft).ConfiguredEmptyWeightText);
        Assert.Equal("Maximum takeoff weight 2550 lb", Assert.Single(viewModel.Aircraft).MaximumTakeoffWeightText);
        Assert.Equal("Maximum landing weight 2450 lb", Assert.Single(viewModel.Aircraft).MaximumLandingWeightText);
        Assert.Equal("Maximum zero-fuel weight 2200 lb", Assert.Single(viewModel.Aircraft).MaximumZeroFuelWeightText);
        Assert.Equal("Maximum fuel weight 318 lb", Assert.Single(viewModel.Aircraft).MaximumFuelWeightText);
        Assert.Equal("Fuel capacity 53 gal", Assert.Single(viewModel.Aircraft).FuelCapacityText);
        Assert.Equal(
            "Passenger capacity 3 (cabin value; crew and total seats not inferred)",
            Assert.Single(viewModel.Aircraft).PassengerCapacityText);
        Assert.Equal(
            "Evidence: payload — cfg (Verified)",
            Assert.Single(viewModel.Aircraft).CapabilityEvidenceText);
        Assert.Equal("Refresh Aircraft", viewModel.RefreshButtonText);
    }

    [Fact]
    public async Task UnavailableSnapshotClearsPreviouslyProjectedAircraft()
    {
        var source = new StubInventorySource(new(
            InstalledAircraftInventoryStatus.Ready,
            "ready",
            [new("id", "name", "INSTALLED", "type", "family", "class", "engine", "capability", "fleet", "reservation", "access")]));
        var viewModel = new AircraftViewModel(source);
        await viewModel.RefreshAsync();

        source.Current = new(
            InstalledAircraftInventoryStatus.DiscoveryUnavailable,
            "Discovery unavailable.",
            []);
        await viewModel.RefreshAsync();

        Assert.False(viewModel.HasAircraft);
        Assert.Empty(viewModel.Aircraft);
        Assert.Equal("Discovery unavailable.", viewModel.StatusText);
        Assert.Equal(2, source.ReadCount);
    }

    [Fact]
    public async Task RefreshFailureClearsStaleAircraftAndSuccessfulRetryRestoresInventory()
    {
        var source = new StubInventorySource(new(
            InstalledAircraftInventoryStatus.Ready,
            "ready",
            [
                new(
                    "id",
                    "name",
                    "INSTALLED",
                    "type",
                    "family",
                    "class",
                    "engine",
                    "capability",
                    "fleet",
                    "reservation",
                    "access")
            ]));
        var viewModel = new AircraftViewModel(source);
        await viewModel.RefreshAsync();

        viewModel.ReportRefreshFailure(
            new IOException("C:\\Users\\pilot\\AppData\\Local\\Packages\\sensitive-path"));

        Assert.False(viewModel.HasAircraft);
        Assert.Empty(viewModel.Aircraft);
        Assert.DoesNotContain("sensitive-path", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("Refresh Aircraft", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);

        source.Current = new(
            InstalledAircraftInventoryStatus.Ready,
            "authoritative retry",
            [
                new(
                    "updated-id",
                    "updated name",
                    "INSTALLED",
                    "updated type",
                    "updated family",
                    "updated class",
                    "updated engine",
                    "updated capability",
                    "updated fleet",
                    "updated reservation",
                    "updated access")
            ]);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasAircraft);
        Assert.Equal("updated name", Assert.Single(viewModel.Aircraft).DisplayName);
        Assert.Equal("authoritative retry", viewModel.StatusText);
        Assert.Equal(2, source.ReadCount);
    }

    private sealed class StubInventorySource(InstalledAircraftInventorySnapshot current)
        : IInstalledAircraftInventorySource
    {
        public InstalledAircraftInventorySnapshot Current { get; set; } = current;
        public int ReadCount { get; private set; }

        public Task<InstalledAircraftInventorySnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(Current);
        }
    }
}
