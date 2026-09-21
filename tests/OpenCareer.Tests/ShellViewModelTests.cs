using OpenCareer.App.ViewModels;
using OpenCareer.Application.Checklists;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Checklists;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void RefreshClearsConnectionAndDoesNotClaimAircraftWithoutTelemetry()
    {
        var connection = new TestConnection { Current = new(SimulatorConnectionState.Connected) };
        var telemetry = new TestTelemetrySource();
        var viewModel = new ShellViewModel(connection, telemetry, new TestSettingsService());

        viewModel.RefreshConnectionStatus();

        Assert.True(viewModel.IsSimulatorConnected);
        Assert.False(viewModel.HasTelemetry);
        Assert.Equal("Waiting for aircraft telemetry", viewModel.AircraftStatus);
        Assert.Contains("telemetry", viewModel.ConnectionDetail);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.RefreshConnectionStatus();
        Assert.Empty(changed);

        connection.Current = new(
            SimulatorConnectionState.Reconnecting,
            SimulatorConnectionIssue.ConnectionLost);
        viewModel.RefreshConnectionStatus();

        Assert.False(viewModel.IsSimulatorConnected);
        Assert.False(viewModel.HasTelemetry);
        Assert.Equal("No aircraft connected", viewModel.AircraftStatus);
        Assert.Contains(nameof(ShellViewModel.IsSimulatorConnected), changed);
    }

    [Fact]
    public void TelemetryRefreshesWhileConnectionSnapshotIsUnchanged()
    {
        var connectionSnapshot = new SimulatorConnectionSnapshot(
            SimulatorConnectionState.Connected);
        var connection = new TestConnection { Current = connectionSnapshot };
        var telemetry = new TestTelemetrySource();
        var viewModel = new ShellViewModel(
            connection,
            telemetry,
            new TestSettingsService());

        viewModel.RefreshConnectionStatus();

        telemetry.Latest = Snapshot(1_000, 110, -500, paused: false);
        viewModel.RefreshConnectionStatus();

        Assert.Same(connectionSnapshot, connection.Current);
        Assert.True(viewModel.HasTelemetry);
        Assert.Equal("Live aircraft telemetry", viewModel.AircraftStatus);
        Assert.Equal("1000 ft MSL / 450 ft AGL", viewModel.AltitudeSummary);
        Assert.Equal("110 kt IAS / 120 kt GS", viewModel.SpeedSummary);
        Assert.Equal("-500 ft/min", viewModel.VerticalSpeedSummary);
        Assert.Equal("090° true", viewModel.HeadingSummary);
        Assert.Equal("Airborne", viewModel.AircraftStateSummary);

        telemetry.Latest = Snapshot(1_050, 112, 0, paused: true);
        viewModel.RefreshConnectionStatus();

        Assert.Equal("Paused", viewModel.AircraftStateSummary);
        Assert.Equal("1050 ft MSL / 450 ft AGL", viewModel.AltitudeSummary);
    }

    [Fact]
    public async Task ChangingUnitsReformatsExistingTelemetryWithoutNewSimulatorSample()
    {
        var connection = new TestConnection
        {
            Current = new SimulatorConnectionSnapshot(
                SimulatorConnectionState.Connected)
        };
        var telemetry = new TestTelemetrySource
        {
            Latest = Snapshot(1_000, 110, -500, paused: false)
        };
        var settings = new TestSettingsService();
        var viewModel = new ShellViewModel(connection, telemetry, settings);

        viewModel.RefreshConnectionStatus();
        Assert.Equal("1000 ft MSL / 450 ft AGL", viewModel.AltitudeSummary);

        await settings.UpdateAsync(
            settings.Current with
            {
                MeasurementSystem = MeasurementSystem.Metric
            });

        Assert.Equal("305 m MSL / 137 m AGL", viewModel.AltitudeSummary);
        Assert.Equal("204 km/h IAS / 222 km/h GS", viewModel.SpeedSummary);
        Assert.Equal("-2.5 m/s", viewModel.VerticalSpeedSummary);
        Assert.Equal("136 kg fuel / 181 kg payload", viewModel.LoadSummary);
    }

    [Fact]
    public async Task RecoveredFlightSessionIsPresentedAsSuspendedNotCompleted()
    {
        DateTimeOffset epoch =
            new(
                2026,
                9,
                19,
                16,
                30,
                0,
                TimeSpan.Zero);

        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new TestFlightStore
            {
                Checkpoint =
                    FlightSession.Start(
                        epoch,
                        sessionId:
                            Guid.Parse(
                                "55555555-5555-5555-5555-555555555555"))
            };

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.RecoverAsync();

        var viewModel =
            new ShellViewModel(
                new TestConnection
                {
                    Current =
                        new SimulatorConnectionSnapshot(
                            SimulatorConnectionState.WaitingForSimulator)
                },
                new TestTelemetrySource(),
                new TestSettingsService(),
                coordinator,
                persistence);

        viewModel.RefreshConnectionStatus();

        Assert.True(viewModel.HasFlightSession);
        Assert.True(viewModel.HasRecoveredFlightSession);
        Assert.Equal(
            "Flight suspended",
            viewModel.CurrentFlightTitle);
        Assert.Contains(
            "SUSPENDED",
            viewModel.CurrentFlightStatus);
        Assert.Equal(
            "RESTORED FROM LOCAL SAVE",
            viewModel.CurrentFlightRecoveryText);
        Assert.Contains(
            "continuity",
            viewModel.CurrentFlightDetail,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "complete",
            viewModel.CurrentFlightStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveFlightSessionIsShownWithoutRecoveryClaim()
    {
        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(
            FlightSession.Start(
                new DateTimeOffset(
                    2026,
                    9,
                    19,
                    16,
                    45,
                    0,
                    TimeSpan.Zero)));

        var viewModel =
            new ShellViewModel(
                new TestConnection(),
                new TestTelemetrySource(),
                new TestSettingsService(),
                coordinator,
                flightPersistence: null);

        viewModel.RefreshConnectionStatus();

        Assert.True(viewModel.HasFlightSession);
        Assert.False(viewModel.HasRecoveredFlightSession);
        Assert.Equal(
            "Active flight",
            viewModel.CurrentFlightTitle);
        Assert.Equal(
            "LIVE SESSION",
            viewModel.CurrentFlightRecoveryText);
    }

    [Fact]
    public async Task ChecklistSnapshotIsRenderedAndPreferenceControlsVisibility()
    {
        var settings = new TestSettingsService();
        var checklist = new TestChecklistSnapshotSource
        {
            Snapshot =
                new FlightChecklistSnapshot(
                    FlightChecklistPhase.EngineStart,
                    [
                        new(
                            FlightChecklistStepId.AircraftReady,
                            FlightChecklistPhase.Preflight,
                            FlightChecklistVerificationCapability.AutoEvidence,
                            FlightChecklistStepState.Verified,
                            DateTimeOffset.UtcNow),
                        new(
                            FlightChecklistStepId.EngineStarted,
                            FlightChecklistPhase.EngineStart,
                            FlightChecklistVerificationCapability.AutoEvidence,
                            FlightChecklistStepState.Pending,
                            null)
                    ])
        };

        var viewModel =
            new ShellViewModel(
                new TestConnection(),
                new TestTelemetrySource(),
                settings,
                new FlightSessionCoordinator(),
                flightPersistence: null,
                checklistSnapshots: checklist);

        Assert.True(viewModel.IsChecklistVisible);
        Assert.Equal(
            "Current phase · Engine start",
            viewModel.ChecklistPhaseText);
        Assert.Equal(2, viewModel.ChecklistSteps.Count);
        Assert.Equal(
            "VERIFIED",
            viewModel.ChecklistSteps[0].Status);
        Assert.Equal(
            "PENDING",
            viewModel.ChecklistSteps[1].Status);

        await settings.UpdateAsync(
            settings.Current with
            {
                ShowChecklistEveryFlight = false
            });

        Assert.False(viewModel.IsChecklistVisible);
        Assert.Equal(2, viewModel.ChecklistSteps.Count);

        await settings.UpdateAsync(
            settings.Current with
            {
                ShowChecklistEveryFlight = true
            });

        Assert.True(viewModel.IsChecklistVisible);

        checklist.Publish(snapshot: null);

        Assert.False(viewModel.IsChecklistVisible);
        Assert.Equal(
            "No active checklist",
            viewModel.ChecklistPhaseText);
        Assert.Empty(viewModel.ChecklistSteps);
    }

    [Fact]
    public void MissingRuntimeIsDistinguishedFromWaitingForSimulator()
    {
        var connection = new TestConnection
        {
            Current = new SimulatorConnectionSnapshot(
                SimulatorConnectionState.WaitingForSimulator)
        };
        var viewModel = new ShellViewModel(
            connection,
            new TestTelemetrySource(),
            new TestSettingsService());

        viewModel.RefreshConnectionStatus();
        Assert.Equal("Waiting for MSFS 2024", viewModel.ConnectionStatus);

        connection.Current = new(
            SimulatorConnectionState.Unavailable,
            SimulatorConnectionIssue.RuntimeMissing);
        viewModel.RefreshConnectionStatus();

        Assert.Equal("Simulator connection unavailable", viewModel.ConnectionStatus);
        Assert.Contains("missing", viewModel.ConnectionDetail);
        Assert.False(viewModel.IsSimulatorConnected);
    }

    private static AircraftTelemetrySnapshot Snapshot(
        double altitudeMsl,
        double indicatedAirspeed,
        double verticalSpeed,
        bool paused) =>
        new(
            DateTimeOffset.UtcNow,
            43.23,
            -75.40,
            altitudeMsl,
            450,
            indicatedAirspeed,
            120,
            verticalSpeed,
            90,
            2,
            -1,
            1.02,
            false,
            false,
            1,
            300,
            400,
            10,
            true,
            paused,
            false);

    private sealed class TestConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; set; } =
            new(SimulatorConnectionState.Disconnected);

        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestTelemetrySource : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest { get; set; }
    }

    private sealed class TestFlightStore :
        IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            Checkpoint = session;
            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Checkpoint);

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            Checkpoint = null;
            return Task.CompletedTask;
        }
    }

    private sealed class TestChecklistSnapshotSource : IFlightChecklistSnapshotSource
    {
        public FlightChecklistSnapshot? Snapshot { get; set; }

        public bool IsActive => Snapshot is not null;
        public string? ActiveProfileId => IsActive ? "test" : null;
        public FlightChecklistSnapshot? Current => Snapshot;

        public event EventHandler<FlightChecklistSnapshotChangedEventArgs>? SnapshotChanged;

        public void Publish(FlightChecklistSnapshot? snapshot)
        {
            Snapshot = snapshot;
            SnapshotChanged?.Invoke(
                this,
                new FlightChecklistSnapshotChangedEventArgs(
                    ActiveProfileId,
                    Snapshot));
        }
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
