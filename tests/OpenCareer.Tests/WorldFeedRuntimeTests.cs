using System.Net;
using System.Text;
using System.Text.Json;
using OpenCareer.Application.WorldFeed;
using OpenCareer.Domain.Events;
using OpenCareer.Infrastructure.WorldFeed;

namespace OpenCareer.Tests;

public sealed class WorldFeedRuntimeTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 17, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OfflineNarratorIsDeterministicForSameWorldState()
    {
        var request = Request();
        var narrator = new DeterministicWorldFeedNarrator();

        var first = await narrator.GenerateAsync(request);
        var second = await narrator.GenerateAsync(request);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
            AssertPostEquivalent(first[i], second[i]);
        Assert.NotEmpty(first);
        Assert.All(first, post => Assert.False(post.IsAiGenerated));
    }

    [Fact]
    public async Task PrimaryNarratorFailureFallsBackOffline()
    {
        var service = new WorldFeedNarrationService(
            new ThrowingNarrator(),
            new DeterministicWorldFeedNarrator());

        var result = await service.GenerateAsync(Request());

        Assert.True(result.UsedOfflineFallback);
        Assert.Equal("offline-deterministic", result.NarratorName);
        Assert.Equal(nameof(HttpRequestException), result.FallbackReason);
        Assert.NotEmpty(result.Posts);
    }

    [Fact]
    public async Task OpenAiNarratorUsesOnlySuppliedFactsAndNoWebTools()
    {
        var request = Request();
        var factKey = $"signal:{request.Signals[0].SignalId:N}";
        var outputText = JsonSerializer.Serialize(new
        {
            posts = new[]
            {
                new
                {
                    category = "Cargo",
                    headline = "Freight demand building around KRME",
                    body = "Operators are seeing stronger simulated cargo pressure as local capacity tightens.",
                    factKeys = new[] { factKey }
                }
            }
        });
        var responseJson = JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[]
            {
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new { type = "output_text", text = outputText }
                    }
                }
            }
        });

        var handler = new RecordingHandler(responseJson);
        using var httpClient = new HttpClient(handler);
        var narrator = new OpenAiWorldFeedNarrator(
            httpClient,
            new OpenAiWorldFeedOptions("test-key", "test-model"));

        var posts = await narrator.GenerateAsync(request);

        var post = Assert.Single(posts);
        Assert.True(post.IsAiGenerated);
        Assert.Contains(request.Signals[0].SignalId, post.RelatedSignalIds!);
        Assert.Contains("no live web/news collection", post.SourceDisclosure!, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(handler.RequestBody);
        Assert.Contains("\"model\":\"test-model\"", handler.RequestBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("\"tools\"", handler.RequestBody!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("web_search", handler.RequestBody!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-key", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task SqliteStoreRoundTripsPostsAndCoordinatorThrottlesRefresh()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"opencareer-worldfeed-{Guid.NewGuid():N}.db");

        try
        {
            var store = new SqliteWorldFeedPostStore(databasePath);
            var service = new WorldFeedNarrationService(
                primaryNarrator: null,
                new DeterministicWorldFeedNarrator());
            var coordinator = new WorldFeedCoordinator(
                service,
                store,
                new WorldFeedRefreshPolicy(TimeSpan.FromHours(1), 5));

            var first = await coordinator.RefreshAsync(Request());
            Assert.False(first.WasSkipped);
            Assert.True(first.Generation!.UsedOfflineFallback);

            var stored = await store.ReadTimelineAsync(Epoch, "KRME", 20);
            Assert.NotEmpty(stored);
            Assert.Equal(first.Generation.Posts.Count, stored.Count);
            for (var i = 0; i < stored.Count; i++)
                AssertPostEquivalent(first.Generation.Posts[i], stored[i]);

            var skipped = await coordinator.RefreshAsync(Request(Epoch.AddMinutes(30)));
            Assert.True(skipped.WasSkipped);
            Assert.Null(skipped.Generation);

            var later = await coordinator.RefreshAsync(Request(Epoch.AddHours(2)));
            Assert.False(later.WasSkipped);

            var latest = await store.GetLatestCreatedAtAsync("KRME");
            Assert.Equal(Epoch.AddHours(2), latest);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
        }
    }

    [Fact]
    public async Task UnknownAiFactReferenceIsRejected()
    {
        var outputText = JsonSerializer.Serialize(new
        {
            posts = new[]
            {
                new
                {
                    category = "Market",
                    headline = "Invented update",
                    body = "This should never become a valid post.",
                    factKeys = new[] { "unknown:fact" }
                }
            }
        });
        var responseJson = JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[]
            {
                new
                {
                    type = "message",
                    content = new[] { new { type = "output_text", text = outputText } }
                }
            }
        });

        using var httpClient = new HttpClient(new RecordingHandler(responseJson));
        var narrator = new OpenAiWorldFeedNarrator(
            httpClient,
            new OpenAiWorldFeedOptions("test-key", "test-model"));

        await Assert.ThrowsAsync<InvalidDataException>(() => narrator.GenerateAsync(Request()));
    }

    private static WorldFeedNarrationRequest Request(DateTimeOffset? generatedAt = null)
    {
        var at = generatedAt ?? Epoch;
        var signal = new WorldSignal(
            Guid.Parse("6da6da32-5de0-4f90-bd0a-70b726701001"),
            WorldSignalType.CargoTrend,
            "KRME",
            Magnitude: 0.70,
            ObservedAt: at.AddMinutes(-10),
            FetchedAt: at.AddMinutes(-10),
            ExpiresAt: at.AddHours(4),
            WorldSignalSourceKind.DeterministicSimulation,
            WorldSignalValidationStatus.Validated,
            Confidence: 0.90);

        return new WorldFeedNarrationRequest(
            CareerSeed: 424242,
            GeneratedAt: at,
            ScopeId: "KRME",
            Signals: [signal],
            ActiveEvents: Array.Empty<WorldEventInstance>(),
            MaximumPosts: 4);
    }

    private static void AssertPostEquivalent(WorldFeedPost expected, WorldFeedPost actual)
    {
        Assert.Equal(expected.PostId, actual.PostId);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.Category, actual.Category);
        Assert.Equal(expected.Headline, actual.Headline);
        Assert.Equal(expected.Body, actual.Body);
        Assert.Equal(expected.ScopeId, actual.ScopeId);
        Assert.Equal(expected.ExpiresAt, actual.ExpiresAt);
        Assert.Equal(expected.IsAiGenerated, actual.IsAiGenerated);
        Assert.Equal(expected.SourceDisclosure, actual.SourceDisclosure);
        Assert.Equal(
            expected.RelatedSignalIds ?? Array.Empty<Guid>(),
            actual.RelatedSignalIds ?? Array.Empty<Guid>());
        Assert.Equal(
            expected.RelatedWorldEventIds ?? Array.Empty<string>(),
            actual.RelatedWorldEventIds ?? Array.Empty<string>());
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private sealed class ThrowingNarrator : IWorldFeedNarrator
    {
        public string Name => "throwing-test";

        public Task<IReadOnlyList<WorldFeedPost>> GenerateAsync(
            WorldFeedNarrationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Simulated offline API.");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public RecordingHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
