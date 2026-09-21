using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class OperationDispatchPlanningTests
{
    [Fact]
    public async Task NeededPhysicalFieldsCanPassWhileUnrelatedAircraftFieldsRemainUnknown()
    {
        AircraftRegistryResolution aircraft = Resolution();
        Assert.Contains(AircraftRegistryField.Seats, aircraft.UnresolvedCapabilityFields);
        Assert.Contains(AircraftRegistryField.IfrCapable, aircraft.UnresolvedCapabilityFields);

        var service = Service(
            aircraft,
            Airport("KAAA", Runway("18", 5000)),
            Airport("KBBB", Runway("36", 4500)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1200, 500));

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Equal("18", result.OriginRunwayIdentifier);
        Assert.Equal("36", result.DestinationRunwayIdentifier);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task PayloadAboveKnownMaximumIsInfeasibleWithExplicitValues()
    {
        var service = Service(
            Resolution(maximumPayloadPounds: 2000),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(2500, 500));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.PayloadExceedsAircraftMaximum);

        Assert.Equal(AircraftRegistryField.MaximumPayloadPounds, issue.AircraftField);
        Assert.Equal(2500, issue.RequiredPounds);
        Assert.Equal(2000, issue.AvailablePounds);
    }

    [Fact]
    public async Task RangeAboveKnownMaximumIsInfeasibleWithExplicitValues()
    {
        var service = Service(
            Resolution(maximumRangeNauticalMiles: 800),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 900));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.RangeExceedsAircraftMaximum);

        Assert.Equal(AircraftRegistryField.MaximumRangeNauticalMiles, issue.AircraftField);
        Assert.Equal(900, issue.RequiredNauticalMiles);
        Assert.Equal(800, issue.AvailableNauticalMiles);
    }

    [Fact]
    public async Task MissingPayloadCapacityRemainsInsufficientData()
    {
        var service = Service(
            Resolution(maximumPayloadPounds: null),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.AircraftPayloadCapacityUnknown
                && issue.AircraftField == AircraftRegistryField.MaximumPayloadPounds);
    }

    [Fact]
    public async Task MissingRangeRemainsInsufficientData()
    {
        var service = Service(
            Resolution(maximumRangeNauticalMiles: null),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.AircraftRangeUnknown
                && issue.AircraftField == AircraftRegistryField.MaximumRangeNauticalMiles);
    }

    [Fact]
    public async Task RunwayFailureRemainsAuthoritativeForOperation()
    {
        var service = Service(
            Resolution(),
            Airport("KAAA", Runway("18", 1500)),
            Airport("KBBB", Runway("36", 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);
    }

    [Fact]
    public async Task MissingRunwayPerformanceRemainsInsufficientData()
    {
        var service = Service(
            Resolution(includeRunwayPerformance: false),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.AircraftRunwayPerformanceUnknown);
    }

    [Fact]
    public async Task DefinitePayloadFailureBeatsUnknownRunwayData()
    {
        var service = Service(
            Resolution(maximumPayloadPounds: 1000, includeRunwayPerformance: false),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1500, 500));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.PayloadExceedsAircraftMaximum);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.AircraftRunwayPerformanceUnknown);
    }

    [Fact]
    public async Task KnownButNotInstalledAircraftIsInfeasibleBeforePhysicalEvaluation()
    {
        var service = Service(
            Resolution(isInstalled: false),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(result.Issues);
        Assert.Equal(DispatchFeasibilityReason.AircraftNotInstalled, issue.Reason);
    }

    [Fact]
    public async Task PersistedUnavailableAircraftIsInfeasibleBeforePhysicalEvaluation()
    {
        AircraftRegistryResolution aircraft = Resolution();
        var service = new OperationDispatchPlanningService(
            new StubAircraftRegistrySource(aircraft),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("18")),
                    ["KBBB"] = Airport("KBBB", Runway("36"))
                }),
            weatherSource: null,
            new StubAircraftAvailabilityStore(
                new AircraftAvailabilityState(
                    aircraft.CanonicalAircraftId,
                    AircraftAvailabilityStatus.Unavailable)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "provider-alias",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(result.Issues);
        Assert.Equal(DispatchFeasibilityReason.AircraftUnavailable, issue.Reason);
    }

    [Fact]
    public async Task MissingSourceRecordsFailClosedBeforePhysicalEvaluation()
    {
        var service = new OperationDispatchPlanningService(
            new StubAircraftRegistrySource(null),
            new StubAirportDataSource(
                new Dictionary<string, AirportRecord>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KAAA"] = Airport("KAAA", Runway("18"))
                }));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "missing-aircraft",
            "KAAA",
            "KBBB",
            new(1000, 500));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason == DispatchFeasibilityReason.AircraftNotFound);
        Assert.Contains(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Destination
                && issue.Reason == DispatchFeasibilityReason.AirportNotFound);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    public async Task InvalidOperationRequirementsAreRejected(
        double payloadPounds,
        double requiredRangeNauticalMiles)
    {
        var service = Service(
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                new(payloadPounds, requiredRangeNauticalMiles)));
    }

    private static OperationDispatchPlanningService Service(
        AircraftRegistryResolution resolution,
        params AirportRecord[] airports) =>
        new(
            new StubAircraftRegistrySource(resolution),
            new StubAirportDataSource(
                airports.ToDictionary(
                    static airport => airport.Icao,
                    StringComparer.OrdinalIgnoreCase)));

    private static AircraftRegistryResolution Resolution(
        double? maximumPayloadPounds = 2000,
        double? maximumRangeNauticalMiles = 800,
        AircraftRunwayPerformanceProfile? runwayPerformance = null,
        bool includeRunwayPerformance = true,
        bool isInstalled = true)
    {
        AircraftRunwayPerformanceProfile? resolvedRunwayPerformance =
            includeRunwayPerformance
                ? runwayPerformance ?? new(
                    MinimumTakeoffRunwayFeet: 1800,
                    MinimumLandingRunwayFeet: 1600,
                    MinimumRunwayWidthFeet: 50,
                    SupportedSurfaces: RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete,
                    Confidence: AircraftDataConfidence.Verified,
                    Source: "test")
                : null;

        return AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: "fixture-aircraft",
                ProviderId: "test-source",
                ProviderRecordId: "fixture",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: isInstalled,
                MaximumPayloadPounds: maximumPayloadPounds,
                MaximumRangeNauticalMiles: maximumRangeNauticalMiles,
                RunwayPerformance: resolvedRunwayPerformance)
        ]);
    }

    private static AirportRecord Airport(
        string icao,
        params RunwayRecord[] runways) =>
        new(icao, $"{icao} Fixture", runways);

    private static RunwayRecord Runway(
        string identifier,
        double? lengthFeet = 5000,
        double? widthFeet = 100,
        RunwaySurface surface = RunwaySurface.Asphalt) =>
        new(identifier, lengthFeet, widthFeet, surface);

    private sealed class StubAircraftRegistrySource(AircraftRegistryResolution? result)
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
            cancellationToken.ThrowIfCancellationRequested();
            airports.TryGetValue(icao, out AirportRecord? airport);
            return Task.FromResult(airport);
        }
    }
}
