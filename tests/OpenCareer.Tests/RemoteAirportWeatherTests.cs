using System.Net;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;
using OpenCareer.Infrastructure.Weather;

namespace OpenCareer.Tests;

public sealed class RemoteAirportWeatherTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 20, 19, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task MetarMapsSustainedAndGustWindUsingVerifiedRunwayHeading()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "KAAA 201900Z 18010G20KT 10SM CLR 25/10 A2992");
        using var client = new HttpClient(handler);

        var source = new AviationWeatherMetarSource(
            AirportSource(AirportWithRunway(
                new("18", 180),
                new("36", 0))),
            client,
            new FixedTimeProvider(Now));

        AirportDispatchWeatherObservation observation =
            Assert.IsType<AirportDispatchWeatherObservation>(
                await source.FindWeatherAsync("kaaa"));

        Assert.Equal("KAAA", observation.Icao);
        Assert.Equal(
            AviationWeatherMetarSource.ProviderId,
            observation.SourceId);
        Assert.Equal(
            DispatchWeatherAuthority.Reference,
            observation.Authority);
        Assert.Equal(
            new DateTimeOffset(
                2026,
                9,
                20,
                19,
                0,
                0,
                TimeSpan.Zero),
            observation.ObservedAt);
        Assert.Null(observation.DensityAltitudeFeet);

        RunwayWindObservation wind = Assert.Single(observation.RunwayWinds);
        Assert.Equal("18/36", wind.RunwayIdentifier);
        Assert.Equal(10, wind.SustainedHeadwindKnots!.Value, 6);
        Assert.Equal(0, wind.SustainedCrosswindKnots!.Value, 6);
        Assert.Equal(20, wind.GustHeadwindKnots!.Value, 6);
        Assert.Equal(0, wind.GustCrosswindKnots!.Value, 6);

        Assert.Equal(1, handler.RequestCount);
        Assert.Contains(
            "ids=KAAA",
            handler.LastRequestUri!.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "format=raw",
            handler.LastRequestUri.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "OpenCareer",
            handler.LastUserAgent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosedRunwayEndIsNeverUsedForReferenceWind()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "KAAA 201900Z 18010KT 10SM CLR 25/10 A2992");
        using var client = new HttpClient(handler);

        var source = new AviationWeatherMetarSource(
            AirportSource(AirportWithRunway(
                new("18", 180, IsClosed: true),
                new("36", 0))),
            client,
            new FixedTimeProvider(Now));

        AirportDispatchWeatherObservation observation =
            Assert.IsType<AirportDispatchWeatherObservation>(
                await source.FindWeatherAsync("KAAA"));

        RunwayWindObservation wind = Assert.Single(observation.RunwayWinds);
        Assert.Equal(-10, wind.SustainedHeadwindKnots!.Value, 6);
        Assert.Equal(0, wind.SustainedCrosswindKnots!.Value, 6);
    }

    [Fact]
    public async Task VariableWindDoesNotInventRunwayComponents()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "KAAA 201900Z VRB10G18KT 10SM CLR 25/10 A2992");
        using var client = new HttpClient(handler);

        var source = new AviationWeatherMetarSource(
            AirportSource(AirportWithRunway(
                new("18", 180),
                new("36", 0))),
            client,
            new FixedTimeProvider(Now));

        AirportDispatchWeatherObservation observation =
            Assert.IsType<AirportDispatchWeatherObservation>(
                await source.FindWeatherAsync("KAAA"));

        Assert.Empty(observation.RunwayWinds);
    }

    [Fact]
    public async Task CalmVariableWindCanResolveToZeroForEveryOpenRunway()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "KAAA 201900Z VRB00KT 10SM CLR 25/10 A2992");
        using var client = new HttpClient(handler);

        var source = new AviationWeatherMetarSource(
            AirportSource(AirportWithRunway(
                new("18", 180),
                new("36", 0))),
            client,
            new FixedTimeProvider(Now));

        RunwayWindObservation wind = Assert.Single(
            Assert.IsType<AirportDispatchWeatherObservation>(
                await source.FindWeatherAsync("KAAA"))
            .RunwayWinds);

        Assert.Equal(0, wind.SustainedHeadwindKnots);
        Assert.Equal(0, wind.SustainedCrosswindKnots);
    }

    [Fact]
    public async Task MetresPerSecondAreConvertedToKnots()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "KAAA 201900Z 18010G20MPS 9999 CLR 25/10 Q1013");
        using var client = new HttpClient(handler);

        var source = new AviationWeatherMetarSource(
            AirportSource(AirportWithRunway(
                new("18", 180),
                new("36", 0))),
            client,
            new FixedTimeProvider(Now));

        RunwayWindObservation wind = Assert.Single(
            Assert.IsType<AirportDispatchWeatherObservation>(
                await source.FindWeatherAsync("KAAA"))
            .RunwayWinds);

        Assert.Equal(19.438444924406, wind.SustainedHeadwindKnots!.Value, 9);
        Assert.Equal(38.876889848812, wind.GustHeadwindKnots!.Value, 9);
    }

    [Fact]
    public async Task StaleMetarFailsClosed()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "KAAA 201600Z 18010KT 10SM CLR 25/10 A2992");
        using var client = new HttpClient(handler);

        var source = new AviationWeatherMetarSource(
            AirportSource(AirportWithRunway(
                new("18", 180),
                new("36", 0))),
            client,
            new FixedTimeProvider(Now));

        Assert.Null(await source.FindWeatherAsync("KAAA"));
    }

    [Fact]
    public async Task MissingVerifiedRunwayHeadingFailsBeforeNetworkCall()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "KAAA 201900Z 18010KT 10SM CLR 25/10 A2992");
        using var client = new HttpClient(handler);

        var airport = new AirportRecord(
            "KAAA",
            "Fixture Airport",
            [
                new(
                    "18/36",
                    6000,
                    100,
                    RunwaySurface.Asphalt)
            ]);

        var source = new AviationWeatherMetarSource(
            AirportSource(airport),
            client,
            new FixedTimeProvider(Now));

        Assert.Null(await source.FindWeatherAsync("KAAA"));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NoContentAndHttpFailureReturnUnavailable()
    {
        var airportSource = AirportSource(
            AirportWithRunway(
                new("18", 180),
                new("36", 0)));

        using var noContentClient = new HttpClient(
            new StubHttpMessageHandler(HttpStatusCode.NoContent, string.Empty));

        var noContent = new AviationWeatherMetarSource(
            airportSource,
            noContentClient,
            new FixedTimeProvider(Now));

        Assert.Null(await noContent.FindWeatherAsync("KAAA"));

        using var failureClient = new HttpClient(
            new StubHttpMessageHandler(
                HttpStatusCode.TooManyRequests,
                "rate limited"));

        var failure = new AviationWeatherMetarSource(
            airportSource,
            failureClient,
            new FixedTimeProvider(Now));

        Assert.Null(await failure.FindWeatherAsync("KAAA"));
    }

    [Fact]
    public async Task LocalWeatherWinsWithoutReferenceRequest()
    {
        AirportDispatchWeatherObservation local = Weather(
            "KAAA",
            DispatchWeatherAuthority.LocalSimulator,
            "local");
        AirportDispatchWeatherObservation reference = Weather(
            "KAAA",
            DispatchWeatherAuthority.Reference,
            "reference");

        var localSource = new StubWeatherSource(local);
        var referenceSource = new StubWeatherSource(reference);
        var source = new LocalThenReferenceAirportWeatherSource(
            localSource,
            referenceSource);

        AirportDispatchWeatherObservation result =
            Assert.IsType<AirportDispatchWeatherObservation>(
                await source.FindWeatherAsync("KAAA"));

        Assert.Same(local, result);
        Assert.Equal(1, localSource.RequestCount);
        Assert.Equal(0, referenceSource.RequestCount);
    }

    [Fact]
    public async Task ReferenceWeatherIsUsedOnlyWhenLocalWeatherIsUnavailable()
    {
        AirportDispatchWeatherObservation reference = Weather(
            "KBBB",
            DispatchWeatherAuthority.Reference,
            "reference");

        var localSource = new StubWeatherSource(null);
        var referenceSource = new StubWeatherSource(reference);
        var source = new LocalThenReferenceAirportWeatherSource(
            localSource,
            referenceSource);

        AirportDispatchWeatherObservation result =
            Assert.IsType<AirportDispatchWeatherObservation>(
                await source.FindWeatherAsync("kbbb"));

        Assert.Same(reference, result);
        Assert.Equal(1, localSource.RequestCount);
        Assert.Equal(1, referenceSource.RequestCount);
    }

    [Fact]
    public async Task LayeredSourceRejectsWrongAuthority()
    {
        var localSource = new StubWeatherSource(
            Weather(
                "KAAA",
                DispatchWeatherAuthority.Reference,
                "wrong-authority"));
        var referenceSource = new StubWeatherSource(null);
        var source = new LocalThenReferenceAirportWeatherSource(
            localSource,
            referenceSource);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.FindWeatherAsync("KAAA"));
    }

    private static AirportRecord AirportWithRunway(
        params RunwayEndRecord[] ends) =>
        new(
            "KAAA",
            "Fixture Airport",
            [
                new(
                    "18/36",
                    6000,
                    100,
                    RunwaySurface.Asphalt,
                    IsClosed: false,
                    Ends: ends)
            ]);

    private static IAirportDataSource AirportSource(AirportRecord airport) =>
        new StubAirportDataSource(airport);

    private static AirportDispatchWeatherObservation Weather(
        string icao,
        DispatchWeatherAuthority authority,
        string sourceId) =>
        new(
            icao,
            sourceId,
            authority,
            Now,
            [
                new(
                    "18/36",
                    SustainedHeadwindKnots: 5,
                    SustainedCrosswindKnots: 2)
            ]);

    private sealed class StubAirportDataSource(AirportRecord? airport)
        : IAirportDataSource
    {
        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AirportRecord? result = airport is not null
                && string.Equals(
                    airport.Icao,
                    icao,
                    StringComparison.OrdinalIgnoreCase)
                    ? airport
                    : null;

            return Task.FromResult(result);
        }
    }

    private sealed class StubWeatherSource(
        AirportDispatchWeatherObservation? observation)
        : IAirportDispatchWeatherSource
    {
        public int RequestCount { get; private set; }

        public Task<AirportDispatchWeatherObservation?> FindWeatherAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(observation);
        }
    }

    private sealed class StubHttpMessageHandler(
        HttpStatusCode statusCode,
        string content)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string LastUserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastUserAgent = request.Headers.UserAgent.ToString();

            return Task.FromResult(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(content)
                });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
