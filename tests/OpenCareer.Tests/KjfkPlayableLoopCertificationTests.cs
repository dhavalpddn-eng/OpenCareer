using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;
using OpenCareer.Domain.Telemetry;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.Infrastructure.Airports;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;
using OpenCareer.SimConnect;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

/// <summary>
/// Pre-simulator release gate. Only the simulator transport, normalized observations,
/// clock and preferences are fixtures. All career authorities and stores are production.
/// </summary>
public sealed class KjfkPlayableLoopCertificationTests
{
    private const string AircraftId = AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId;

    [Fact]
    public async Task LiveC172TrafficSurvivesMissingHeartbeatAndSilentReconnectRestoresReadyWithoutReselection()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        var transport = new SimConnectTestTransport();
        var connectionClock = new ConnectionClock();
        transport.Enqueue(SimConnectPackets.Open());
        await using var connection = new SimConnectConnection(transport,
            NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions
            {
                DispatchInterval = TimeSpan.FromMilliseconds(5),
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
            }, connectionClock);
        var live = new SimConnectInstalledAircraftObservationSource(connection);
        var discovery = new CompositeInstalledAircraftDiscoverySource(new SwitchableDiscovery(null), live);
        byte[] title = SimConnectPackets.StringSimObjectData(SimConnectCurrentAircraftDefinition.RequestId,
            SimConnectCurrentAircraftDefinition.DefinitionId, "C172SP Classic Passengers");
        try
        {
            connection.Start();
            await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
            transport.Enqueue(title);
            await Until(() => live.Current.Availability == InstalledAircraftDiscoveryAvailability.Available);
            var clock = new TestClock();
            var app = new Harness(Path.Combine(root, "career.db"), discovery, connection, new TestTelemetry(), clock);
            await app.Profiles.SaveAsync(PlayerCareerProfile.Start(Guid.NewGuid(), "KJFK", clock.Now),
                expectedRevision: null, savedAt: clock.Now);
            await app.InitializeAsync();
            var offer = await app.Development.GenerateAsync();
            await app.Jobs.RefreshAsync();
            await app.Jobs.SelectAircraftAsync(AircraftId);

            transport.Enqueue(action: () => connectionClock.Advance(TimeSpan.FromSeconds(6)));
            await Until(() => transport.Heartbeats.Count == 1);
            // Omit the heartbeat reply for >35 seconds while TITLE and normalized telemetry flow.
            for (int i = 0; i < 6; i++)
            {
                var values = new double[SimConnectTelemetryDefinition.ValueCount];
                values[(int)SimConnectTelemetryValue.HeadingTrue] = i + 1;
                transport.Enqueue(title, action: () => connectionClock.Advance(TimeSpan.FromSeconds(6)));
                transport.Enqueue(SimConnectPackets.SimObjectData(SimConnectTelemetryDefinition.RequestId,
                    SimConnectTelemetryDefinition.DefinitionId, values));
                await Until(() => connection.Latest?.HeadingDegrees == i + 1 || transport.Attempts > 1);
                Assert.Equal(1, transport.Attempts);
                Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
                await app.Jobs.RefreshAircraftAndReadinessAsync();
                Assert.Equal(AircraftId, app.Jobs.SelectedAircraftId);
                Assert.Equal("C172SP Classic Passengers", app.Jobs.SelectedAircraftOption!.DisplayName);
                var ready = Assert.Single(app.Jobs.Offers);
                Assert.Equal(CareerJobStartInputState.Ready, ready.StartInputState);
                Assert.Equal("READY TO START", ready.AvailabilityText);
                Assert.True(ready.CanStart);
            }

            // Now stop all inbound traffic: true silence still closes the connection.
            transport.Enqueue(action: () => connectionClock.Advance(TimeSpan.FromSeconds(31)));
            await Until(() => transport.Attempts == 2);
            Assert.Equal(SimulatorConnectionState.Connecting, connection.Current.State);
            Assert.Equal(InstalledAircraftDiscoveryAvailability.Unavailable, discovery.Current.Availability);
            int reads = app.RegistryReadCount;
            var selected = app.Jobs.SelectedAircraftOption;
            await app.Jobs.RefreshAircraftAndReadinessAsync();
            Assert.Equal(AircraftId, app.Jobs.SelectedAircraftId);
            Assert.Same(selected, app.Jobs.SelectedAircraftOption);
            Assert.False(Assert.Single(app.Jobs.Offers).CanStart);
            await app.Jobs.StartOfferAsync(offer.OfferId);
            Assert.Equal(reads, app.RegistryReadCount);
            Assert.Null(await app.ContractStore.ReadJobContractAsync(offer.OfferId));
            Assert.Null(await app.ReservationAsync(offer.OfferId));
            Assert.Null(app.Sessions.Current);

            transport.Enqueue(SimConnectPackets.Open());
            await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
            await app.Jobs.RefreshAircraftAndReadinessAsync();
            Assert.False(Assert.Single(app.Jobs.Offers).CanStart); // connected alone is not fresh installation evidence
            transport.Enqueue(title);
            await Until(() => live.Current.Availability == InstalledAircraftDiscoveryAvailability.Available);
            await app.Jobs.RefreshAircraftAndReadinessAsync();
            Assert.Equal(AircraftId, app.Jobs.SelectedAircraftId); // no SelectAircraftAsync after reconnection
            Assert.True(Assert.Single(app.Jobs.Offers).CanStart);
            await app.Jobs.StartOfferAsync(offer.OfferId);
            Assert.Equal(ContractStatus.InProgress, app.Contracts.Find(offer.OfferId)!.Contract.Status);
            Assert.Equal(offer.OfferId, app.Session.ContractId);
            Assert.NotNull(await app.ReservationAsync(offer.OfferId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ConnectionClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    [Fact]
    public async Task StockC172RefreshReadinessSurvivesProviderChangesButActualPickerRemovalBlocksStart()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        var transport = new SimConnectTestTransport();
        transport.Enqueue(SimConnectPackets.Open());
        await using var connection = new SimConnectConnection(transport,
            NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions { DispatchInterval = TimeSpan.FromMilliseconds(5) }, TimeProvider.System);
        var live = new SimConnectInstalledAircraftObservationSource(connection);
        var filteredLive = new SwitchableDiscovery(live);
        var packages = new SwitchableDiscovery(null);
        var discovery = new CompositeInstalledAircraftDiscoverySource(packages, filteredLive);
        try
        {
            connection.Start();
            await Until(() => transport.StringDataDefinitions.Any(
                item => item.DefinitionId == SimConnectCurrentAircraftDefinition.DefinitionId));
            transport.Enqueue(SimConnectPackets.StringSimObjectData(SimConnectCurrentAircraftDefinition.RequestId,
                SimConnectCurrentAircraftDefinition.DefinitionId, "C172SP Classic Passengers"));
            await Until(() => live.Current.Availability == InstalledAircraftDiscoveryAvailability.Available);

            var clock = new TestClock();
            var app = new Harness(Path.Combine(root, "career.db"), discovery, connection, new TestTelemetry(), clock,
                () => filteredLive.OmitAircraft = true);
            await app.Profiles.SaveAsync(PlayerCareerProfile.Start(Guid.NewGuid(), "KJFK", clock.Now),
                expectedRevision: null, savedAt: clock.Now);
            await app.InitializeAsync();
            var offer = await app.Development.GenerateAsync();
            await app.Jobs.RefreshAsync();
            Assert.Equal("C172SP Classic Passengers", Assert.Single(app.Jobs.AircraftOptions).DisplayName);
            await app.Jobs.SelectAircraftAsync(AircraftId);

            for (int i = 0; i < 5; i++)
            {
                filteredLive.OmitAircraft = false; // present when the picker takes its discovery snapshot
                filteredLive.ReportUnavailable = i % 2 == 0;
                int reads = app.RegistryReadCount;
                var oldOption = app.Jobs.SelectedAircraftOption;
                await app.Jobs.RefreshAircraftAndReadinessAsync(); // live evidence drops after registry resolution
                Assert.Equal(1, app.RegistryReadCount - reads);
                Assert.True(filteredLive.OmitAircraft);
                Assert.Equal(filteredLive.ReportUnavailable ? InstalledAircraftDiscoveryAvailability.Unavailable
                    : InstalledAircraftDiscoveryAvailability.Available, discovery.Current.Availability);
                Assert.Empty(discovery.Current.Observations); // the package provider cannot prove live removal
                Assert.Equal(AircraftId, app.Jobs.SelectedAircraftId);
                Assert.NotSame(oldOption, app.Jobs.SelectedAircraftOption);
                Assert.Same(Assert.Single(app.Jobs.AircraftOptions), app.Jobs.SelectedAircraftOption);
                var ready = Assert.Single(app.Jobs.Offers);
                Assert.Equal(CareerJobStartInputState.Ready, ready.StartInputState);
                Assert.Equal("READY TO START", ready.AvailabilityText);
                Assert.True(ready.CanStart, ready.ActionText);
                var retained = await new SqliteInstalledAircraftRegistryStore(app.DatabasePath).FindAsync(AircraftId);
                Assert.Equal(AircraftId, Assert.Single(retained).CanonicalAircraftId);
            }

            // Current discovery, not persisted installation history, controls selectable options.
            filteredLive.ReportUnavailable = false; // every provider confirms authoritative empty
            await app.Jobs.RefreshAircraftAndReadinessAsync();
            Assert.Empty(app.Jobs.AircraftOptions);
            Assert.Null(app.Jobs.SelectedAircraftId);
            Assert.Null(app.Jobs.SelectedAircraftOption);
            Assert.False(Assert.Single(app.Jobs.Offers).CanStart);
            filteredLive.OmitAircraft = false;
            await app.Jobs.RefreshAircraftAndReadinessAsync();
            Assert.Null(app.Jobs.SelectedAircraftId); // no automatic reselection after genuine removal
            await app.Jobs.SelectAircraftAsync(AircraftId);
            Assert.True(Assert.Single(app.Jobs.Offers).CanStart);
            filteredLive.OmitAircraft = false;
            await app.Jobs.StartOfferAsync(offer.OfferId);
            Assert.Equal(ContractStatus.InProgress, app.Contracts.Find(offer.OfferId)!.Contract.Status);
            Assert.Equal(offer.OfferId, app.Session.ContractId);
            Assert.NotNull(await app.ReservationAsync(offer.OfferId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SwitchableDiscovery(IInstalledAircraftDiscoverySource? source) : IInstalledAircraftDiscoverySource
    {
        public bool OmitAircraft { get; set; }
        public bool ReportUnavailable { get; set; }
        public InstalledAircraftDiscoverySnapshot Current => source is not null && !OmitAircraft
            ? source.Current
            : ReportUnavailable ? InstalledAircraftDiscoverySnapshot.Unavailable
            : new(InstalledAircraftDiscoveryAvailability.Available, []);
    }

    private sealed class ObservedRegistry(IAircraftRegistrySource source, Action? afterResolution) : IAircraftRegistrySource
    {
        public int ReadCount { get; private set; }
        public async Task<AircraftRegistryResolution?> FindAircraftAsync(string aircraftId, CancellationToken cancellationToken = default)
        {
            var result = await source.FindAircraftAsync(aircraftId, cancellationToken);
            ReadCount++;
            afterResolution?.Invoke();
            return result;
        }
    }

    [Fact]
    public async Task StockC172CircuitBouncesCompletesRecoversAndCanBeAbandonedAndRepeated()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        var transport = new SimConnectTestTransport();
        transport.Enqueue(SimConnectPackets.Open());
        await using var connection = new SimConnectConnection(
            transport, NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions
            {
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(10),
                DispatchInterval = TimeSpan.FromMilliseconds(5)
            }, TimeProvider.System);
        var discovery = new SimConnectInstalledAircraftObservationSource(connection);
        try
        {
            connection.Start();
            await Until(() => transport.StringDataDefinitions.Any(
                item => item.DefinitionId == SimConnectCurrentAircraftDefinition.DefinitionId));
            // The live stock-aircraft case: no catalog reply and no aircraft.cfg.
            transport.Enqueue(SimConnectPackets.StringSimObjectData(
                SimConnectCurrentAircraftDefinition.RequestId,
                SimConnectCurrentAircraftDefinition.DefinitionId, "C172SP Classic Passengers"));
            await Until(() => discovery.Current.Availability == InstalledAircraftDiscoveryAvailability.Available);
            Assert.Equal(AircraftId, Assert.Single(discovery.Current.Observations).CanonicalAircraftId);

            var clock = new TestClock();
            var telemetry = new TestTelemetry();
            var app = new Harness(Path.Combine(root, "career.db"), discovery, connection, telemetry, clock);
            await app.Profiles.SaveAsync(PlayerCareerProfile.Start(Guid.NewGuid(), "KJFK", clock.Now),
                expectedRevision: null, savedAt: clock.Now);
            await app.InitializeAsync();
            var before = (await app.Profiles.LoadAsync())!;
            decimal cashBefore = await app.Ledger.ReadCashBalanceAsync();

            Guid first = await app.GenerateAndStartAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Development.GenerateAsync());
            Assert.False((await app.Completion.ReadAvailabilityAsync()).CanComplete);
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Completion.CompleteAsync());
            Assert.Equal(0, await app.RowCountAsync("economy_ledger_transactions"));

            await app.TakeoffAsync();
            Assert.Equal(1, app.Session.Tracking.TakeoffCount);
            Assert.True(app.Session.TimeLedger.AirborneTime > TimeSpan.Zero);

            // A real restart: reconstruct all services and stores, recover checkpoint and contract,
            // then resume only after fresh plausible telemetry. The reservation stays contract-owned.
            await app.Persistence.FlushAsync();
            app = new Harness(app.DatabasePath, discovery, connection, telemetry, clock);
            await app.InitializeAsync();
            Assert.Equal(FlightSessionStatus.Suspended, app.Session.Status);
            Assert.Equal(first, app.Session.SessionId);
            Assert.Equal(ContractStatus.InProgress, app.Contracts.Find(first)!.Contract.Status);
            Assert.NotNull(await app.ReservationAsync(first));
            await app.SamplesAsync(4, false, 600, 80);
            Assert.Equal(FlightSessionStatus.Active, app.Session.Status);
            Assert.Equal(first, app.Session.SessionId);

            await app.SamplesAsync(3, false, 100, 65, descent: -400);
            await app.SamplesAsync(1, true, 0, 55);
            Assert.Equal(FlightSessionStatus.Active, app.Session.Status);
            Assert.True(app.Session.ContinuityAnchor!.OnGround);
            // First contact is provisional. A short airborne excursion must not poison continuity.
            await app.SamplesAsync(1, false, 8, 54);
            await app.SamplesAsync(2, true, 0, 50);
            Assert.Equal(FlightTrackingState.LandingEpisode, app.Session.Tracking.State);
            Assert.Equal(1, app.Session.Tracking.LandingEpisodeCount);
            Assert.Equal(1, app.Session.Tracking.BounceCount);
            // A second short bounce stays in this episode, including duplicate telemetry delivery.
            await app.SamplesAsync(2, false, 18, 48);
            await app.SamplesAsync(2, true, 0, 20);
            Assert.Equal(2, app.Session.Tracking.BounceCount);
            Assert.False(await app.Runtime.RefreshAsync());
            Assert.Equal(2, app.Session.Tracking.BounceCount);
            Assert.Equal(1, app.Session.Tracking.LandingEpisodeCount);
            Assert.Single(app.Session.EffectiveLandingEpisodes);
            Assert.Equal(first, app.Session.SessionId);
            Assert.Equal(first, app.Session.ContractId);
            Assert.Equal(AircraftId, (await app.ReservationAsync(first))!.CanonicalAircraftId);
            Assert.False((await app.Completion.ReadAvailabilityAsync()).CanComplete);

            await app.SamplesAsync(3, true, 0, 15);
            Assert.Equal(FlightOperationState.TaxiIn, app.Session.OperationState);
            await app.SamplesAsync(2, true, 0, 0, parking: true);
            Assert.Equal(FlightOperationState.Parked, app.Session.OperationState);
            Assert.False((await app.Completion.ReadAvailabilityAsync()).CanComplete);
            await app.SamplesAsync(2, true, 0, 0, engines: 0, parking: true);
            Assert.Equal(FlightOperationState.Shutdown, app.Session.OperationState);
            Assert.True(app.Evidence.Current!.CoreFlightSequenceObserved);
            var completionInput = await app.CompletionInputs.ReadCurrentAsync();
            Assert.True(completionInput.IsReady, completionInput.Detail);
            await app.Shell.RefreshCareerCompletionActionAsync();
            Assert.True(app.Shell.CanCompleteCareerFlight);
            Assert.Equal("Complete Career Flight", app.Shell.CareerCompletionActionText);

            // Inspect durable state at the actual checkpoint-delete boundary, without replacing stores.
            app.Checkpoints.BeforeClear = async terminal =>
            {
                Assert.Equal(FlightSessionStatus.Completed, terminal.Status);
                Assert.Equal(ContractStatus.Completed, (await app.ContractStore.ReadJobContractAsync(first))!.Contract.Status);
                Assert.Null(await app.ReservationAsync(first));
                Assert.Equal(1, await app.RowCountAsync("economy_ledger_transactions"));
                Assert.Equal(1, await app.RowCountAsync("logbook_entries"));
                Assert.Single((await app.Profiles.LoadAsync())!.Profile.AppliedExperienceDebriefIds);
            };
            await app.Shell.CompleteCareerFlightAsync(); // actual completion action + terminal workflow
            Assert.Contains("Career flight completed.", app.Shell.CareerCompletionActionDetail);
            Assert.Null(app.Sessions.Current);
            Assert.Null(await app.Checkpoints.LoadAsync());
            Assert.False(app.Shell.HasFlightSession);
            Assert.Equal(1, app.Checkpoints.ClearCount);
            Assert.Null(await app.ReservationAsync(first));
            Assert.False((await app.Completion.ReadAvailabilityAsync()).CanComplete);

            var completed = (await app.ContractStore.ReadJobContractAsync(first))!.Contract;
            Assert.Equal(ContractStatus.Completed, completed.Status);
            Assert.Equal(0m, completed.Compensation.PilotCompensation);
            Assert.Equal(0, completed.ReputationReward);
            Assert.Equal(0, completed.ReputationPenalty);
            var transaction = Assert.Single(await app.Ledger.ReadRecentAsync(20));
            Assert.Equal(0m, transaction.TotalDebits);
            Assert.Equal(0m, transaction.TotalCredits);
            Assert.Equal(cashBefore, await app.Ledger.ReadCashBalanceAsync());
            string key = LogbookCommitCoordinator.BuildCareerSettlementIdempotencyKey(
                ContractSettlementEngine.GetIdempotencyKey(first));
            var log = (await app.Logbook.FindByIdempotencyKeyAsync(key))!;
            Assert.Equal(first, log.Debrief.ContractId);
            Assert.Equal(0, log.Debrief.Settlement.ReputationDelta);
            var after = (await app.Profiles.LoadAsync())!;
            Assert.Equal(before.Profile.Experience, after.Profile.Experience);
            Assert.Equal(before.Profile.Qualifications, after.Profile.Qualifications);
            Assert.Equal("KJFK", after.Profile.Location.CurrentAirportIcao);
            Assert.True(after.SavedAt >= log.CommittedAt);

            // Repeat the authoritative request, then recreate services and replay again.
            for (int i = 0; i < 5; i++)
            {
                var replay = await app.Loop.CompleteAsync(completionInput.Request!);
                Assert.False(replay.Terminal.Settlement.WasNewlyPosted);
                Assert.Equal(0m, replay.Terminal.Settlement.Settlement.GrossCashReceipt);
                Assert.Equal(0m, replay.Terminal.Settlement.Settlement.PlayerOperatingCosts);
                Assert.Equal(0m, replay.Terminal.Settlement.Settlement.NetCashChange);
                Assert.Equal(LogbookAppendDisposition.AlreadyExists, replay.Terminal.Logbook.Disposition);
            }
            app = new Harness(app.DatabasePath, discovery, connection, telemetry, clock);
            await app.InitializeAsync();
            await app.Loop.CompleteAsync(completionInput.Request!);
            Assert.Null(app.Sessions.Current);
            Assert.Equal(1, await app.RowCountAsync("economy_ledger_transactions"));
            Assert.Equal(1, await app.RowCountAsync("logbook_entries"));
            Assert.Equal(after.Revision, (await app.Profiles.LoadAsync())!.Revision);
            Assert.Null(await app.ReservationAsync(first));

            Guid second = await app.GenerateAndStartAsync();
            Assert.NotEqual(first, second);
            await app.TakeoffAsync();
            // A distant relocation suspends, then stable incompatible reconnect evidence interrupts.
            await app.SamplesAsync(4, false, 600, 80, latitude: 10);
            Assert.Equal(FlightSessionStatus.Interrupted, app.Session.Status);
            Assert.Equal(FlightSessionStatus.Interrupted, (await app.Checkpoints.LoadAsync())!.Status);
            Assert.False((await app.Completion.ReadAvailabilityAsync()).CanComplete);
            await app.Shell.RefreshCareerAbandonActionAsync();
            Assert.True(app.Shell.CanAbandonCurrentFlight);
            Assert.Contains("interrupted", app.Shell.CareerAbandonActionDetail, StringComparison.OrdinalIgnoreCase);
            app.Checkpoints.BeforeClear = async terminal =>
            {
                Assert.Equal(FlightSessionStatus.Interrupted, terminal.Status);
                Assert.Equal(ContractStatus.Cancelled, (await app.ContractStore.ReadJobContractAsync(second))!.Contract.Status);
                Assert.Null(app.Contracts.Find(second));
                Assert.Null(await app.ReservationAsync(second));
                Assert.Equal(1, await app.RowCountAsync("economy_ledger_transactions"));
                Assert.Equal(1, await app.RowCountAsync("logbook_entries"));
            };
            // Fail the final cleanup once to prove persisted cancellation/release can be retried.
            app.Checkpoints.FailNextClear = true;
            Assert.False(await app.Shell.AbandonCurrentFlightAsync());
            Assert.Equal(FlightSessionStatus.Interrupted, (await app.Checkpoints.LoadAsync())!.Status);
            Assert.True(await app.Shell.AbandonCurrentFlightAsync());
            Assert.Null(await app.Checkpoints.LoadAsync());
            Assert.Equal(1, app.Checkpoints.ClearCount);
            Assert.Equal(CareerFlightAbandonStatus.NoActiveSession,
                (await app.Abandon.AbandonAsync(second, second)).Status);
            Assert.Equal(after.Revision, (await app.Profiles.LoadAsync())!.Revision);
            Assert.Equal(cashBefore, await app.Ledger.ReadCashBalanceAsync());
            await app.Jobs.RefreshAsync();
            Guid third = await app.GenerateAndStartAsync();
            Assert.NotEqual(second, third);
            Assert.Equal(FlightSessionStatus.Active, app.Session.Status);
            Assert.NotNull(await app.ReservationAsync(third));
            app.Checkpoints.BeforeClear = terminal =>
            {
                Assert.Equal(FlightSessionStatus.Cancelled, terminal.Status);
                return Task.CompletedTask;
            };
            // Active-abandon regression through the same UI/application action.
            await app.Shell.RefreshCareerAbandonActionAsync();
            Assert.True(await app.Shell.AbandonCurrentFlightAsync());
            Assert.Equal(ContractStatus.Cancelled, (await app.ContractStore.ReadJobContractAsync(third))!.Contract.Status);
            Assert.Null(await app.Checkpoints.LoadAsync());
            Assert.Equal(1, await app.RowCountAsync("logbook_entries"));
            Assert.Equal(1, await app.RowCountAsync("economy_ledger_transactions"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProductionUiBindingsAndServiceRegistrationsReachTheCertifiedAuthorities()
    {
        string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiContracts", name));
        var jobs = System.Xml.Linq.XDocument.Parse(Read("JobsPage.xaml"));
        var aircraftPicker = Assert.Single(jobs.Descendants(), e => e.Name.LocalName == "ComboBox"
            && (string?)e.Attribute("ItemsSource") == "{Binding AircraftOptions}");
        Assert.Equal("AircraftId", (string?)aircraftPicker.Attribute("SelectedValuePath"));
        Assert.Equal("{Binding SelectedAircraftId, Mode=OneWay}", (string?)aircraftPicker.Attribute("SelectedValue"));
        Assert.Null(aircraftPicker.Attribute("SelectedItem"));
        Assert.Contains((string)aircraftPicker.Attribute("SelectionChanged")!, Read("JobsPage.xaml.cs"));
        Assert.Contains("SelectAircraftAsync", Read("JobsPage.xaml.cs"));
        Assert.Contains("RefreshAircraftAndReadinessAsync", Read("JobsPage.xaml.cs"));
        Assert.Contains("TimeSpan.FromSeconds(2)", Read("JobsPage.xaml.cs"));
        Assert.Equal("AircraftPicker", (string?)aircraftPicker.Attribute(
            System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")));
        Assert.Contains("AircraftPicker.SelectedItem = viewModel.SelectedAircraftOption", Read("JobsPage.xaml.cs"));
        Assert.Contains("SynchronizeAircraftSelection(viewModel)", Read("JobsPage.xaml.cs"));
        string jobsCode = Read("JobsPage.xaml.cs");
        foreach (string method in new[] { "RefreshAsync", "RefreshAircraftAndReadinessAsync", "SelectAircraftAsync" })
            Assert.Matches(@"\." + method + @"\([^;]+;\s*SynchronizeAircraftSelection\(viewModel\);", jobsCode);
        Assert.Contains("if (_synchronizingAircraftSelection", jobsCode);
        Assert.Matches(@"_synchronizingAircraftSelection = true;\s*try\s*\{\s*AircraftPicker.SelectedItem = viewModel.SelectedAircraftOption;\s*\}\s*finally\s*\{\s*_synchronizingAircraftSelection = false;", jobsCode);
        var start = Assert.Single(jobs.Descendants(), e => e.Name.LocalName == "Button"
            && (string?)e.Attribute("Content") == "Accept & Start Flight");
        Assert.Equal("{Binding CanStart}", (string?)start.Attribute("IsEnabled"));
        Assert.Contains("StartOfferAsync", Read("JobsPage.xaml.cs"));
        Assert.Contains((string)start.Attribute("Click")!, Read("JobsPage.xaml.cs"));
        var current = System.Xml.Linq.XDocument.Parse(Read("CurrentFlightPage.xaml"));
        foreach (var binding in new[] { ("CareerCompletionActionText", "CanCompleteCareerFlight", "CompleteCareerFlightAsync"),
            ("CareerAbandonActionText", "CanAbandonCurrentFlight", "AbandonCurrentFlightAsync") })
        {
            var button = Assert.Single(current.Descendants(), e => e.Name.LocalName == "Button"
                && (string?)e.Attribute("Content") == "{Binding " + binding.Item1 + "}");
            Assert.Equal("{Binding " + binding.Item2 + "}", (string?)button.Attribute("IsEnabled"));
            Assert.Contains((string)button.Attribute("Click")!, Read("CurrentFlightPage.xaml.cs"));
            Assert.Contains(binding.Item3, Read("CurrentFlightPage.xaml.cs"));
        }
        Assert.Contains("answer != ContentDialogResult.Primary", Read("CurrentFlightPage.xaml.cs"));
        Assert.Contains("jobs.RefreshAsync", Read("CurrentFlightPage.xaml.cs"));
        string registration = Read("App.xaml.cs");
        foreach (string authority in new[] { "CareerJobStartActionService", "CareerJobCompletionActionService",
            "CareerJobPlayableLoopCoordinator", "CareerFlightAbandonCoordinator", "FlightSessionRuntime",
            "SqliteJobContractStore", "SqliteEconomyLedgerStore", "SqliteLogbookStore" })
            Assert.Contains(authority, registration);
        Assert.Contains("PlayableLoopReferenceAircraftObservationSource", Read("PlanningServiceRegistration.cs"));
        Assert.Contains("PlayableLoopReferenceAirportObservationSource", Read("PlanningServiceRegistration.cs"));
    }

    private sealed class Harness
    {
        public string DatabasePath { get; }
        public TestClock Clock { get; }
        public TestTelemetry Telemetry { get; }
        public SqlitePlayerCareerProfileStore Profiles { get; }
        public SqliteJobContractStore ContractStore { get; }
        public JobContractRuntimeState Contracts { get; }
        public SqliteAircraftAvailabilityStore Fleet { get; }
        public SqliteEconomyLedgerStore Ledger { get; }
        public SqliteLogbookStore Logbook { get; }
        public FlightSessionCoordinator Sessions { get; } = new();
        public FlightSession Session => Assert.IsType<FlightSession>(Sessions.Current);
        public ObservedCheckpointStore Checkpoints { get; }
        public FlightSessionPersistenceService Persistence { get; }
        public FlightSessionRuntime Runtime { get; }
        public JobFlightCompletionEvidenceTracker Evidence { get; }
        public DevelopmentFlightService Development { get; }
        public CareerJobCompletionInputSource CompletionInputs { get; }
        public CareerJobCompletionActionService Completion { get; }
        public CareerFlightAbandonCoordinator Abandon { get; }
        public CareerJobPlayableLoopCoordinator Loop { get; }
        public JobsViewModel Jobs { get; }
        public ShellViewModel Shell { get; }
        private readonly PlayerCareerRuntimeState _career;
        private readonly ObservedRegistry _registry;
        public int RegistryReadCount => _registry.ReadCount;

        public Harness(string path, IInstalledAircraftDiscoverySource discovery,
            ISimulatorConnection connection, TestTelemetry telemetry, TestClock clock, Action? afterAircraftResolution = null)
        {
            DatabasePath = path; Clock = clock; Telemetry = telemetry;
            var options = new OpenCareerDatabaseOptions(path);
            Profiles = new(options, NullLogger<SqlitePlayerCareerProfileStore>.Instance);
            ContractStore = new(options, NullLogger<SqliteJobContractStore>.Instance);
            var recoverySource = new SqliteJobContractRecoverySource(options, NullLogger<SqliteJobContractRecoverySource>.Instance);
            Contracts = new(new JobContractRecoveryService(recoverySource, ContractStore));
            var lifecycle = new JobContractLifecycleService(ContractStore, Contracts);
            var boards = new SqliteJobBoardStateStore(options, NullLogger<SqliteJobBoardStateStore>.Instance);
            _career = new(Profiles);
            var location = new PlayerCareerLocationCoordinator(Profiles, _career);
            Fleet = new(path);
            Ledger = new(options, NullLogger<SqliteEconomyLedgerStore>.Instance);
            Logbook = new(options, NullLogger<SqliteLogbookStore>.Instance);
            Checkpoints = new(new SqliteFlightSessionCheckpointStore(path));
            Persistence = new(Sessions, Checkpoints);
            Runtime = new(Sessions, Persistence, new FlightTelemetryEvidenceProcessor(),
                new FlightContinuityPolicy(), connection, telemetry, clock);
            Evidence = new(Sessions, Runtime);
            var installed = new PersistentInstalledAircraftObservationSource(discovery, new SqliteInstalledAircraftRegistryStore(path));
            _registry = new(new AircraftRegistryCatalogService([installed, new PlayableLoopReferenceAircraftObservationSource()]),
                afterAircraftResolution);
            var airports = new CachedAirportDataSource([new PlayableLoopReferenceAirportObservationSource(clock)], clock: clock);
            var dispatch = new OperationDispatchPlanningService(_registry, airports, aircraftAvailability: Fleet);
            var acceptance = new JobOfferAcceptanceService(ContractStore, lifecycle, boards, Contracts);
            var fleetBridge = new JobAcceptanceFleetBridge(acceptance, ContractStore, new AircraftReservationCoordinator(_registry, Fleet));
            var terminal = new CareerFlightTerminalWorkflowCoordinator(
                new SettlementPendingContractCoordinator(new EconomySettlementService(ContractStore, Ledger)),
                new SettledJobLogbookCoordinator(Sessions, new LogbookCommitCoordinator(Logbook)), Logbook,
                new CareerLogbookExperienceCoordinator(new PlayerCareerExperienceCoordinator(Profiles, _career, ContractStore)),
                location, new CareerFlightFinalizationCoordinator(new CareerFlightReservationReleaseCoordinator(Fleet, Fleet), Persistence, Sessions));
            Loop = new(new AcceptedJobDispatchBridge(fleetBridge, dispatch),
                new AcceptedJobFlightSessionBridge(new AcceptedJobStartBridge(lifecycle), ContractStore, Persistence, Sessions),
                new JobFlightSessionCompletionBridge(Evidence, Sessions, new FlightSessionCompletionService(Sessions, Persistence)),
                new CompletedJobContractBridge(ContractStore, lifecycle, Sessions), ContractStore, terminal);
            var inputs = new CareerJobStartInputSource(boards, _career, ContractStore, _registry, dispatch,
                [new PersistedJobContractTermsSource()], [new StandardCivilianPointToPointDispatchAuthoritySource()], clock);
            Development = new(new JobBoardGenerationService(boards), _career, recoverySource, Checkpoints, clock, location);
            Jobs = new(boards, _career, clock, new CareerJobAircraftSelectionSource(discovery),
                new CareerJobStartActionService(inputs, Loop), NullLogger<JobsViewModel>.Instance,
                developmentFlights: Development);
            CompletionInputs = new(Sessions, Contracts, Evidence, Fleet, _registry,
                [new StandardPointToPointMissionCompletionSource(airports, StandardPointToPointMissionPolicy.Default)],
                [new PersistedFlightSettlementCostsSource([new EmployerCoveredOperatingCostQuoteSource()])]);
            Completion = new(CompletionInputs, Loop);
            Abandon = new(Sessions, Persistence, ContractStore, lifecycle, Contracts, Fleet, Fleet, clock,
                NullLogger<CareerFlightAbandonCoordinator>.Instance);
            Shell = new(connection, telemetry, new TestSettings(), Sessions, Persistence,
                new CareerJobPlayableLoopReadinessSource(Sessions, Contracts, Evidence), Completion, Abandon,
                NullLogger<ShellViewModel>.Instance);
        }

        public async Task InitializeAsync()
        {
            await _career.InitializeAsync();
            await Contracts.InitializeAsync();
            await Persistence.RecoverAsync();
        }

        public Task<AircraftReservationOwnership?> ReservationAsync(Guid contractId) =>
            Fleet.FindByReservationIdAsync(JobAcceptanceFleetBridge.GetReservationId(contractId));

        public async Task<Guid> GenerateAndStartAsync()
        {
            Clock.Now = Clock.Now.AddSeconds(1);
            var offer = await Development.GenerateAsync();
            Assert.Equal(offer.OfferId, (await Development.GenerateAsync()).OfferId);
            Assert.True(DevelopmentFlight.IsDevelopment(offer));
            Assert.Equal("KJFK", offer.OriginIcao);
            Assert.Equal("KJFK", offer.DestinationIcao);
            await Jobs.RefreshAsync();
            var aircraft = Assert.Single(Jobs.AircraftOptions);
            Assert.Equal(AircraftId, aircraft.AircraftId);
            await Jobs.SelectAircraftAsync(aircraft.AircraftId);
            // Simulate the picker clearing during each periodic ItemsSource replacement.
            // The real production XAML binding is certified separately above, not rendered here.
            Task pickerReset = Task.CompletedTask;
            void OnOptionsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(JobsViewModel.AircraftOptions))
                    pickerReset = Jobs.SelectAircraftAsync(null);
            }
            Jobs.PropertyChanged += OnOptionsChanged;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var previous = Assert.Single(Jobs.AircraftOptions);
                    await Jobs.RefreshAircraftAndReadinessAsync();
                    await pickerReset;
                    Assert.NotSame(previous, Assert.Single(Jobs.AircraftOptions));
                    Assert.Equal(AircraftId, Jobs.SelectedAircraftId);
                    var refreshed = Assert.Single(Jobs.Offers);
                    Assert.Equal(CareerJobStartInputState.Ready, refreshed.StartInputState);
                    Assert.Equal("READY TO START", refreshed.AvailabilityText);
                    Assert.True(refreshed.CanStart);
                }
            }
            finally
            {
                Jobs.PropertyChanged -= OnOptionsChanged;
            }
            var ready = Assert.Single(Jobs.Offers);
            Assert.True(ready.CanStart, ready.AvailabilityText);
            Assert.Equal(CareerJobStartInputState.Ready, ready.StartInputState);
            Assert.Equal("READY TO START", ready.AvailabilityText);
            var merged = (await _registry.FindAircraftAsync(AircraftId))!;
            Assert.Equal(AircraftInstallationStatus.Installed, merged.InstallationStatus);
            Assert.True(merged.HasCompleteCapabilityProfile);
            await Jobs.StartOfferAsync(offer.OfferId); // real ICareerJobStartAction.StartAsync
            Assert.Contains("Career flight started:", Jobs.AcceptanceStatus);
            Assert.Empty(Jobs.Offers);
            Assert.Equal(ContractStatus.InProgress, (await ContractStore.ReadJobContractAsync(offer.OfferId))!.Contract.Status);
            Assert.Equal(ContractStatus.InProgress, Contracts.Find(offer.OfferId)!.Contract.Status);
            Assert.Equal(offer.OfferId, Session.ContractId);
            Assert.Equal(Session.SessionId, (await Checkpoints.LoadAsync())!.SessionId);
            Assert.Equal(AircraftId, (await ReservationAsync(offer.OfferId))!.CanonicalAircraftId);
            Shell.RefreshConnectionStatus();
            Assert.True(Shell.HasFlightSession);
            Assert.Contains("KJFK", Shell.CurrentFlightRouteSummary);
            return offer.OfferId;
        }

        public async Task TakeoffAsync()
        {
            await SamplesAsync(3, true, 0, 0, engines: 0, parking: true);
            await SamplesAsync(2, true, 0, 0);
            Assert.Equal(FlightOperationState.EngineStart, Session.OperationState);
            await SamplesAsync(2, true, 0, 8);
            Assert.Equal(FlightOperationState.TaxiOut, Session.OperationState);
            await SamplesAsync(2, true, 0, 55);
            await SamplesAsync(4, false, 200, 70);
            Assert.Equal(FlightOperationState.Airborne, Session.OperationState);
        }

        public async Task SamplesAsync(int count, bool ground, double agl, double speed,
            int engines = 1, bool parking = false, double descent = 0, double latitude = 40.63993)
        {
            for (int i = 0; i < count; i++)
            {
                Clock.Now = Clock.Now.AddSeconds(1);
                Telemetry.Latest = new(Clock.Now, latitude, -73.77869, 13 + agl, agl,
                    speed, speed, descent, 31, 0, 0, 1, ground, parking, engines,
                    170, 0, 0, true, false, false);
                await Runtime.RefreshAsync();
            }
        }

        public async Task<long> RowCountAsync(string table)
        {
            // Table names are literals controlled by this test, never simulator/player input.
            await using var db = new SqliteConnection($"Data Source={DatabasePath}");
            await db.OpenAsync();
            await using var command = db.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            return (long)(await command.ExecuteScalarAsync())!;
        }
    }

    private sealed class ObservedCheckpointStore(SqliteFlightSessionCheckpointStore inner) : IFlightSessionCheckpointStore
    {
        public Func<FlightSession, Task>? BeforeClear { get; set; }
        public bool FailNextClear { get; set; }
        public int ClearCount { get; private set; }
        public Task SaveAsync(FlightSession session, CancellationToken cancellationToken = default) => inner.SaveAsync(session, cancellationToken);
        public Task<FlightSession?> LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            if (BeforeClear is not null) await BeforeClear((await inner.LoadAsync(cancellationToken))!);
            if (FailNextClear)
            {
                FailNextClear = false;
                throw new IOException("Test crash before terminal checkpoint cleanup");
            }
            await inner.ClearAsync(cancellationToken);
            ClearCount++;
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class TestTelemetry : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest { get; set; }
    }
    private sealed class TestSettings : IAppSettingsService
    {
        public AppPreferences Current => AppPreferences.Default;
        public event EventHandler? Changed { add { } remove { } }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(AppPreferences preferences, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
}
