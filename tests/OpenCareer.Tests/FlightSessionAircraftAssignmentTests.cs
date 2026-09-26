using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Planning;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.Infrastructure.Airports;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class FlightSessionAircraftAssignmentTests : IDisposable
{
    private const string Model = AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId;
    private static readonly DateTimeOffset Epoch = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_root, "assignment.db");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" model")]
    [InlineData("model\n")]
    public void IdentityRequiresNormalizedCanonicalModel(string? value) =>
        Assert.ThrowsAny<ArgumentException>(() => new FlightSessionAircraftIdentity(value!));

    [Fact]
    public void DefaultPhysicalIdentityIsNotModelOnly()
    {
        Assert.Throws<ArgumentException>(() => new FlightSessionAircraftIdentity(Model, default(AirframeId)));
        var modelOnly = new FlightSessionAircraftIdentity(Model);
        Assert.Equal(Model, modelOnly.CanonicalAircraftId);
        Assert.Null(modelOnly.PhysicalAirframeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelOnlyCareerStartPersistsCanonicalModelWithoutInferringPhysicalAircraft(bool existingAirframes)
    {
        Harness app = await CreateAsync();
        if (existingAirframes)
        {
            await app.CreateAirframeAsync();
            await app.CreateAirframeAsync();
        }
        CareerJobPlayableStartRequest request = await app.RequestAsync();
        Assert.Null(request.PhysicalAirframeId);
        CareerJobPlayableStartResult result = await app.Loop.AcceptAndStartAsync(request);
        Assert.Equal(ContractStatus.InProgress, result.StartedFlight.Contract.Contract.Status);
        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Dispatch.DispatchResult!.Status);
        var identity = Assert.IsType<FlightSessionAircraftIdentity>(result.StartedFlight.FlightSession.AircraftIdentity);
        Assert.Equal(Model, identity.CanonicalAircraftId);
        Assert.Null(identity.PhysicalAirframeId);
        Assert.Equal(identity, (await app.Checkpoints.LoadAsync())!.AircraftIdentity);
        Assert.Equal(existingAirframes ? 2L : 0L, await ScalarAsync("SELECT count(*) FROM airframes;"));
        Assert.Equal(Model, (await app.Fleet.FindByReservationIdAsync(result.Dispatch.FleetResult.ReservationId))!.CanonicalAircraftId);
    }

    [Theory]
    [InlineData(AirframeDamageState.None)]
    [InlineData(AirframeDamageState.Grounding)]
    public async Task ExplicitAssignmentPersistsAndRecoversWithoutMutatingConditionOrAddingGroundingPolicy(AirframeDamageState damage)
    {
        Harness app = await CreateAsync();
        AirframeStoreRecord original = await app.CreateAirframeAsync(damage: damage);
        AirframeStoreRecord unrelated = await app.CreateAirframeAsync();
        CareerJobPlayableStartRequest request = (await app.RequestAsync()) with { PhysicalAirframeId = original.Airframe.AirframeId };
        CareerJobPlayableStartResult result = await app.Loop.AcceptAndStartAsync(request);
        FlightSession session = result.StartedFlight.FlightSession;
        var expected = new FlightSessionAircraftIdentity(Model, original.Airframe.AirframeId);
        Assert.Equal(expected, session.AircraftIdentity);
        Assert.Equal(expected, (await new SqliteFlightSessionCheckpointStore(DatabasePath).LoadAsync())!.AircraftIdentity);

        // Recreate the production stores and runtime authorities against the same database.
        var restarted = new Harness(DatabasePath);
        await restarted.InitializeAsync();
        FlightSession recovered = Assert.IsType<FlightSession>(restarted.Sessions.Current);
        Assert.Equal(FlightSessionStatus.Suspended, recovered.Status);
        Assert.Equal(session.SessionId, recovered.SessionId);
        Assert.Equal(session.ContractId, recovered.ContractId);
        Assert.Equal(expected, recovered.AircraftIdentity);
        StartedJobFlightSessionResult replay = await restarted.StartBridge.StartAsync(
            result.Dispatch, request.DispatchContext, physicalAirframeId: request.PhysicalAirframeId);
        Assert.Same(recovered, replay.FlightSession);
        Assert.Equal(original, await restarted.Airframes.FindAsync(original.Airframe.AirframeId));
        Assert.Equal(unrelated, await restarted.Airframes.FindAsync(unrelated.Airframe.AirframeId));
        Assert.Equal(14L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(1, recovered.SchemaVersion);
    }

    [Fact]
    public async Task TwoSameModelAirframesRemainDistinctAcrossSeparateOperations()
    {
        Harness app = await CreateAsync();
        AirframeStoreRecord a = await app.CreateAirframeAsync();
        AirframeStoreRecord b = await app.CreateAirframeAsync();
        var first = await app.Loop.AcceptAndStartAsync((await app.RequestAsync()) with { PhysicalAirframeId = a.Airframe.AirframeId });
        await app.Abandon.AbandonAsync(first.StartedFlight.FlightSession.SessionId, first.StartedFlight.Contract.Contract.ContractId);
        Assert.Null(app.Sessions.Current);
        var second = await app.Loop.AcceptAndStartAsync((await app.RequestAsync()) with { PhysicalAirframeId = b.Airframe.AirframeId });
        Assert.NotEqual(first.StartedFlight.FlightSession.SessionId, second.StartedFlight.FlightSession.SessionId);
        Assert.Equal(a.Airframe.AirframeId, first.StartedFlight.FlightSession.AircraftIdentity!.PhysicalAirframeId);
        Assert.Equal(b.Airframe.AirframeId, second.StartedFlight.FlightSession.AircraftIdentity!.PhysicalAirframeId);
        Assert.Equal(a, await app.Airframes.FindAsync(a.Airframe.AirframeId));
        Assert.Equal(b, await app.Airframes.FindAsync(b.Airframe.AirframeId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("default")]
    [InlineData("model-mismatch")]
    public async Task InvalidPhysicalAssignmentCannotCreateSessionOrTransitionContractInProgress(string reason)
    {
        Harness app = await CreateAsync();
        AirframeStoreRecord unrelated = await app.CreateAirframeAsync();
        AirframeStoreRecord? mismatch = reason == "model-mismatch" ? await app.CreateAirframeAsync("other-canonical-model") : null;
        AirframeId id = reason == "default" ? default : mismatch?.Airframe.AirframeId ?? new AirframeId(Guid.NewGuid());
        CareerJobPlayableStartRequest request = (await app.RequestAsync()) with { PhysicalAirframeId = id };
        if (reason == "default")
            await Assert.ThrowsAsync<ArgumentException>(() => app.Loop.AcceptAndStartAsync(request));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.Loop.AcceptAndStartAsync(request));
        Assert.Null(app.Sessions.Current);
        Assert.Null(await app.Checkpoints.LoadAsync());
        PersistedJobContract? retained = await app.Contracts.ReadJobContractAsync(request.Contract.Offer.OfferId);
        if (reason == "default") Assert.Null(retained);
        else Assert.Equal(ContractStatus.Accepted, retained!.Contract.Status);
        Assert.Equal(unrelated, await app.Airframes.FindAsync(unrelated.Airframe.AirframeId));
        if (mismatch is not null) Assert.Equal(mismatch, await app.Airframes.FindAsync(mismatch.Airframe.AirframeId));
        Assert.Equal(mismatch is null ? 1L : 2L, await ScalarAsync("SELECT count(*) FROM airframes;"));
    }

    [Theory]
    [InlineData("missing-store")]
    [InlineData("wrong-record")]
    public async Task ExplicitAssignmentRequiresAuthoritativeStoreAndExactPhysicalRecord(string reason)
    {
        Harness app = await CreateAsync();
        AirframeStoreRecord a = await app.CreateAirframeAsync();
        AirframeStoreRecord b = await app.CreateAirframeAsync();
        CareerJobPlayableStartRequest request = await app.RequestAsync();
        AcceptedJobDispatchResult dispatch = await app.Dispatch.AcceptReserveAndEvaluateAsync(
            request.Contract, request.DispatchContext, request.DispatchRequirements);
        var bridge = new AcceptedJobFlightSessionBridge(new AcceptedJobStartBridge(app.Lifecycle), app.Contracts,
            app.Persistence, app.Sessions, reason == "missing-store" ? null : new WrongRecordStore(b));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.StartAsync(dispatch,
            request.DispatchContext, physicalAirframeId: a.Airframe.AirframeId));
        Assert.Equal(ContractStatus.Accepted, (await app.Contracts.ReadJobContractAsync(request.Contract.Offer.OfferId))!.Contract.Status);
        Assert.Null(await app.Checkpoints.LoadAsync());
    }

    [Theory]
    [InlineData("other-physical")]
    [InlineData("remove-physical")]
    [InlineData("other-model")]
    public async Task ReplayCannotReplaceOrDowngradeAnExistingAssignment(string change)
    {
        Harness app = await CreateAsync();
        AirframeStoreRecord a = await app.CreateAirframeAsync();
        AirframeStoreRecord b = await app.CreateAirframeAsync();
        CareerJobPlayableStartRequest request = (await app.RequestAsync()) with { PhysicalAirframeId = a.Airframe.AirframeId };
        var started = await app.Loop.AcceptAndStartAsync(request);
        var dispatch = started.Dispatch;
        AirframeId? replayId = change == "remove-physical" ? null : b.Airframe.AirframeId;
        if (change == "other-model")
            dispatch = dispatch with { FleetResult = dispatch.FleetResult with { CanonicalAircraftId = "different-model" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartBridge.StartAsync(dispatch,
            request.DispatchContext, physicalAirframeId: replayId));
        Assert.Equal(started.StartedFlight.FlightSession.AircraftIdentity, (await app.Checkpoints.LoadAsync())!.AircraftIdentity);
        Assert.Equal(started.StartedFlight.Contract, await app.Contracts.ReadJobContractAsync(request.Contract.Offer.OfferId));
    }

    [Fact]
    public async Task LegacyRecoveredSessionIsNotRetrofittedByAReplay()
    {
        Harness app = await CreateAsync();
        var physical = await app.CreateAirframeAsync();
        var request = await app.RequestAsync();
        var started = await app.Loop.AcceptAndStartAsync(request);
        await RewriteCheckpointAsync(payload => payload.Remove("aircraftIdentity"));
        var restarted = new Harness(DatabasePath);
        await restarted.InitializeAsync();
        var replay = await restarted.StartBridge.StartAsync(started.Dispatch, request.DispatchContext);
        Assert.Null(replay.FlightSession.AircraftIdentity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.StartBridge.StartAsync(started.Dispatch,
            request.DispatchContext, physicalAirframeId: physical.Airframe.AirframeId));
        Assert.Null((await restarted.Checkpoints.LoadAsync())!.AircraftIdentity);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Active, false)]
    [InlineData(FlightSessionStatus.Suspended, false)]
    [InlineData(FlightSessionStatus.Completed, false)]
    [InlineData(FlightSessionStatus.Interrupted, false)]
    [InlineData(FlightSessionStatus.Cancelled, false)]
    [InlineData(FlightSessionStatus.Active, true)]
    [InlineData(FlightSessionStatus.Suspended, true)]
    [InlineData(FlightSessionStatus.Completed, true)]
    [InlineData(FlightSessionStatus.Interrupted, true)]
    [InlineData(FlightSessionStatus.Cancelled, true)]
    public async Task CheckpointRecoveryPreservesIdentityOrLegacyAbsenceAcrossSessionStatuses(FlightSessionStatus status, bool legacy)
    {
        // Checkpoint-format fixtures; this test does not fabricate gameplay completion or flight evidence.
        var identity = new FlightSessionAircraftIdentity(Model, new AirframeId(Guid.NewGuid()));
        var checkpoint = FlightSession.Start(Epoch, Guid.NewGuid(), aircraftIdentity: identity) with { Status = status };
        var store = new SqliteFlightSessionCheckpointStore(DatabasePath);
        await store.SaveAsync(checkpoint);
        if (legacy) await RewriteCheckpointAsync(payload => payload.Remove("aircraftIdentity"));
        var sessions = new FlightSessionCoordinator();
        var persistence = new FlightSessionPersistenceService(sessions, new SqliteFlightSessionCheckpointStore(DatabasePath));
        var recovered = (await persistence.RecoverAsync())!;
        Assert.Equal(status == FlightSessionStatus.Active ? FlightSessionStatus.Suspended : status, recovered.Status);
        Assert.Equal(checkpoint.SessionId, recovered.SessionId);
        Assert.Equal(checkpoint.ContractId, recovered.ContractId);
        Assert.Equal(legacy ? null : identity, recovered.AircraftIdentity);
        Assert.Equal(recovered.AircraftIdentity, (await store.LoadAsync())!.AircraftIdentity);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"canonicalAircraftId\":\" \"}")]
    [InlineData("{\"canonicalAircraftId\":\" model\"}")]
    [InlineData("{\"canonicalAircraftId\":\"model\",\"physicalAirframeId\":{}}")]
    [InlineData("{\"canonicalAircraftId\":\"model\",\"physicalAirframeId\":{\"value\":\"00000000-0000-0000-0000-000000000000\"}}")]
    public async Task MalformedSuppliedIdentityFailsClosedInsteadOfLoadingAsUnassigned(string identityJson)
    {
        var store = new SqliteFlightSessionCheckpointStore(DatabasePath);
        await store.SaveAsync(FlightSession.Start(Epoch));
        await RewriteCheckpointAsync(payload => payload["aircraftIdentity"] = JsonNode.Parse(identityJson));
        var sessions = new FlightSessionCoordinator();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new FlightSessionPersistenceService(sessions, store).RecoverAsync());
        Assert.Null(sessions.Current);
    }

    [Fact]
    public void RuntimeCannotReassignAnExistingSession()
    {
        var coordinator = new FlightSessionCoordinator();
        FlightSession started = coordinator.Start(Epoch, aircraftIdentity: new(Model, new AirframeId(Guid.NewGuid())));
        Assert.Throws<InvalidOperationException>(() => coordinator.CommitPersisted(started with { AircraftIdentity = new(Model) }));
        Assert.Same(started, coordinator.Current);
    }

    private async Task<Harness> CreateAsync()
    {
        var app = new Harness(DatabasePath);
        await app.Profiles.SaveAsync(PlayerCareerProfile.Start(Guid.NewGuid(), "KJFK", Epoch), null, Epoch);
        await app.InitializeAsync();
        return app;
    }

    private async Task RewriteCheckpointAsync(Action<JsonObject> change)
    {
        await using var db = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT payload_json FROM flight_session_checkpoint WHERE slot_id=1;";
        var payload = JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsObject();
        change(payload);
        command.CommandText = "UPDATE flight_session_checkpoint SET payload_json=$json WHERE slot_id=1;";
        command.Parameters.AddWithValue("$json", payload.ToJsonString());
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var db = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // Production dispatch/contract/reservation/session/SQLite authorities. Only installed evidence and
    // the clock are supplied by the test; completion is not invoked and receives no flight evidence.
    private sealed class Harness
    {
        public SqlitePlayerCareerProfileStore Profiles { get; }
        public SqliteJobContractStore Contracts { get; }
        public SqliteAircraftAvailabilityStore Fleet { get; }
        public SqliteAirframeStore Airframes { get; }
        public SqliteFlightSessionCheckpointStore Checkpoints { get; }
        public FlightSessionCoordinator Sessions { get; } = new();
        public FlightSessionPersistenceService Persistence { get; }
        public JobContractLifecycleService Lifecycle { get; }
        public AcceptedJobDispatchBridge Dispatch { get; }
        public AcceptedJobFlightSessionBridge StartBridge { get; }
        public CareerJobPlayableLoopCoordinator Loop { get; }
        public CareerFlightAbandonCoordinator Abandon { get; }
        private readonly PlayerCareerRuntimeState _career;
        private readonly JobContractRuntimeState _contracts;
        private readonly DevelopmentFlightService _development;
        private readonly CareerJobStartInputSource _inputs;
        private readonly Clock _clock = new();

        public Harness(string path)
        {
            var options = new OpenCareerDatabaseOptions(path);
            Profiles = new(options, NullLogger<SqlitePlayerCareerProfileStore>.Instance);
            Contracts = new(options, NullLogger<SqliteJobContractStore>.Instance);
            Airframes = new(options, NullLogger<SqliteAirframeStore>.Instance);
            Fleet = new(path);
            Checkpoints = new(path);
            Persistence = new(Sessions, Checkpoints);
            _career = new(Profiles);
            var recovery = new SqliteJobContractRecoverySource(options, NullLogger<SqliteJobContractRecoverySource>.Instance);
            _contracts = new(new JobContractRecoveryService(recovery, Contracts));
            Lifecycle = new(Contracts, _contracts);
            var boards = new SqliteJobBoardStateStore(options, NullLogger<SqliteJobBoardStateStore>.Instance);
            var location = new PlayerCareerLocationCoordinator(Profiles, _career);
            var registry = new AircraftRegistryCatalogService([new InstalledObservation(), new PlayableLoopReferenceAircraftObservationSource()]);
            var airports = new CachedAirportDataSource([new PlayableLoopReferenceAirportObservationSource(_clock)], clock: _clock);
            var planning = new OperationDispatchPlanningService(registry, airports, aircraftAvailability: Fleet);
            Dispatch = new(new JobAcceptanceFleetBridge(new JobOfferAcceptanceService(Contracts, Lifecycle, boards, _contracts),
                Contracts, new AircraftReservationCoordinator(registry, Fleet)), planning);
            StartBridge = new(new AcceptedJobStartBridge(Lifecycle), Contracts, Persistence, Sessions, Airframes);
            var ledger = new SqliteEconomyLedgerStore(options, NullLogger<SqliteEconomyLedgerStore>.Instance);
            var logbook = new SqliteLogbookStore(options, NullLogger<SqliteLogbookStore>.Instance);
            var terminal = new CareerFlightTerminalWorkflowCoordinator(
                new SettlementPendingContractCoordinator(new EconomySettlementService(Contracts, ledger)),
                new SettledJobLogbookCoordinator(Sessions, new LogbookCommitCoordinator(logbook)), logbook,
                new CareerLogbookExperienceCoordinator(new PlayerCareerExperienceCoordinator(Profiles, _career, Contracts)), location,
                new CareerFlightFinalizationCoordinator(new CareerFlightReservationReleaseCoordinator(Fleet, Fleet), Persistence, Sessions));
            Loop = new(Dispatch, StartBridge,
                new JobFlightSessionCompletionBridge(new JobFlightCompletionEvidenceTracker(Sessions, new NoFlightEvidence()), Sessions,
                    new FlightSessionCompletionService(Sessions, Persistence)),
                new CompletedJobContractBridge(Contracts, Lifecycle, Sessions), Contracts, terminal);
            Abandon = new(Sessions, Persistence, Contracts, Lifecycle, _contracts, Fleet, Fleet, _clock,
                NullLogger<CareerFlightAbandonCoordinator>.Instance);
            _development = new(new JobBoardGenerationService(boards), _career, recovery, Checkpoints, _clock, location);
            _inputs = new(boards, _career, Contracts, registry, planning, [new PersistedJobContractTermsSource()],
                [new StandardCivilianPointToPointDispatchAuthoritySource()], _clock);
        }

        public async Task InitializeAsync()
        {
            await _career.InitializeAsync();
            await _contracts.InitializeAsync();
            await Persistence.RecoverAsync();
        }

        public async Task<CareerJobPlayableStartRequest> RequestAsync()
        {
            _clock.Now = _clock.Now.AddSeconds(1);
            var offer = await _development.GenerateAsync();
            Assert.Equal("KJFK", offer.OriginIcao);
            Assert.Equal("KJFK", offer.DestinationIcao);
            var ready = await _inputs.ReadAsync(offer.OfferId, Model);
            Assert.Equal(CareerJobStartInputState.Ready, ready.State);
            Assert.True(ready.IsReady);
            return Assert.IsType<CareerJobPlayableStartRequest>(ready.Request);
        }

        public Task<AirframeStoreRecord> CreateAirframeAsync(string model = Model, AirframeDamageState damage = AirframeDamageState.None) =>
            Airframes.CreateAsync(new(new AirframeId(Guid.NewGuid()), model, Epoch.AddDays(-1)), new(0.2, damage), Epoch);
    }

    private sealed class InstalledObservation : IAircraftRegistryObservationSource
    {
        public Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(string canonicalAircraftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>([new(Model, "test-installed-evidence", "C172SP Classic Passengers",
                AircraftDataConfidence.Verified, IsInstalled: true)]);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Epoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoFlightEvidence : IFlightStateEvidenceSource
    {
        public FlightStateEvidence? Current => null;
        public event Action<FlightStateEvidence?>? EvidenceChanged { add { } remove { } }
    }

    private sealed class WrongRecordStore(AirframeStoreRecord record) : IAirframeStore
    {
        public Task<AirframeStoreRecord?> FindAsync(AirframeId airframeId, CancellationToken cancellationToken = default) => Task.FromResult<AirframeStoreRecord?>(record);
        public Task<AirframeStoreRecord> CreateAsync(Airframe airframe, AirframeCondition condition, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AirframeStoreRecord> UpdateConditionAsync(Airframe airframe, AirframeCondition condition, long expectedRevision, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
