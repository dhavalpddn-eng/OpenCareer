using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public class AircraftRunwayFeasibilityTests
{
    private static AircraftRegistryRecord Aircraft(
        AircraftAccess access = AircraftAccess.Civilian,
        AircraftRunwayPerformanceProfile? performance = null) =>
        new(
            new AircraftCapabilityProfile(
                "fixture-aircraft",
                "Fixture Aircraft",
                AircraftCapability.Cargo,
                access,
                2000,
                800,
                150,
                6,
                1,
                true,
                false,
                false),
            IsInstalled: true,
            performance ?? new(
                MinimumTakeoffRunwayFeet: 1800,
                MinimumLandingRunwayFeet: 1600,
                MinimumRunwayWidthFeet: 50,
                SupportedSurfaces: RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete,
                Confidence: AircraftDataConfidence.Verified,
                Source: "test-fixture"));

    private static AirportRecord Airport(string icao, params RunwayRecord[] runways) =>
        new(icao, $"{icao} Fixture", runways);

    private static RunwayRecord Runway(
        string identifier,
        double? length = 5000,
        double? width = 100,
        RunwaySurface surface = RunwaySurface.Asphalt,
        bool closed = false) =>
        new(identifier, length, width, surface, closed);

    [Fact]
    public void CompatibleOriginAndDestinationAreFeasible()
    {
        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", Runway("01", 3000, 75)),
            Airport("KBBB", Runway("19", 2800, 75, RunwaySurface.Concrete)));

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Equal("01", result.OriginRunwayIdentifier);
        Assert.Equal("19", result.DestinationRunwayIdentifier);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void TooShortOriginIsRejectedWithExplicitReason()
    {
        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", Runway("01", 1700, 75)),
            Airport("KBBB", Runway("19")));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);
        Assert.Equal(1800, issue.RequiredFeet);
        Assert.Equal(1700, issue.AvailableFeet);
    }

    [Fact]
    public void TooShortDestinationUsesLandingRequirement()
    {
        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", Runway("01")),
            Airport("KBBB", Runway("19", 1500, 75)));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Destination
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);
        Assert.Equal(1600, issue.RequiredFeet);
    }

    [Fact]
    public void UnsupportedSurfaceIsRejected()
    {
        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", Runway("01", surface: RunwaySurface.Grass)),
            Airport("KBBB", Runway("19")));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwaySurfaceUnsupported);
    }

    [Fact]
    public void TooNarrowRunwayIsRejected()
    {
        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", Runway("01", width: 40)),
            Airport("KBBB", Runway("19")));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayTooNarrow
                && issue.RequiredFeet == 50
                && issue.AvailableFeet == 40);
    }

    [Fact]
    public void MissingAircraftPerformanceReturnsInsufficientData()
    {
        var aircraft = new AircraftRegistryRecord(
            Aircraft().Capabilities,
            IsInstalled: true,
            RunwayPerformance: null);

        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            aircraft,
            Airport("KAAA", Runway("01")),
            Airport("KBBB", Runway("19")));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Reason == DispatchFeasibilityReason.AircraftRunwayPerformanceUnknown);
    }

    [Fact]
    public void MissingRunwayMeasurementReturnsInsufficientData()
    {
        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", Runway("01", length: null)),
            Airport("KBBB", Runway("19")));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayLengthUnknown);
    }

    [Fact]
    public void AllClosedRunwaysAreInfeasible()
    {
        DispatchFeasibilityResult result = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", Runway("01", closed: true), Runway("10", closed: true)),
            Airport("KBBB", Runway("19")));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Equal(
            2,
            result.Issues.Count(
                issue => issue.Endpoint == DispatchEndpoint.Origin
                    && issue.Reason == DispatchFeasibilityReason.RunwayClosed));
    }

    [Fact]
    public void RunwayOrderDoesNotChangeResult()
    {
        RunwayRecord shortRunway = Runway("02", 1700, 75);
        RunwayRecord longRunway = Runway("20", 4200, 100);

        DispatchFeasibilityResult first = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", shortRunway, longRunway),
            Airport("KBBB", Runway("19")));

        DispatchFeasibilityResult second = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            Airport("KAAA", longRunway, shortRunway),
            Airport("KBBB", Runway("19")));

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.OriginRunwayIdentifier, second.OriginRunwayIdentifier);
        Assert.Equal(first.DestinationRunwayIdentifier, second.DestinationRunwayIdentifier);
        Assert.Equal(first.Issues, second.Issues);
        Assert.Equal("20", first.OriginRunwayIdentifier);
    }

    [Fact]
    public void MilitaryAccessDoesNotOverrideOrChangePhysicalCompatibility()
    {
        DispatchFeasibilityResult civilian = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(AircraftAccess.Civilian),
            Airport("KAAA", Runway("01")),
            Airport("KBBB", Runway("19")));

        DispatchFeasibilityResult military = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(AircraftAccess.Military),
            Airport("KAAA", Runway("01")),
            Airport("KBBB", Runway("19")));

        Assert.Equal(civilian.Status, military.Status);
        Assert.Equal(civilian.OriginRunwayIdentifier, military.OriginRunwayIdentifier);
        Assert.Equal(civilian.DestinationRunwayIdentifier, military.DestinationRunwayIdentifier);
        Assert.Equal(civilian.Issues, military.Issues);
    }

    [Fact]
    public async Task KnownButNotInstalledAircraftIsInfeasible()
    {
        AircraftRegistryRecord knownOnly = Aircraft() with { IsInstalled = false };
        var service = new DispatchFeasibilityService(
            new StubAircraftRegistrySource(knownOnly),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("01")),
                    ["KBBB"] = Airport("KBBB", Runway("19"))
                }));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB");

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(result.Issues);
        Assert.Equal(DispatchFeasibilityReason.AircraftNotInstalled, issue.Reason);
    }

    [Fact]
    public async Task PersistedUnavailableAircraftIsInfeasible()
    {
        var service = new DispatchFeasibilityService(
            new StubAircraftRegistrySource(Aircraft()),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("01")),
                    ["KBBB"] = Airport("KBBB", Runway("19"))
                }),
            new StubAircraftAvailabilityStore(
                new AircraftAvailabilityState(
                    "fixture-aircraft",
                    AircraftAvailabilityStatus.Unavailable)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "provider-alias",
            "KAAA",
            "KBBB");

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(result.Issues);
        Assert.Equal(DispatchFeasibilityReason.AircraftUnavailable, issue.Reason);
    }

    [Fact]
    public async Task MissingAvailabilityStateDoesNotBlockInstalledAircraft()
    {
        var service = new DispatchFeasibilityService(
            new StubAircraftRegistrySource(Aircraft()),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("01")),
                    ["KBBB"] = Airport("KBBB", Runway("19"))
                }),
            new StubAircraftAvailabilityStore(null));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB");

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task UnknownAircraftAndAirportSourcesFailClosed()
    {
        var service = new DispatchFeasibilityService(
            new StubAircraftRegistrySource(null),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("01"))
                }));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "unknown-aircraft",
            "KAAA",
            "KBBB");

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Reason == DispatchFeasibilityReason.AircraftNotFound);
        Assert.Contains(
            result.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Destination
                && issue.Reason == DispatchFeasibilityReason.AirportNotFound);
    }

    private sealed class StubAircraftRegistrySource(AircraftRegistryRecord? aircraft)
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                aircraft is null
                    ? null
                    : CompleteResolution(aircraft));
    }

    private static AircraftRegistryResolution CompleteResolution(
        AircraftRegistryRecord aircraft) =>
        AircraftRegistryResolver.Resolve(
        [
            new(
                aircraft.AircraftId,
                "test-source",
                aircraft.AircraftId,
                AircraftDataConfidence.Verified,
                aircraft.IsInstalled,
                aircraft.DisplayName,
                aircraft.Capabilities.Capabilities,
                aircraft.Capabilities.Access,
                aircraft.Capabilities.MaximumPayloadPounds,
                aircraft.Capabilities.MaximumRangeNauticalMiles,
                aircraft.Capabilities.TypicalCruiseKnots,
                aircraft.Capabilities.Seats,
                aircraft.Capabilities.EngineCount,
                aircraft.Capabilities.IfrCapable,
                aircraft.Capabilities.Pressurized,
                aircraft.Capabilities.RetractableGear,
                aircraft.RunwayPerformance)
        ]);

    private sealed class StubAircraftAvailabilityStore(
        AircraftAvailabilityState? state)
        : IAircraftAvailabilityStore
    {
        public Task<AircraftAvailabilityState?> FindAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AircraftAvailabilityState? result =
                state is not null
                && string.Equals(
                    state.CanonicalAircraftId,
                    canonicalAircraftId,
                    StringComparison.OrdinalIgnoreCase)
                    ? state
                    : null;

            return Task.FromResult(result);
        }

        public Task SetAsync(
            AircraftAvailabilityState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubAirportDataSource(
        IReadOnlyDictionary<string, AirportRecord> airports)
        : IAirportDataSource
    {
        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            airports.TryGetValue(icao, out AirportRecord? airport);
            return Task.FromResult(airport);
        }
    }
}
