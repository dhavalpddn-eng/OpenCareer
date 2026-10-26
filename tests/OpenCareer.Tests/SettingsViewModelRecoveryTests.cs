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
    public async Task FailedRecoveryWarnsBeforeAnotherFlightStarts()
    {
        var sessions = new FlightSessionCoordinator();
        var persistence =
            new FlightSessionPersistenceService(
                sessions,
                new FailingCheckpointStore());

        await Assert.ThrowsAsync<IOException>(
            () => persistence.RecoverAsync());

        TestContext context = Create(
            sessions: sessions,
            flightPersistence: persistence);

        Assert.Contains("RECOVERY ATTENTION", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("did not complete", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("did not clear", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("Close and reopen OpenCareer", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("diagnostic bundle", context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain(
            "Synthetic checkpoint read failure",
            context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain(
            "No flight session is currently loaded",
            context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    [Fact]
    public void NormalCareerClearlyDisablesCleanTestReset()
    {
        TestContext context = Create();

        Assert.Contains("NORMAL CAREER", context.ViewModel.DataProfileStatus);
        Assert.False(context.ViewModel.CanScheduleDevelopmentProfileReset);

        context.ViewModel.ScheduleDevelopmentProfileReset();

        Assert.Contains("normal career profile", context.ViewModel.ActionStatus);
        Assert.False(File.Exists(
            Path.Combine(_root, ".opencareer-reset-on-next-launch")));
    }

    [Fact]
    public void BindingStatusUsesReadOnlyDiscoverySourceWithoutClaimingUnbound()
    {
        TestContext context = Create(
            new FakeBindingSource(
                InputBindingDiscoverySnapshot.Unavailable(
                    "Official runtime profile assignments are not exposed.")));

        Assert.Contains("UNAVAILABLE", context.ViewModel.InputBindingStatus);
        Assert.Contains("not exposed", context.ViewModel.InputBindingStatus);
        Assert.DoesNotContain("UNBOUND", context.ViewModel.InputBindingStatus);
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

    private TestContext Create(
        IInputBindingDiscoverySource? inputBindings = null,
        FlightSessionCoordinator? sessions = null,
        FlightSessionPersistenceService? flightPersistence = null)
    {
        var paths = new OpenCareerDataPaths(_root);
        var settings = new FakeSettings();
        var connection = new FakeConnection();
        var telemetry = new FakeTelemetrySource();
        sessions ??= new FlightSessionCoordinator();
        flightPersistence ??=
            new FlightSessionPersistenceService(
                sessions,
                new EmptyCheckpointStore());
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
            sessions,
            flightPersistence,
            inputBindings);

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

    private sealed class FakeBindingSource(
        InputBindingDiscoverySnapshot snapshot) : IInputBindingDiscoverySource
    {
        public InputBindingDiscoverySnapshot Read(
            IReadOnlyList<InputBindingActionIdentity> actions) => snapshot;
    }

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

    private sealed class EmptyCheckpointStore : IFlightSessionCheckpointStore
    {
        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FlightSession?>(null);

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FailingCheckpointStore : IFlightSessionCheckpointStore
    {
        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic checkpoint read failure.");

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

}
