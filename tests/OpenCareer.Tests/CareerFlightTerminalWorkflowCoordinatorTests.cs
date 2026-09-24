using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class CareerFlightTerminalWorkflowCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TerminalWorkflowChainsAllAuthoritativeSteps()
    {
        TestContext context =
            CreateContext();

        CareerFlightTerminalWorkflowResult result =
            await context.Workflow.CompleteAsync(
                context.Request);

        Assert.True(
            result.Settlement.WasNewlyPosted);
        Assert.Equal(
            LogbookAppendDisposition.Appended,
            result.Logbook.Disposition);
        Assert.Contains(
            result.Logbook.Entry.Debrief.DebriefId,
            result.CareerProfile.Profile
                .AppliedExperienceDebriefIds);
        Assert.Equal(
            "KSYR",
            result.CareerProfile.Profile.Location
                .CurrentAirportIcao);
        Assert.Contains(
            result.Settlement.PersistedContract.Contract.ContractId,
            result.CareerProfile.Profile.Location
                .AppliedTravelContracts);
        Assert.Equal(
            CareerFlightFinalizationStatus.Finalized,
            result.Finalization.Status);

        Assert.Equal(
            1,
            context.Ledger.UniquePostCount);
        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Equal(
            1,
            context.CheckpointStore.ClearCount);
        Assert.Null(
            context.Sessions.Current);
    }

    [Fact]
    public async Task ReplayAfterSuccessfulFinalizationUsesPersistedAuthorities()
    {
        TestContext context =
            CreateContext();

        CareerFlightTerminalWorkflowResult first =
            await context.Workflow.CompleteAsync(
                context.Request);

        CareerFlightTerminalWorkflowResult replay =
            await context.Workflow.CompleteAsync(
                context.Request);

        Assert.True(
            first.Settlement.WasNewlyPosted);
        Assert.False(
            replay.Settlement.WasNewlyPosted);
        Assert.Equal(
            LogbookAppendDisposition.AlreadyExists,
            replay.Logbook.Disposition);
        Assert.Equal(
            CareerFlightFinalizationStatus.AlreadyFinalized,
            replay.Finalization.Status);

        Assert.Equal(
            1,
            context.Ledger.UniquePostCount);
        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Equal(
            1,
            context.CheckpointStore.ClearCount);
        Assert.Null(
            context.Sessions.Current);
    }

    [Fact]
    public async Task FiveCompletionAttemptsConvergeWithoutDuplicateMutations()
    {
        TestContext context =
            CreateContext();

        CareerFlightTerminalWorkflowResult[] results =
            new CareerFlightTerminalWorkflowResult[5];

        for (int index = 0; index < results.Length; index++)
        {
            results[index] =
                await context.Workflow.CompleteAsync(
                    context.Request);
        }

        Assert.True(
            results[0].Settlement.WasNewlyPosted);
        Assert.All(
            results.Skip(1),
            result =>
            {
                Assert.False(
                    result.Settlement.WasNewlyPosted);
                Assert.Equal(
                    LogbookAppendDisposition.AlreadyExists,
                    result.Logbook.Disposition);
                Assert.Equal(
                    CareerFlightFinalizationStatus.AlreadyFinalized,
                    result.Finalization.Status);
            });

        Assert.Equal(
            1,
            context.Ledger.UniquePostCount);
        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Equal(
            1,
            context.CheckpointStore.ClearCount);
    }

    [Fact]
    public async Task RetryAfterLogbookFailureReusesSettlementAndCompletes()
    {
        TestContext context =
            CreateContext();

        context.Logbook.FailNextAppend =
            true;

        await Assert.ThrowsAsync<IOException>(
            () => context.Workflow.CompleteAsync(
                context.Request));

        Assert.Equal(
            1,
            context.Ledger.UniquePostCount);
        Assert.Empty(
            context.Logbook.Entries);
        Assert.Equal(
            0,
            context.ProfileStore.SaveCount);
        Assert.Equal(
            0,
            context.Fleet.ReleaseCount);
        Assert.NotNull(
            context.Sessions.Current);

        CareerFlightTerminalWorkflowResult retry =
            await context.Workflow.CompleteAsync(
                context.Request);

        Assert.False(
            retry.Settlement.WasNewlyPosted);
        Assert.Equal(
            LogbookAppendDisposition.Appended,
            retry.Logbook.Disposition);
        Assert.Equal(
            CareerFlightFinalizationStatus.Finalized,
            retry.Finalization.Status);
        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
        Assert.Null(
            context.Sessions.Current);
    }

    [Fact]
    public async Task RestartAfterSettlementResumesWithoutDuplicatePayment()
    {
        TestContext context =
            CreateContext();

        context.Logbook.FailNextAppend =
            true;

        await Assert.ThrowsAsync<IOException>(
            () => context.Workflow.CompleteAsync(
                context.Request));

        CareerFlightTerminalWorkflowCoordinator restarted =
            RebuildWorkflow(
                context,
                out FlightSessionCoordinator restartedSessions);

        CareerFlightTerminalWorkflowResult result =
            await restarted.CompleteAsync(
                context.Request);

        Assert.False(
            result.Settlement.WasNewlyPosted);
        Assert.Equal(
            1,
            context.Ledger.UniquePostCount);
        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Null(
            restartedSessions.Current);
    }

    [Fact]
    public async Task RestartAfterLogbookResumesAtProfileWithoutSecondEntry()
    {
        TestContext context =
            CreateContext();

        context.ProfileStore.FailOnSaveAttempt =
            1;

        await Assert.ThrowsAsync<IOException>(
            () => context.Workflow.CompleteAsync(
                context.Request));

        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            0,
            context.ProfileStore.SaveCount);

        CareerFlightTerminalWorkflowCoordinator restarted =
            RebuildWorkflow(
                context,
                out FlightSessionCoordinator restartedSessions);

        await restarted.CompleteAsync(
            context.Request);

        Assert.Equal(
            1,
            context.Ledger.UniquePostCount);
        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Null(
            restartedSessions.Current);
    }

    [Fact]
    public async Task RestartAfterProfileResumesWithoutDuplicateProgression()
    {
        TestContext context =
            CreateContext();

        context.ProfileStore.FailOnSaveAttempt =
            2;

        await Assert.ThrowsAsync<IOException>(
            () => context.Workflow.CompleteAsync(
                context.Request));

        Assert.Equal(
            1,
            context.ProfileStore.SaveCount);
        Assert.Equal(
            1,
            context.ProfileStore.Current.Profile.Experience.FlightCount);

        CareerFlightTerminalWorkflowCoordinator restarted =
            RebuildWorkflow(
                context,
                out FlightSessionCoordinator restartedSessions);

        await restarted.CompleteAsync(
            context.Request);

        Assert.Equal(
            1,
            context.ProfileStore.Current.Profile.Experience.FlightCount);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Null(
            restartedSessions.Current);
    }

    [Fact]
    public async Task RestartBeforeCleanupToleratesAlreadyReleasedFleet()
    {
        TestContext context =
            CreateContext();

        context.CheckpointStore.FailNextClear =
            true;

        await Assert.ThrowsAsync<IOException>(
            () => context.Workflow.CompleteAsync(
                context.Request));

        Assert.Null(
            context.Fleet.Ownership);
        Assert.NotNull(
            context.CheckpointStore.Checkpoint);

        CareerFlightTerminalWorkflowCoordinator restarted =
            RebuildWorkflow(
                context,
                out FlightSessionCoordinator restartedSessions);

        CareerFlightTerminalWorkflowResult result =
            await restarted.CompleteAsync(
                context.Request);

        Assert.Equal(
            CareerFlightReservationReleaseStatus.AlreadyReleased,
            result.Finalization.ReservationRelease.Status);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Equal(
            1,
            context.CheckpointStore.ClearCount);
        Assert.Null(
            restartedSessions.Current);
    }

    private static TestContext CreateContext()
    {
        Guid contractId =
            Guid.Parse(
                "9f000000-0000-0000-0000-000000000001");

        FlightSession completedSession =
            CompletedSession(
                contractId);

        PersistedJobContract completedContract =
            CompletedContract(
                contractId,
                completedSession.Milestones.CompletedAt!.Value);

        var ledger =
            new FakeLedgerStore();

        var contractStore =
            new FakeContractStore(
                completedContract);

        var settlementCoordinator =
            new SettlementPendingContractCoordinator(
                new EconomySettlementService(
                    contractStore,
                    ledger));

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            completedSession);

        var checkpointStore =
            new MemoryCheckpointStore
            {
                Checkpoint =
                    completedSession
            };

        var flightPersistence =
            new FlightSessionPersistenceService(
                sessions,
                checkpointStore);

        var logbookStore =
            new FakeLogbookStore();

        var settledLogbook =
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    logbookStore));

        PlayerCareerProfileStoreRecord initialProfile =
            InitialProfile();

        var profileStore =
            new FakeProfileStore(
                initialProfile);

        var profileRuntime =
            new PlayerCareerRuntimeState(
                profileStore);

        var experience =
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    profileStore,
                    profileRuntime));

        var location =
            new PlayerCareerLocationCoordinator(
                profileStore,
                profileRuntime);

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                contractId);

        var fleet =
            new FakeFleetStore(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId));

        var release =
            new CareerFlightReservationReleaseCoordinator(
                fleet,
                fleet);

        var finalization =
            new CareerFlightFinalizationCoordinator(
                release,
                flightPersistence,
                sessions);

        var workflow =
            new CareerFlightTerminalWorkflowCoordinator(
                settlementCoordinator,
                settledLogbook,
                logbookStore,
                experience,
                location,
                finalization);

        DateTimeOffset settledAt =
            completedContract.Contract.CompletedAt!.Value
                .AddMinutes(1);

        DateTimeOffset logbookCommittedAt =
            settledAt.AddMinutes(1);

        var request =
            new CareerFlightTerminalWorkflowRequest(
                new SettlementPendingContractRequest(
                    new SettlementPendingContract(
                        completedContract,
                        ContractSettlementEngine.GetIdempotencyKey(
                            contractId)),
                    new ContractSettlementCosts(
                        FuelCost:
                            100m,
                        MaintenanceReserveCost:
                            50m,
                        AirportFees:
                            25m),
                    settledAt),
                new SettledJobLogbookContext(
                    new AircraftDebrief(
                        "Integration Aircraft",
                        Family:
                            "Cargo"),
                    ActualDeparture:
                        "KRME",
                    ActualArrival:
                        "KSYR",
                    DiversionLocation:
                        null,
                    Payload:
                        new PayloadDebrief(
                            PassengerCount:
                                null,
                            CargoMassPounds:
                                500,
                            CargoDescription:
                                "Career cargo",
                            Outcome:
                                "Delivered",
                            EvidenceQuality:
                                EvidenceQuality.MissionDeclared),
                    SafetyOutcome:
                        FlightSafetyOutcome.CompletedNormally,
                    MissionOutcome:
                        MissionOutcome.Succeeded,
                    ReputationDelta:
                        2d),
                logbookCommittedAt,
                ExperienceSavedAt:
                    logbookCommittedAt.AddMinutes(1));

        return new(
            workflow,
            request,
            contractStore,
            ledger,
            logbookStore,
            profileStore,
            fleet,
            checkpointStore,
            sessions);
    }

    private static CareerFlightTerminalWorkflowCoordinator RebuildWorkflow(
        TestContext context,
        out FlightSessionCoordinator sessions)
    {
        sessions =
            new FlightSessionCoordinator();

        if (context.CheckpointStore.Checkpoint is { } checkpoint)
            sessions.Restore(checkpoint);

        var flightPersistence =
            new FlightSessionPersistenceService(
                sessions,
                context.CheckpointStore);

        var profileRuntime =
            new PlayerCareerRuntimeState(
                context.ProfileStore);

        return new CareerFlightTerminalWorkflowCoordinator(
            new SettlementPendingContractCoordinator(
                new EconomySettlementService(
                    context.ContractStore,
                    context.Ledger)),
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    context.Logbook)),
            context.Logbook,
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    context.ProfileStore,
                    profileRuntime)),
            new PlayerCareerLocationCoordinator(
                context.ProfileStore,
                profileRuntime),
            new CareerFlightFinalizationCoordinator(
                new CareerFlightReservationReleaseCoordinator(
                    context.Fleet,
                    context.Fleet),
                flightPersistence,
                sessions));
    }

    private static PersistedJobContract CompletedContract(
        Guid contractId,
        DateTimeOffset completedAt)
    {
        var contract =
            new JobContract(
                ContractId:
                    contractId,
                EmployerId:
                    null,
                Kind:
                    ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CompanyContract,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.CompanyRevenue,
                        GrossCustomerRevenue:
                            1_000m,
                        PilotCompensation:
                            0m,
                        EmployerCoversFuel:
                            false,
                        EmployerCoversMaintenance:
                            false,
                        EmployerCoversAirportFees:
                            false),
                OfferedAt:
                    Epoch.AddHours(-1),
                MustStartBy:
                    null,
                MustCompleteBy:
                    Epoch.AddHours(2),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.Completed,
                AcceptedAt:
                    Epoch.AddMinutes(-30),
                StartedAt:
                    Epoch,
                CompletedAt:
                    completedAt);

        contract.Validate();

        return new(
            contract,
            Version: 3);
    }

    private static PlayerCareerProfileStoreRecord InitialProfile()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "9f000000-0000-0000-0000-000000000099"),
                "KRME",
                Epoch.AddHours(-2));

        return new(
            Revision: 1,
            profile,
            SavedAt:
                Epoch.AddHours(-1));
    }

    private static FlightSession CompletedSession(
        Guid contractId)
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId,
                plan:
                    new FlightSessionPlan(
                        "KRME",
                        "KSYR"));

        session =
            Advance(
                session,
                1,
                stable:
                    true,
                validAircraft:
                    true);

        session =
            Advance(
                session,
                2,
                movement:
                    true);

        session =
            Advance(
                session,
                3,
                takeoffCandidate:
                    true);

        session =
            Advance(
                session,
                4,
                airborne:
                    true);

        session =
            Advance(
                session,
                5,
                touchdown:
                    true);

        session =
            Advance(
                session,
                6,
                rollout:
                    true);

        session =
            Advance(
                session,
                7,
                parking:
                    true,
                shutdown:
                    true);

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(8),
                    Connected:
                        true,
                    StableTelemetry:
                        true,
                    ContinuityPlausible:
                        true,
                    OperationCompleteConfirmed:
                        true),
                ShutdownConfirmed:
                    true));
    }

    private static FlightSession Advance(
        FlightSession session,
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false,
        bool shutdown = false) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(seconds),
                    Connected:
                        true,
                    StableTelemetry:
                        stable,
                    ValidLoadedAircraft:
                        validAircraft,
                    ContinuityPlausible:
                        true,
                    SelfPoweredMovementForFlight:
                        movement,
                    TakeoffCandidate:
                        takeoffCandidate,
                    AirborneConfirmed:
                        airborne,
                    TouchdownConfirmed:
                        touchdown,
                    LandingRolloutConfirmed:
                        rollout,
                    ParkingConfirmed:
                        parking),
                ShutdownConfirmed:
                    shutdown));

    private sealed record TestContext(
        CareerFlightTerminalWorkflowCoordinator Workflow,
        CareerFlightTerminalWorkflowRequest Request,
        FakeContractStore ContractStore,
        FakeLedgerStore Ledger,
        FakeLogbookStore Logbook,
        FakeProfileStore ProfileStore,
        FakeFleetStore Fleet,
        MemoryCheckpointStore CheckpointStore,
        FlightSessionCoordinator Sessions);

    private sealed class FakeContractStore(
        PersistedJobContract contract)
        : IJobContractStore
    {
        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PersistedJobContract?>(
                contract.Contract.ContractId
                    == contractId
                        ? contract
                        : null);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLedgerStore
        : IEconomyLedgerStore
    {
        private readonly Dictionary<string, EconomyLedgerTransaction>
            _transactions =
                new(StringComparer.Ordinal);

        public int UniquePostCount =>
            _transactions.Count;

        public Task<LedgerPostResult> PostAsync(
            EconomyLedgerTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Validate();

            if (_transactions.TryGetValue(
                    transaction.IdempotencyKey,
                    out EconomyLedgerTransaction? existing))
            {
                if (!Equivalent(
                        existing,
                        transaction))
                {
                    throw new InvalidOperationException(
                        "Conflicting duplicate settlement.");
                }

                return Task.FromResult(
                    LedgerPostResult.AlreadyPosted);
            }

            _transactions.Add(
                transaction.IdempotencyKey,
                transaction);

            return Task.FromResult(
                LedgerPostResult.Posted);
        }

        public Task<EconomyLedgerTransaction?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _transactions.TryGetValue(
                idempotencyKey,
                out EconomyLedgerTransaction? result);

            return Task.FromResult(
                result);
        }

        public Task<decimal> ReadCashBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _transactions.Values.Sum(
                    item =>
                        item.CashChange));
        }

        public Task<IReadOnlyList<LedgerAccountBalance>>
            ReadAccountBalancesAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EconomyLedgerTransaction>>
            ReadRecentAsync(
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static bool Equivalent(
            EconomyLedgerTransaction left,
            EconomyLedgerTransaction right) =>
            left.TransactionId
                == right.TransactionId
            && string.Equals(
                left.IdempotencyKey,
                right.IdempotencyKey,
                StringComparison.Ordinal)
            && left.OccurredAt
                == right.OccurredAt
            && string.Equals(
                left.Description,
                right.Description,
                StringComparison.Ordinal)
            && string.Equals(
                left.ReferenceType,
                right.ReferenceType,
                StringComparison.Ordinal)
            && string.Equals(
                left.ReferenceId,
                right.ReferenceId,
                StringComparison.Ordinal)
            && left.Postings.SequenceEqual(
                right.Postings);
    }

    private sealed class FakeLogbookStore
        : ILogbookWriter,
          ILogbookIdempotencySource
    {
        private readonly Dictionary<string, LogbookEntry>
            _entries =
                new(StringComparer.Ordinal);

        public IReadOnlyCollection<LogbookEntry> Entries =>
            _entries.Values;

        public bool FailNextAppend { get; set; }

        public Task<LogbookEntry?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _entries.TryGetValue(
                idempotencyKey,
                out LogbookEntry? entry);

            return Task.FromResult(
                entry);
        }

        public Task<LogbookAppendResult> TryAppendAsync(
            LogbookEntry entry,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FailNextAppend)
            {
                FailNextAppend =
                    false;
                throw new IOException(
                    "Synthetic logbook failure.");
            }

            if (_entries.TryGetValue(
                    idempotencyKey,
                    out LogbookEntry? existing))
            {
                return Task.FromResult(
                    new LogbookAppendResult(
                        LogbookAppendDisposition.AlreadyExists,
                        existing));
            }

            _entries.Add(
                idempotencyKey,
                entry);

            return Task.FromResult(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry));
        }
    }

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord current)
        : IPlayerCareerProfileStore
    {
        private PlayerCareerProfileStoreRecord _current =
            current;

        public int SaveCount { get; private set; }

        public int? FailOnSaveAttempt { get; set; }

        public PlayerCareerProfileStoreRecord Current =>
            _current;

        private int _saveAttemptCount;

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PlayerCareerProfileStoreRecord?>(
                _current);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _saveAttemptCount++;

            if (FailOnSaveAttempt == _saveAttemptCount)
            {
                FailOnSaveAttempt =
                    null;
                throw new IOException(
                    "Synthetic Career/Profile persistence failure.");
            }

            if (expectedRevision
                != _current.Revision)
            {
                throw new PlayerCareerProfileConcurrencyException(
                    "Synthetic stale career profile revision.");
            }

            SaveCount++;

            _current =
                new PlayerCareerProfileStoreRecord(
                    checked(_current.Revision + 1),
                    profile,
                    savedAt);

            _current.Validate();

            return Task.FromResult(
                _current);
        }
    }

    private sealed class FakeFleetStore(
        AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup,
          IAircraftReservationStore
    {
        public AircraftReservationOwnership? Ownership { get; private set; } =
            ownership;

        public int ReleaseCount { get; private set; }

        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Ownership is not null
                && string.Equals(
                    Ownership.ReservationId,
                    reservationId,
                    StringComparison.Ordinal)
                    ? Ownership
                    : null);
        }

        public Task<AircraftReservationAcquireResult> TryReserveAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;

            if (Ownership is null)
            {
                return Task.FromResult(
                    AircraftReservationReleaseResult.AlreadyReleased);
            }

            Ownership =
                null;

            return Task.FromResult(
                AircraftReservationReleaseResult.Released);
        }
    }

    private sealed class MemoryCheckpointStore
        : IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public int ClearCount { get; private set; }

        public bool FailNextClear { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint =
                session;
            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Checkpoint);
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FailNextClear)
            {
                FailNextClear =
                    false;
                throw new IOException(
                    "Synthetic checkpoint cleanup failure.");
            }

            ClearCount++;
            Checkpoint =
                null;
            return Task.CompletedTask;
        }
    }
}
