using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Tutorials;

namespace OpenCareer.Tests;

public sealed class TutorialViewModelRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"opencareer-tutorial-recovery-{Guid.NewGuid():N}");

    [Fact]
    public async Task FailedProgressWriteKeepsCurrentStepAndCanRetry()
    {
        var store = new ControlledProgressStore();
        TutorialViewModel viewModel = CreateViewModel(store);
        await viewModel.InitializeAsync();
        int initialIndex = viewModel.CurrentIndexForTest();
        store.NextSaveFailure =
            new IOException("Synthetic path /private/tutorial-data must stay out of the UI.");

        await viewModel.NextAsync();

        Assert.Equal(initialIndex, viewModel.CurrentIndexForTest());
        Assert.True(viewModel.CanRunActions);
        Assert.Contains("current tutorial state was kept", viewModel.ActionStatus);
        Assert.Contains("then retry", viewModel.ActionStatus);
        Assert.Contains("diagnostic bundle", viewModel.ActionStatus);
        Assert.DoesNotContain("/private/tutorial-data", viewModel.ActionStatus);
        Assert.DoesNotContain("Synthetic path", viewModel.ActionStatus);

        await viewModel.NextAsync();

        Assert.Equal(initialIndex + 1, viewModel.CurrentIndexForTest());
        Assert.True(viewModel.CanRunActions);
        Assert.Equal(string.Empty, viewModel.ActionStatus);
    }

    [Fact]
    public async Task FailedProgressReadKeepsCurrentTutorialAndCanRetry()
    {
        var store = new ControlledProgressStore();
        TutorialViewModel viewModel = CreateViewModel(store);
        await viewModel.InitializeAsync();
        string initialTitle = viewModel.CurrentTitle;
        int initialIndex = viewModel.CurrentIndexForTest();
        store.NextReadFailure =
            new IOException("Synthetic path /private/tutorial-data must stay out of the UI.");

        await viewModel.StartFirstJobAsync();

        Assert.Equal(initialTitle, viewModel.CurrentTitle);
        Assert.Equal(initialIndex, viewModel.CurrentIndexForTest());
        Assert.True(viewModel.CanRunActions);
        Assert.Contains("current tutorial state was kept", viewModel.ActionStatus);
        Assert.Contains("then retry", viewModel.ActionStatus);
        Assert.Contains("diagnostic bundle", viewModel.ActionStatus);
        Assert.DoesNotContain("/private/tutorial-data", viewModel.ActionStatus);
        Assert.DoesNotContain("Synthetic path", viewModel.ActionStatus);

        await viewModel.StartFirstJobAsync();

        Assert.NotEqual(initialTitle, viewModel.CurrentTitle);
        Assert.Equal(0, viewModel.CurrentIndexForTest());
        Assert.True(viewModel.CanRunActions);
        Assert.Equal(string.Empty, viewModel.ActionStatus);
    }

    [Fact]
    public async Task OverlappingTutorialActionIsRejectedWithoutQueuingAWrite()
    {
        var store = new ControlledProgressStore();
        TutorialViewModel viewModel = CreateViewModel(store);
        await viewModel.InitializeAsync();
        var saveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store.NextSaveStarted = saveStarted;
        store.NextSaveRelease = releaseSave.Task;
        int writesBeforeAction = store.SaveCount;

        Task next = viewModel.NextAsync();
        await saveStarted.Task;

        Assert.False(viewModel.CanRunActions);
        await viewModel.SkipAsync();

        Assert.Equal(writesBeforeAction + 1, store.SaveCount);
        Assert.Contains(
            "already running",
            viewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);

        releaseSave.SetResult();
        await next;

        Assert.True(viewModel.CanRunActions);
        Assert.Equal(string.Empty, viewModel.ActionStatus);
    }

    [Fact]
    public async Task CancellationPropagatesAndReleasesTheActionGate()
    {
        var store = new ControlledProgressStore();
        TutorialViewModel viewModel = CreateViewModel(store);
        await viewModel.InitializeAsync();
        store.NextSaveRelease = Task.Delay(Timeout.InfiniteTimeSpan);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.NextAsync(cancellation.Token));

        Assert.True(viewModel.CanRunActions);
        await viewModel.NextAsync();
        Assert.Equal(1, viewModel.CurrentIndexForTest());
    }

    [Fact]
    public async Task JsonStorePathCollisionFailsClosedAndCleansTemporaryFile()
    {
        var paths = new OpenCareerDataPaths(_root);
        Directory.CreateDirectory(paths.TutorialPreferencesFile);
        var store = new JsonTutorialProgressStore(
            NullLogger<JsonTutorialProgressStore>.Instance,
            paths);

        Exception? readFailure = await Record.ExceptionAsync(
            () => store.GetAsync(AppTutorialCatalog.AppIntroId));
        Exception? writeFailure = await Record.ExceptionAsync(
            () => store.SaveAsync(
                new TutorialProgress(AppTutorialCatalog.AppIntroId)));

        Assert.True(
            readFailure is IOException or UnauthorizedAccessException,
            $"Expected a filesystem read failure, but received {readFailure?.GetType().Name ?? "no exception"}.");
        Assert.True(
            writeFailure is IOException or UnauthorizedAccessException,
            $"Expected a filesystem write failure, but received {writeFailure?.GetType().Name ?? "no exception"}.");

        Assert.False(File.Exists(paths.TutorialPreferencesFile + ".tmp"));
    }

    [Fact]
    public async Task MissingProgressFileStartsFreshAndCanBePersisted()
    {
        var paths = new OpenCareerDataPaths(_root);
        var store = new JsonTutorialProgressStore(
            NullLogger<JsonTutorialProgressStore>.Instance,
            paths);

        TutorialProgress initial =
            await store.GetAsync(AppTutorialCatalog.AppIntroId);
        var saved = initial with { LastStepId = "connection" };
        await store.SaveAsync(saved);

        Assert.Equal(new TutorialProgress(AppTutorialCatalog.AppIntroId), initial);
        Assert.Equal(saved, await store.GetAsync(AppTutorialCatalog.AppIntroId));
    }

    [Fact]
    public async Task CorruptJsonCannotBeReadOrOverwritten()
    {
        var paths = new OpenCareerDataPaths(_root);
        Directory.CreateDirectory(_root);
        const string corruptDocument = "{ not-valid-json";
        await File.WriteAllTextAsync(
            paths.TutorialPreferencesFile,
            corruptDocument);
        var store = new JsonTutorialProgressStore(
            NullLogger<JsonTutorialProgressStore>.Instance,
            paths);

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => store.GetAsync(AppTutorialCatalog.AppIntroId));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => store.SaveAsync(
                new TutorialProgress(AppTutorialCatalog.AppIntroId)));

        Assert.Equal(
            corruptDocument,
            await File.ReadAllTextAsync(paths.TutorialPreferencesFile));
        Assert.False(File.Exists(paths.TutorialPreferencesFile + ".tmp"));
    }

    [Fact]
    public async Task UnsupportedSchemaCannotBeReadOrOverwritten()
    {
        var paths = new OpenCareerDataPaths(_root);
        Directory.CreateDirectory(_root);
        const string unsupportedDocument =
            """
            {
              "SchemaVersion": 2,
              "Tutorials": {}
            }
            """;
        await File.WriteAllTextAsync(
            paths.TutorialPreferencesFile,
            unsupportedDocument);
        var store = new JsonTutorialProgressStore(
            NullLogger<JsonTutorialProgressStore>.Instance,
            paths);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.GetAsync(AppTutorialCatalog.AppIntroId));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(
                new TutorialProgress(AppTutorialCatalog.AppIntroId)));

        Assert.Equal(
            unsupportedDocument,
            await File.ReadAllTextAsync(paths.TutorialPreferencesFile));
        Assert.False(File.Exists(paths.TutorialPreferencesFile + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TutorialViewModel CreateViewModel(ControlledProgressStore store)
    {
        var coordinator = new TutorialCoordinator(
            new AppTutorialCatalog(),
            new AvailableReadiness(),
            store);
        return new TutorialViewModel(
            coordinator,
            NullLogger<TutorialViewModel>.Instance);
    }

    private sealed class AvailableReadiness : ITutorialFeatureReadiness
    {
        public TutorialFeatureState GetState(string featureKey) =>
            TutorialFeatureState.Available;
    }

    private sealed class ControlledProgressStore : ITutorialProgressStore
    {
        private readonly Dictionary<string, TutorialProgress> _values =
            new(StringComparer.Ordinal);

        public Exception? NextReadFailure { get; set; }
        public Exception? NextSaveFailure { get; set; }
        public TaskCompletionSource? NextSaveStarted { get; set; }
        public Task? NextSaveRelease { get; set; }
        public int SaveCount { get; private set; }

        public Task<TutorialProgress> GetAsync(
            string tutorialId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Exception? failure = NextReadFailure;
            NextReadFailure = null;
            if (failure is not null)
                return Task.FromException<TutorialProgress>(failure);

            return Task.FromResult(
                _values.TryGetValue(tutorialId, out TutorialProgress? progress)
                    ? progress
                    : new TutorialProgress(tutorialId));
        }

        public async Task SaveAsync(
            TutorialProgress progress,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;

            Exception? failure = NextSaveFailure;
            NextSaveFailure = null;
            if (failure is not null)
                throw failure;

            TaskCompletionSource? started = NextSaveStarted;
            Task? release = NextSaveRelease;
            NextSaveStarted = null;
            NextSaveRelease = null;
            started?.SetResult();
            if (release is not null)
                await release.WaitAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            _values[progress.TutorialId] = progress;
        }
    }
}

internal static class TutorialViewModelTestExtensions
{
    public static int CurrentIndexForTest(this TutorialViewModel viewModel)
    {
        string[] parts = viewModel.StepPosition.Split(
            '/',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out int oneBased)
            ? oneBased - 1
            : -1;
    }
}
