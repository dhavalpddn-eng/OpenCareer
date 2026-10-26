using OpenCareer.App.Services;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Dashboard;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public async Task RefreshFailureClearsStaleProjectionAndRoutesRecoveryToJobs()
    {
        var source = new ControllableSnapshotSource(ReadySnapshot());
        var viewModel = new DashboardViewModel(
            Shell(),
            source,
            new DashboardGuidanceEngine(),
            new OpenCareerDataPaths(Path.Combine(Path.GetTempPath(), "OpenCareer-dashboard-tests")));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasActiveOperation);
        Assert.Single(viewModel.TopOpportunities);

        var failure = new IOException(
            "Sensitive database path C:\\Users\\pilot\\AppData\\Local\\OpenCareer\\opencareer.db");
        viewModel.ReportRefreshFailure(failure);

        Assert.False(viewModel.HasActiveOperation);
        Assert.Empty(viewModel.TopOpportunities);
        Assert.Empty(viewModel.RecentActivity);
        Assert.Empty(viewModel.SocialFeed);
        Assert.True(viewModel.HasPrimaryAction);
        Assert.Equal("Dashboard unavailable", viewModel.PrimaryActionTitle);
        Assert.Contains("Open Jobs", viewModel.PrimaryActionDetail, StringComparison.Ordinal);
        Assert.Contains("Current Flight", viewModel.PrimaryActionDetail, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.PrimaryActionDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppData", viewModel.PrimaryActionDetail, StringComparison.Ordinal);

        DashboardActionTarget? requested = null;
        viewModel.NavigationRequested += (_, args) => requested = args.Target;
        viewModel.RequestPrimaryAction();

        Assert.True(requested.HasValue);
        Assert.Equal(DashboardActionTarget.Jobs, requested.Value);
    }

    [Fact]
    public async Task SuccessfulRetryReplacesFailureGuidanceWithAuthoritativeSnapshot()
    {
        var source = new ControllableSnapshotSource(ReadySnapshot());
        var viewModel = new DashboardViewModel(
            Shell(),
            source,
            new DashboardGuidanceEngine(),
            new OpenCareerDataPaths(Path.Combine(Path.GetTempPath(), "OpenCareer-dashboard-tests")));

        viewModel.ReportRefreshFailure(new IOException("Sensitive failure."));
        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasActiveOperation);
        Assert.Equal("Continue Flight", viewModel.PrimaryActionTitle);
        Assert.DoesNotContain("could not refresh", viewModel.PrimaryActionDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardPageContainsUnexpectedFailureBoundaryForInitialAndTimerRefresh()
    {
        string page = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "UiButtons",
                "DashboardPage.xaml.cs"));

        Assert.Equal(
            2,
            System.Text.RegularExpressions.Regex.Matches(
                page,
                @"catch \(Exception ex\)\s*\{\s*(?:viewModel|_viewModel)\.ReportRefreshFailure\(ex\);\s*\}")
                .Count);
    }

    private static DashboardSnapshot ReadySnapshot() =>
        new(
            Career: null,
            Employment: null,
            Finances: null,
            Aircraft: null,
            ActiveOperation: new(
                "contract-a",
                "Fixture Flight",
                "KJFK",
                "KBOS",
                ActiveOperationStage.InProgress,
                DashboardActionTarget.CurrentFlight,
                "Continue Flight",
                "Recover the current flight.",
                ChecklistRequired: true),
            World: null,
            Opportunities:
            [
                new(
                    "offer-a",
                    "Fixture Offer",
                    "KJFK",
                    "KBOS",
                    "Ferry",
                    OpportunityTier.Standard,
                    IsAvailable: true,
                    FitScore: 90)
            ],
            RecentActivity:
            [
                new(
                    new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
                    "Flight",
                    "Fixture activity")
            ],
            SocialFeed:
            [
                new(
                    "post-a",
                    new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
                    "Dispatcher",
                    "System",
                    "KJFK",
                    "Fixture post")
            ],
            Guidance: []);

    private static ShellViewModel Shell() =>
        new(
            new TestConnection(),
            new TestTelemetrySource(),
            new TestSettingsService());

    private sealed class ControllableSnapshotSource(DashboardSnapshot snapshot)
        : IDashboardSnapshotSource
    {
        public Task<DashboardSnapshot> GetAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }

    private sealed class TestConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; } =
            new(SimulatorConnectionState.Disconnected);

        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestTelemetrySource : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest => null;
    }

    private sealed class TestSettingsService : IAppSettingsService
    {
        public AppPreferences Current { get; private set; } = AppPreferences.Default;

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(
            AppPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            Current = preferences;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default) =>
            UpdateAsync(AppPreferences.Default, cancellationToken);
    }
}
