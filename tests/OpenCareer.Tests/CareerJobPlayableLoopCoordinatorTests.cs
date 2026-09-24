using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class CareerJobPlayableLoopCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteRunsAuthoritativeTerminalChainAndReplaySurvivesCleanup()
    {
        TestContext context =
            CreateContext();

        CareerJobPlayableCompletionResult first =
            await context.Coordinator.CompleteAsync(
                context.Request);

        Assert.Equal(
            FlightSessionStatus.Completed,
            first.CompletedFlight?.Status);
        Assert.Equal(
            ContractStatus.Completed,
            first.CompletedContract.Contract.Status);
        Assert.True(
            first.Terminal.Settlement.WasNewlyPosted);
        Assert.Equal(
            LogbookAppendDisposition.Appended,
            first.Terminal.Logbook.Disposition);
        Assert.Equal(
            CareerFlightFinalizationStatus.Finalized,
            first.Terminal.Finalization.Status);
        Assert.Equal(
            "KSYR",
            first.Terminal.CareerProfile.Profile.Location
                .CurrentAirportIcao);
        Assert.Contains(
            first.CompletedContract.Contract.ContractId,
            first.Terminal.CareerProfile.Profile.Location
                .AppliedTravelContracts);

        Assert.Null(
            context.Sessions.Current);
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

        CareerJobPlayableCompletionResult replay =
            await context.Coordinator.CompleteAsync(
                context.Request);

        Assert.Null(
            replay.CompletedFlight);
        Assert.Equal(
            ContractStatus.Completed,
            replay.CompletedContract.Contract.Status);
        Assert.False(
            replay.Terminal.Settlement.WasNewlyPosted);
        Assert.Equal(
            LogbookAppendDisposition.AlreadyExists,
            replay.Terminal.Logbook.Disposition);
        Assert.Equal(
            CareerFlightFinalizationStatus.AlreadyFinalized,
            replay.Terminal.Finalization.Status);

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
    public async Task CompleteRaisesStaleTerminalTimesToContractCompletion()
    {
        TestContext context =
            CreateContext();

        DateTimeOffset staleTerminalTime =
            context.Request.FlightCompletionTime
                .AddMinutes(-1);

        CareerJobPlayableCompletionResult result =
            await context.Coordinator.CompleteAsync(
                context.Request with
                {
                    SettledAt = staleTerminalTime,
                    LogbookCommittedAt = staleTerminalTime,
                    ExperienceSavedAt = staleTerminalTime
                });

        DateTimeOffset completedAt =
            Assert.IsType<DateTimeOffset>(
                result.CompletedContract.Contract.CompletedAt);

        Assert.Equal(
            completedAt,
            result.Terminal.Settlement.Settlement.Transaction.OccurredAt);
        Assert.Equal(
            completedAt,
            result.Terminal.Logbook.Entry.CommittedAt);
        Assert.Equal(
            completedAt,
            result.Terminal.CareerProfile.SavedAt);
        Assert.Equal(
            CareerFlightFinalizationStatus.Finalized,
            result.Terminal.Finalization.Status);
    }

    private static TestContext CreateContext()
    {
        Guid contractId =
            Guid.Parse(
                "a0000000-0000-0000-0000-000000000001");

        PersistedJobContract inProgress =
            InProgressContract(
                contractId);

        var contractStore =
            new FakeContractStore(
                inProgress);

        var lifecycle =
            new JobContractLifecycleService(
                contractStore);

        FlightSession shutdownSession =
            ShutdownSession(
                contractId);

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            shutdownSession);

        var checkpointStore =
            new MemoryCheckpointStore
            {
                Checkpoint =
                    shutdownSession
            };

        var flightPersistence =
            new FlightSessionPersistenceService(
                sessions,
                checkpointStore);

        var evidenceSource =
            new FakeEvidenceSource();

        var evidenceTracker =
            new JobFlightCompletionEvidenceTracker(
                sessions,
                evidenceSource);

        var flightCompletion =
            new JobFlightSessionCompletionBridge(
                evidenceTracker,
                sessions,
                new FlightSessionCompletionService(
                    sessions,
                    flightPersistence));

        var contractCompletion =
            new CompletedJobContractBridge(
                contractStore,
                lifecycle,
                sessions);

        var ledger =
            new FakeLedgerStore();

        var settlement =
            new SettlementPendingContractCoordinator(
                new EconomySettlementService(
                    contractStore,
                    ledger));

        var logbook =
            new FakeLogbookStore();

        var settledLogbook =
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    logbook));

        var profileStore =
            new FakeProfileStore(
                InitialProfile());

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

        var finalization =
            new CareerFlightFinalizationCoordinator(
                new CareerFlightReservationReleaseCoordinator(
                    fleet,
                    fleet),
                flightPersistence,
                sessions);

        var terminal =
            new CareerFlightTerminalWorkflowCoordinator(
                settlement,
                settledLogbook,
                logbook,
                experience,
                location,
                finalization);

        var registry =
            new AircraftRegistryCatalogService(
                Array.Empty<IAircraftRegistryObservationSource>());

        var emptyRecovery =
            new JobContractRuntimeState(
                new JobContractRecoveryService(
                    new EmptyRecoverySource(),
                    contractStore));

        var acceptance =
            new JobOfferAcceptanceService(
                contractStore,
                lifecycle,
                new ThrowingBoardStore(),
                emptyRecovery);

        var reservationCoordinator =
            new AircraftReservationCoordinator(
                registry,
                fleet);

        var acceptanceFleet =
            new JobAcceptanceFleetBridge(
                acceptance,
                contractStore,
                reservationCoordinator);

        var dispatch =
            new AcceptedJobDispatchBridge(
                acceptanceFleet,
                new OperationDispatchPlanningService(
                    registry,
                    new CachedAirportDataSource(
                        Array.Empty<IAirportDataObservationSource>())));

        var flightStart =
            new AcceptedJobFlightSessionBridge(
                new AcceptedJobStartBridge(
                    lifecycle),
                contractStore,
                flightPersistence,
                sessions,
                new FixedLiveAircraftIdentitySource());

        var coordinator =
            new CareerJobPlayableLoopCoordinator(
                dispatch,
                flightStart,
                flightCompletion,
                contractCompletion,
                contractStore,
                terminal);

        DateTimeOffset completedAt =
            shutdownSession.UpdatedAt
                .AddSeconds(1);

        DateTimeOffset settledAt =
            completedAt.AddMinutes(1);

        DateTimeOffset logbookCommittedAt =
            settledAt.AddMinutes(1);

        var request =
            new CareerJobPlayableCompletionRequest(
                contractId,
                completedAt,
                MissionConditionsVerified:
                    true,
                PostFlightTasksVerified:
                    true,
                new ContractSettlementCosts(
                    FuelCost:
                        100m,
                    MaintenanceReserveCost:
                        50m,
                    AirportFees:
                        25m),
                settledAt,
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
            coordinator,
            request,
            contractStore,
            ledger,
            logbook,
            profileStore,
            fleet,
            checkpointStore,
            sessions);
    }

    private static PersistedJobContract InProgressContract(
        Guid contractId)
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
                    ContractStatus.InProgress,
                AcceptedAt:
                    Epoch.AddMinutes(-30),
                StartedAt:
                    Epoch);

        contract.Validate();

        return new(
            contract,
            Version: 2);
    }

    private static PlayerCareerProfileStoreRecord InitialProfile()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "a0000000-0000-0000-0000-000000000099"),
                "KRME",
                Epoch.AddHours(-2));

        return new(
            Revision: 1,
            profile,
            SavedAt:
                Epoch.AddHours(-1));
    }

    private static FlightSession ShutdownSession(
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

        return Advance(
            session,
            7,
            parking:
                true,
            shutdown:
                true);
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
        CareerJobPlayableLoopCoordinator Coordinator,
        CareerJobPlayableCompletionRequest Request,
        FakeContractStore ContractStore,
        FakeLedgerStore Ledger,
        FakeLogbookStore Logbook,
        FakeProfileStore ProfileStore,
        FakeFleetStore Fleet,
        MemoryCheckpointStore CheckpointStore,
        FlightSessionCoordinator Sessions);

    private sealed class FakeContractStore(
        PersistedJobContract current)
        : IJobContractStore
    {
        public PersistedJobContract Current { get; private set; } =
            current;

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PersistedJobContract?>(
                Current.Contract.ContractId
                    == contractId
                        ? Current
                        : null);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contract.Validate();

            if (Current.Version
                != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Synthetic stale contract version.");
            }

            Current =
                new PersistedJobContract(
                    contract,
                    checked(expectedVersion + 1));

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }

    private sealed class EmptyRecoverySource
        : IJobContractRecoverySource
    {
        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<JobContractRecoveryCandidate>>(
                Array.Empty<JobContractRecoveryCandidate>());
        }
    }

    private sealed class ThrowingBoardStore
        : IJobBoardStateStore
    {
        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeEvidenceSource
        : IFlightStateEvidenceSource
    {
        public FlightStateEvidence? Current =>
            null;

        public event Action<FlightStateEvidence?>? EvidenceChanged
        {
            add { }
            remove { }
        }
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
                out EconomyLedgerTransaction? transaction);

            return Task.FromResult(
                transaction);
        }

        public Task<decimal> ReadCashBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _transactions.Values.Sum(
                    transaction =>
                        transaction.CashChange));
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
            SaveCount++;

            if (expectedRevision
                != _current.Revision)
            {
                throw new PlayerCareerProfileConcurrencyException(
                    "Synthetic stale profile revision.");
            }

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
        : IAircraftReservationStore,
          IAircraftReservationLookup
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
            Task.FromResult(
                AircraftReservationAcquireResult.AlreadyHeld);

        public Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Ownership is null)
            {
                return Task.FromResult(
                    AircraftReservationReleaseResult.AlreadyReleased);
            }

            ReleaseCount++;
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
            ClearCount++;
            Checkpoint =
                null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedLiveAircraftIdentitySource
        : ILiveAircraftIdentitySource
    {
        public string? CurrentAircraftTitle =>
            null;
    }
}
