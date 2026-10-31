using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class AirportDataCatalogTests
{
    [Fact]
    public async Task LocalSimulatorWinsWithoutMergingReferenceRunwayFacts()
    {
        DateTimeOffset now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        var local = new StubAirportSource(
            "msfs-runtime",
            AirportDataAuthority.LocalSimulator,
            Observation(
                Airport(
                    "KAAA",
                    "Simulator Airport",
                    Runway(
                        "18",
                        length: null,
                        width: 75,
                        surface: RunwaySurface.Unknown)),
                "msfs-runtime",
                AirportDataAuthority.LocalSimulator,
                now));

        var reference = new StubAirportSource(
            "reference-db",
            AirportDataAuthority.Reference,
            Observation(
                Airport(
                    "KAAA",
                    "Reference Airport",
                    Runway(
                        "18",
                        length: 6000,
                        width: 150,
                        surface: RunwaySurface.Asphalt)),
                "reference-db",
                AirportDataAuthority.Reference,
                now));

        var source = new CachedAirportDataSource([reference, local]);

        AirportDataObservation result = Assert.IsType<AirportDataObservation>(
            await source.FindAirportObservationAsync("kaaa"));

        Assert.Equal("Simulator Airport", result.Airport.Name);
        RunwayRecord runway = Assert.Single(result.Airport.Runways);
        Assert.Null(runway.UsableLengthFeet);
        Assert.Equal(75, runway.WidthFeet);
        Assert.Equal(RunwaySurface.Unknown, runway.Surface);
        Assert.Equal(1, local.Calls);
        Assert.Equal(0, reference.Calls);

        DispatchFeasibilityResult feasibility = RunwayCompatibilityEvaluator.Evaluate(
            Aircraft(),
            result.Airport,
            Airport(
                "KBBB",
                "Destination",
                Runway("36", 5000, 100, RunwaySurface.Asphalt)));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, feasibility.Status);
        Assert.Contains(
            feasibility.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayLengthUnknown);
        Assert.Contains(
            feasibility.Issues,
            issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwaySurfaceUnknown);
    }

    [Fact]
    public async Task ReferenceIsFallbackOnlyWhenLocalSimulatorHasNoObservation()
    {
        DateTimeOffset now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        var local = new StubAirportSource(
            "msfs-runtime",
            AirportDataAuthority.LocalSimulator,
            null);

        var reference = new StubAirportSource(
            "reference-db",
            AirportDataAuthority.Reference,
            Observation(
                Airport(
                    "KAAA",
                    "Reference Airport",
                    Runway("18", 5000, 100, RunwaySurface.Concrete)),
                "reference-db",
                AirportDataAuthority.Reference,
                now));

        var source = new CachedAirportDataSource([reference, local]);

        AirportDataObservation result = Assert.IsType<AirportDataObservation>(
            await source.FindAirportObservationAsync("KAAA"));

        Assert.Equal(AirportDataAuthority.Reference, result.Provenance.Authority);
        Assert.Equal("Reference Airport", result.Airport.Name);
        Assert.Equal(1, local.Calls);
        Assert.Equal(1, reference.Calls);
    }

    [Fact]
    public async Task ReferenceCacheDoesNotHideNewlyAvailableLocalSimulatorData()
    {
        DateTimeOffset now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        var local = new StubAirportSource(
            "msfs-runtime",
            AirportDataAuthority.LocalSimulator,
            null);

        var reference = new StubAirportSource(
            "reference-db",
            AirportDataAuthority.Reference,
            Observation(
                Airport(
                    "KAAA",
                    "Reference Airport",
                    Runway("18", 5000, 100, RunwaySurface.Concrete)),
                "reference-db",
                AirportDataAuthority.Reference,
                now));

        var source = new CachedAirportDataSource([local, reference]);

        AirportDataObservation first = Assert.IsType<AirportDataObservation>(
            await source.FindAirportObservationAsync("KAAA"));
        Assert.Equal(AirportDataAuthority.Reference, first.Provenance.Authority);

        local.Result = Observation(
            Airport(
                "KAAA",
                "Simulator Airport",
                Runway("18", 4800, 90, RunwaySurface.Asphalt)),
            "msfs-runtime",
            AirportDataAuthority.LocalSimulator,
            now.AddMinutes(1));

        AirportDataObservation second = Assert.IsType<AirportDataObservation>(
            await source.FindAirportObservationAsync("KAAA"));

        Assert.Equal(AirportDataAuthority.LocalSimulator, second.Provenance.Authority);
        Assert.Equal("Simulator Airport", second.Airport.Name);
        Assert.Equal(2, local.Calls);
        Assert.Equal(1, reference.Calls);
    }

    [Fact]
    public async Task ReferenceObservationIsCachedUntilFreshnessExpires()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero));

        var reference = new StubAirportSource(
            "reference-db",
            AirportDataAuthority.Reference,
            Observation(
                Airport(
                    "KAAA",
                    "Reference Airport",
                    Runway("18", 5000, 100, RunwaySurface.Asphalt)),
                "reference-db",
                AirportDataAuthority.Reference,
                clock.GetUtcNow()));

        var source = new CachedAirportDataSource(
            [reference],
            new AirportDataCachePolicy
            {
                ReferenceFreshness = TimeSpan.FromMinutes(30)
            },
            clock);

        _ = await source.FindAirportObservationAsync("KAAA");
        _ = await source.FindAirportObservationAsync("KAAA");

        Assert.Equal(1, reference.Calls);

        clock.Advance(TimeSpan.FromMinutes(31));
        reference.Result = Observation(
            Airport(
                "KAAA",
                "Refreshed Airport",
                Runway("18", 5100, 100, RunwaySurface.Asphalt)),
            "reference-db",
            AirportDataAuthority.Reference,
            clock.GetUtcNow());

        AirportDataObservation refreshed = Assert.IsType<AirportDataObservation>(
            await source.FindAirportObservationAsync("KAAA"));

        Assert.Equal(2, reference.Calls);
        Assert.Equal("Refreshed Airport", refreshed.Airport.Name);
    }

    [Fact]
    public async Task SameAuthorityUsesStableSourceIdOrdering()
    {
        DateTimeOffset now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        var bravo = new StubAirportSource(
            "bravo",
            AirportDataAuthority.Reference,
            Observation(
                Airport("KAAA", "Bravo", Runway("18")),
                "bravo",
                AirportDataAuthority.Reference,
                now));

        var alpha = new StubAirportSource(
            "alpha",
            AirportDataAuthority.Reference,
            Observation(
                Airport("KAAA", "Alpha", Runway("18")),
                "alpha",
                AirportDataAuthority.Reference,
                now));

        var source = new CachedAirportDataSource([bravo, alpha]);

        AirportDataObservation result = Assert.IsType<AirportDataObservation>(
            await source.FindAirportObservationAsync("KAAA"));

        Assert.Equal("Alpha", result.Airport.Name);
        Assert.Equal(1, alpha.Calls);
        Assert.Equal(0, bravo.Calls);
    }

    [Fact]
    public async Task MismatchedIcaoIsRejected()
    {
        DateTimeOffset now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        var source = new CachedAirportDataSource(
        [
            new StubAirportSource(
                "bad-source",
                AirportDataAuthority.Reference,
                Observation(
                    Airport("KBBB", "Wrong Airport", Runway("18")),
                    "bad-source",
                    AirportDataAuthority.Reference,
                    now))
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.FindAirportObservationAsync("KAAA"));
    }

    [Fact]
    public async Task ProvenanceMustMatchSourceContract()
    {
        DateTimeOffset now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        var source = new CachedAirportDataSource(
        [
            new StubAirportSource(
                "expected-source",
                AirportDataAuthority.Reference,
                Observation(
                    Airport("KAAA", "Airport", Runway("18")),
                    "different-source",
                    AirportDataAuthority.Reference,
                    now))
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.FindAirportObservationAsync("KAAA"));
    }

    [Fact]
    public async Task NoProviderDataReturnsNull()
    {
        var source = new CachedAirportDataSource(
        [
            new StubAirportSource(
                "msfs-runtime",
                AirportDataAuthority.LocalSimulator,
                null),
            new StubAirportSource(
                "reference-db",
                AirportDataAuthority.Reference,
                null)
        ]);

        Assert.Null(await source.FindAirportObservationAsync("KAAA"));
        Assert.Null(await source.FindAirportAsync("KAAA"));
    }

    [Fact]
    public async Task InvalidateForcesReferenceRefresh()
    {
        DateTimeOffset now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        var reference = new StubAirportSource(
            "reference-db",
            AirportDataAuthority.Reference,
            Observation(
                Airport("KAAA", "First", Runway("18")),
                "reference-db",
                AirportDataAuthority.Reference,
                now));

        var source = new CachedAirportDataSource([reference]);

        _ = await source.FindAirportObservationAsync("KAAA");
        source.Invalidate("kaaa");

        reference.Result = Observation(
            Airport("KAAA", "Second", Runway("18")),
            "reference-db",
            AirportDataAuthority.Reference,
            now.AddMinutes(1));

        AirportDataObservation result = Assert.IsType<AirportDataObservation>(
            await source.FindAirportObservationAsync("KAAA"));

        Assert.Equal("Second", result.Airport.Name);
        Assert.Equal(2, reference.Calls);
    }

    private static AircraftRegistryRecord Aircraft() =>
        new(
            new AircraftCapabilityProfile(
                "fixture-aircraft",
                "Fixture Aircraft",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                2000,
                800,
                150,
                6,
                1,
                true,
                false,
                false),
            IsInstalled: true,
            new AircraftRunwayPerformanceProfile(
                1800,
                1600,
                50,
                RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete,
                AircraftDataConfidence.Verified,
                "test"));

    private static AirportRecord Airport(
        string icao,
        string name,
        params RunwayRecord[] runways) =>
        new(icao, name, runways);

    private static RunwayRecord Runway(
        string identifier,
        double? length = 5000,
        double? width = 100,
        RunwaySurface surface = RunwaySurface.Asphalt,
        bool closed = false) =>
        new(identifier, length, width, surface, closed);

    private static AirportDataObservation Observation(
        AirportRecord airport,
        string sourceId,
        AirportDataAuthority authority,
        DateTimeOffset observedAt) =>
        new(
            airport,
            new AirportDataProvenance(
                sourceId,
                authority,
                observedAt));

    private sealed class StubAirportSource(
        string sourceId,
        AirportDataAuthority authority,
        AirportDataObservation? result)
        : IAirportDataObservationSource
    {
        public string SourceId { get; } = sourceId;
        public AirportDataAuthority Authority { get; } = authority;
        public AirportDataObservation? Result { get; set; } = result;
        public int Calls { get; private set; }

        public Task<AirportDataObservation?> FindAirportObservationAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) =>
            _utcNow = _utcNow.Add(elapsed);
    }
}
