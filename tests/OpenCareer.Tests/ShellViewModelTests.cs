using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
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
    public async Task CareerCompletionActionEnablesOnlyWhenAuthoritativeInputsAreReady()
    {
        var action =
            new FakeCareerCompletionAction
            {
                Availability =
                    new(
                        CanComplete:
                            false,
                        CareerJobCompletionInputState.SettlementCostsUnavailable,
                        "Actual settlement costs are unavailable.")
            };

        var viewModel =
            new ShellViewModel(
                new TestConnection(),
                new TestTelemetrySource(),
                new TestSettingsService(),
                new FlightSessionCoordinator(),
                flightPersistence:
                    null,
                careerReadiness:
                    null,
                action,
                logger:
                    null);

        await viewModel.RefreshCareerCompletionActionAsync();

        Assert.False(
            viewModel.CanCompleteCareerFlight);
        Assert.Contains(
            "cost",
            viewModel.CareerCompletionActionDetail,
            StringComparison.OrdinalIgnoreCase);

        action.Availability =
            new(
                CanComplete:
                    true,
                CareerJobCompletionInputState.Ready,
                "Authoritative completion inputs are ready.");

        await viewModel.RefreshCareerCompletionActionAsync();

        Assert.True(
            viewModel.CanCompleteCareerFlight);
        Assert.Contains(
            "ready",
            viewModel.CareerCompletionActionDetail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CareerCompletionActionDelegatesOnceAndDisablesAfterSuccess()
    {
        var action =
            new FakeCareerCompletionAction
            {
                Availability =
                    new(
                        CanComplete:
                            true,
                        CareerJobCompletionInputState.Ready,
                        "Authoritative completion inputs are ready.")
            };

        var viewModel =
            new ShellViewModel(
                new TestConnection(),
                new TestTelemetrySource(),
                new TestSettingsService(),
                new FlightSessionCoordinator(),
                flightPersistence:
                    null,
                careerReadiness:
                    null,
                action,
                logger:
                    null);

        await viewModel.RefreshCareerCompletionActionAsync();
        Assert.True(
            viewModel.CanCompleteCareerFlight);

        await viewModel.CompleteCareerFlightAsync();

        Assert.Equal(
            1,
            action.CompleteCount);
        Assert.False(
            viewModel.CanCompleteCareerFlight);
        Assert.False(
            viewModel.IsCareerCompletionBusy);
        Assert.Contains(
            "completed",
            viewModel.CareerCompletionActionDetail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CareerAbandonActionUsesVerifiedIdentityAndDisablesAfterSuccess()
    {
        Guid sessionId =
            Guid.Parse(
                "aaaaaaaa-0000-0000-0000-000000000001");
        Guid contractId =
            Guid.Parse(
                "aaaaaaaa-0000-0000-0000-000000000002");

        var action =
            new FakeCareerAbandonAction
            {
                Availability =
                    new(
                        CanAbandon: true,
                        CareerFlightAbandonAvailabilityState.Ready,
                        sessionId,
                        contractId,
                        "The active career flight can be abandoned.")
            };

        var viewModel =
            new ShellViewModel(
                new TestConnection(),
                new TestTelemetrySource(),
                new TestSettingsService(),
                new FlightSessionCoordinator(),
                flightPersistence: null,
                careerReadiness: null,
                careerCompletionAction: null,
                careerAbandonAction: action,
                logger: null);

        await viewModel.RefreshCareerAbandonActionAsync();

        Assert.True(viewModel.CanAbandonCurrentFlight);

        Assert.True(
            await viewModel.AbandonCurrentFlightAsync());

        Assert.Equal(1, action.AbandonCount);
        Assert.Equal(sessionId, action.LastSessionId);
        Assert.Equal(contractId, action.LastContractId);
        Assert.False(viewModel.CanAbandonCurrentFlight);
        Assert.False(viewModel.IsCareerAbandonBusy);
        Assert.Contains(
            "no completion rewards",
            viewModel.CareerAbandonActionDetail,
            StringComparison.OrdinalIgnoreCase);
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

    private sealed class FakeCareerCompletionAction
        : ICareerJobCompletionAction
    {
        public CareerJobCompletionActionAvailability Availability { get; set; } =
            new(
                CanComplete:
                    false,
                CareerJobCompletionInputState.NoCareerFlight,
                "No career flight.");

        public int CompleteCount { get; private set; }

        public Task<CareerJobCompletionActionAvailability> ReadAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Availability);
        }

        public Task CompleteAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeCareerAbandonAction
        : ICareerFlightAbandonAction
    {
        public CareerFlightAbandonAvailability Availability { get; set; } =
            new(
                CanAbandon: false,
                CareerFlightAbandonAvailabilityState.Unavailable,
                SessionId: null,
                ContractId: null,
                "No career flight.");

        public int AbandonCount { get; private set; }
        public Guid? LastSessionId { get; private set; }
        public Guid? LastContractId { get; private set; }

        public Task<CareerFlightAbandonAvailability> ReadAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Availability);
        }

        public Task<CareerFlightAbandonResult> AbandonAsync(
            Guid expectedSessionId,
            Guid expectedContractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AbandonCount++;
            LastSessionId = expectedSessionId;
            LastContractId = expectedContractId;

            return Task.FromResult(
                new CareerFlightAbandonResult(
                    CareerFlightAbandonStatus.Abandoned,
                    expectedSessionId,
                    expectedContractId,
                    SessionWasAlreadyCancelled: false,
                    ContractWasAlreadyCancelled: false,
                    ReservationWasAlreadyReleased: false));
        }
    }

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
