using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SettingsViewModelRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"opencareer-settings-recovery-{Guid.NewGuid():N}");

    [Fact]
    public void NoSessionReportsAvailableSqliteRecoveryWithoutStaleMilestoneCopy()
    {
        TestContext context = Create();

        Assert.Contains("SQLite-backed", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("previous-valid fallback", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("No flight session is currently loaded", context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain("MBL-07", context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    [Fact]
    public void ActiveSessionRefreshesStatusWithoutPersistenceWrites()
    {
        TestContext context = Create();
        int changes = 0;
        context.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.CareerRecoveryStatus))
                changes++;
        };

        context.Sessions.Restore(FlightSession.Start(Now));

        Assert.Contains("active flight session", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("restart checkpoints", context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(1, changes);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Suspended, "suspended flight session", "not completed")]
    [InlineData(FlightSessionStatus.Interrupted, "interrupted terminal session", "not completed")]
    public void NonCompletedRecoveryStatesAreNotDescribedAsCompleted(
        FlightSessionStatus status,
        string expectedState,
        string expectedQualification)
    {
        TestContext context = Create();

        context.Sessions.Restore(Session(status));

        Assert.Contains(expectedState, context.ViewModel.CareerRecoveryStatus);
        Assert.Contains(expectedQualification, context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain(
            "A completed terminal session",
            context.ViewModel.CareerRecoveryStatus);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Completed, "completed terminal session")]
    [InlineData(FlightSessionStatus.Cancelled, "cancelled terminal session")]
    public void TerminalSessionRemainsPendingUntilCleanup(
        FlightSessionStatus status,
        string expectedState)
    {
        TestContext context = Create();

        context.Sessions.Restore(Session(status));

        Assert.Contains(expectedState, context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("cleanup", context.ViewModel.CareerRecoveryStatus);

        context.Sessions.ClearTerminalSession();

        Assert.Contains(
            "No flight session is currently loaded",
            context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    private TestContext Create()
    {
        var paths = new OpenCareerDataPaths(_root);
        var settings = new FakeSettings();
        var connection = new FakeConnection();
        var telemetry = new FakeTelemetrySource();
        var sessions = new FlightSessionCoordinator();
        var diagnostics = new DiagnosticBundleService(
            paths,
            settings,
            connection,
            telemetry,
            NullLogger<DiagnosticBundleService>.Instance);
        var backup = new AppDataBackupService(
            paths,
            new SqliteDatabaseSnapshotService(
                new OpenCareerDatabaseOptions(paths.DatabaseFile)),
            NullLogger<AppDataBackupService>.Instance);
        var shell = new ShellOpenService(
            NullLogger<ShellOpenService>.Instance);
        var viewModel = new SettingsViewModel(
            settings,
            connection,
            telemetry,
            diagnostics,
            backup,
            shell,
            paths,
            sessions);

        return new TestContext(
            viewModel,
            settings,
            sessions);
    }

    private static readonly DateTimeOffset Now =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlightSession Session(FlightSessionStatus status) =>
        FlightSession.Start(Now) with
        {
            Status = status,
            OperationState = status switch
            {
                FlightSessionStatus.Suspended => FlightOperationState.Accepted,
                FlightSessionStatus.Interrupted => FlightOperationState.Failed,
                FlightSessionStatus.Completed => FlightOperationState.Complete,
                FlightSessionStatus.Cancelled => FlightOperationState.Cancelled,
                _ => FlightOperationState.Accepted
            }
        };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record TestContext(
        SettingsViewModel ViewModel,
        FakeSettings Settings,
        FlightSessionCoordinator Sessions);

    private sealed class FakeSettings : IAppSettingsService
    {
        public AppPreferences Current { get; private set; } = AppPreferences.Default;
        public int WriteCount { get; private set; }

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(
            AppPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            Current = preferences;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            WriteCount++;
            Current = AppPreferences.Default;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; } =
            new(SimulatorConnectionState.Disconnected);

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTelemetrySource : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest => null;
    }

}
