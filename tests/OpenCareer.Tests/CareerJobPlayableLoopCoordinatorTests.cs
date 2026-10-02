using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
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
    public async Task DevelopmentCompletionUsesNormalTerminalWorkflowWithoutProgression()
    {
        TestContext context =
            CreateContext(
                development:
                    true);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            CareerJobPlayableCompletionResult result =
                await context.Coordinator.CompleteAsync(
                    context.Request);

            Assert.Equal(
                ContractStatus.Completed,
                result.CompletedContract.Contract.Status);
            Assert.Equal(
                0m,
                result.Terminal.Settlement.Settlement.Transaction
                    .TotalDebits);
            Assert.Equal(
                0m,
                result.Terminal.Settlement.Settlement.Transaction
                    .TotalCredits);
            Assert.Equal(
                0m,
                result.Terminal.Settlement.Settlement
                    .GrossCashReceipt);
            Assert.Equal(
                0m,
                result.Terminal.Settlement.Settlement
                    .PlayerOperatingCosts);
            Assert.Equal(
                0m,
                result.Terminal.Settlement.Settlement
                    .NetCashChange);
            Assert.Equal(
                PilotExperienceTotals.Empty,
                result.Terminal.CareerProfile.Profile.Experience);
            Assert.Equal(
                PilotQualificationState.Entry,
                result.Terminal.CareerProfile.Profile.Qualifications);
            Assert.Contains(
                result.Terminal.Logbook.Entry.Debrief.DebriefId,
                result.Terminal.CareerProfile.Profile
                    .AppliedExperienceDebriefIds);
            Assert.True(
                result.Terminal.CareerProfile.SavedAt
                >= result.Terminal.Logbook.Entry.CommittedAt);
            Assert.Equal(
                "KJFK",
                result.Terminal.CareerProfile.Profile.Location
                    .CurrentAirportIcao);
        }

        Assert.Single(
            context.Logbook.Entries);
        Assert.Equal(
            1,
            context.Ledger.UniquePostCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
        Assert.Equal(
            1,
            context.CheckpointStore.ClearCount);
        Assert.Null(
            context.Sessions.Current);

        LogbookEntry entry =
            Assert.Single(
                context.Logbook.Entries);

        Assert.Equal(
            0,
            entry.Debrief.Settlement.ReputationDelta);

        var restartedExperience =
            new PlayerCareerExperienceCoordinator(
                context.ProfileStore,
                new PlayerCareerRuntimeState(
                    context.ProfileStore),
                context.ContractStore);

        PlayerCareerProfileStoreRecord recovered =
            await restartedExperience.ApplyCommittedAsync(
                entry,
                context.Request.ExperienceSavedAt.AddMinutes(1));

        Assert.Equal(
            PilotExperienceTotals.Empty,
            recovered.Profile.Experience);
        Assert.Equal(
            2,
            context.ProfileStore.SaveCount);
    }

    [Fact]
    public async Task CapturedTerminalRequestAdvancesFromNewerAuthoritativeShutdownState()
    {
        TestContext context =
            CreateContext(
                development: true);

        FlightSession captured =
            Assert.IsType<FlightSession>(context.Sessions.Current);

        CareerJobPlayableCompletionRequest staleRequest =
            context.Request with
            {
                FlightCompletionTime = captured.UpdatedAt,
                SettledAt = captured.UpdatedAt,
                LogbookCommittedAt = captured.UpdatedAt,
                ExperienceSavedAt = captured.UpdatedAt
            };

        FlightSession newer =
            await context.FlightPersistence.AdvanceAsync(
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        captured.UpdatedAt.AddSeconds(1),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: true,
                        ParkingConfirmed: true),
                    ShutdownConfirmed: true,
                    Observation:
                        new FlightSessionObservation(
                            captured.UpdatedAt.AddSeconds(1),
                            LatitudeDegrees: 40.64,
                            LongitudeDegrees: -73.78,
                            AltitudeMslFeet: 13,
                            IndicatedAirspeedKnots: 0,
                            GroundSpeedKnots: 0,
                            FuelTotalPounds: 120,
                            PayloadPounds: 340,
                            CaptureTrackPoint: true)));

        CareerJobPlayableCompletionResult result =
            await context.Coordinator.CompleteAsync(
                staleRequest);

        FlightSession completed =
            Assert.IsType<FlightSession>(result.CompletedFlight);

        Assert.Equal(newer.UpdatedAt, completed.UpdatedAt);
        Assert.Equal(newer.EffectiveStatistics, completed.EffectiveStatistics);
        Assert.Equal(newer.UpdatedAt, result.CompletedContract.Contract.CompletedAt);
        Assert.Equal(newer.UpdatedAt, result.Terminal.Settlement.Settlement.Transaction.OccurredAt);
        Assert.Equal(newer.UpdatedAt, result.Terminal.Logbook.Entry.CommittedAt);
        Assert.Equal(newer.UpdatedAt, result.Terminal.CareerProfile.SavedAt);

        CareerJobPlayableCompletionResult replay =
            await context.Coordinator.CompleteAsync(
                staleRequest);

        Assert.Null(replay.CompletedFlight);
        Assert.False(replay.Terminal.Settlement.WasNewlyPosted);
        Assert.Equal(LogbookAppendDisposition.AlreadyExists, replay.Terminal.Logbook.Disposition);
        Assert.Equal(1, context.Ledger.UniquePostCount);
        Assert.Single(context.Logbook.Entries);
        Assert.Equal(1, context.Fleet.ReleaseCount);
        Assert.Equal(1, context.CheckpointStore.ClearCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task RestartResumesPersistedTerminalWorkflowExactlyOnce(
        int failureWindow)
    {
        TestContext context = CreateContext();

        switch (failureWindow)
        {
            case 0:
                context.Ledger.FailNextPost = true;
                break;
            case 1:
                context.Logbook.FailNextAppend = true;
                break;
            case 2:
                context.ProfileStore.FailNextSave = true;
                break;
            case 3:
                context.Fleet.FailNextRelease = true;
                break;
            case 4:
                context.CheckpointStore.FailNextClear = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failureWindow));
        }

        await Assert.ThrowsAsync<IOException>(
            () => context.Coordinator.CompleteAsync(context.Request));

        Assert.NotNull(context.TerminalRecovery.Pending);

        CareerJobPlayableLoopCoordinator restarted = context.Restart();
        CareerJobPlayableCompletionResult resumed =
            Assert.IsType<CareerJobPlayableCompletionResult>(
                await restarted.ResumePendingAsync());

        Assert.Equal(
            CareerFlightFinalizationStatus.Finalized,
            resumed.Terminal.Finalization.Status);
        Assert.Equal(1, context.Ledger.UniquePostCount);
        Assert.Single(context.Logbook.Entries);
        Assert.Equal(2, context.ProfileStore.SaveCount);
        Assert.Equal(1, context.Fleet.ReleaseCount);
        Assert.Equal(1, context.CheckpointStore.ClearCount);
        Assert.Equal(1, context.TerminalRecovery.ClearCount);
        Assert.Null(context.TerminalRecovery.Pending);
        Assert.Null(context.Sessions.Current);
        Assert.Null(await restarted.ResumePendingAsync());
    }

    private static TestContext CreateContext(
        bool development = false)
    {
        Guid contractId =
            Guid.Parse(
                "a0000000-0000-0000-0000-000000000001");

        PersistedJobContract inProgress =
            InProgressContract(
                contractId);

        if (development)
        {
            JobMarketOfferDraft offer =
                DevelopmentFlight.CreateOffer(
                    contractId,
                    Epoch.AddHours(-1));

            JobMarketContractTermsEnvelope terms =
                Assert.IsType<JobMarketContractTermsEnvelope>(
                    offer.ContractTerms);

            JobContract contract =
                JobContractFactory.Create(
                    new JobContractCreationRequest(
                        offer,
                        Epoch,
                        terms.AircraftRequirements,
                        terms.EstimatedFlightHours,
                        terms.PayloadPounds,
                        terms.DemandAttractiveness,
                        terms.Urgency,
                        terms.Difficulty,
                        terms.EstimatedPlayerOperatingCosts,
                        ReputationReward:
                            terms.ReputationReward,
                        ReputationPenalty:
                            terms.ReputationPenalty,
                        MarketId:
                            terms.MarketId));

            inProgress =
                new PersistedJobContract(
                    contract with
                    {
                        Status = ContractStatus.InProgress,
                        AcceptedAt = Epoch,
                        StartedAt = Epoch
                    },
                    Version:
                        2);
        }

        var contractStore =
            new FakeContractStore(
                inProgress);

        var lifecycle =
            new JobContractLifecycleService(
                contractStore);

        FlightSession shutdownSession =
            ShutdownSession(
                contractId);

        if (development)
        {
            shutdownSession =
                shutdownSession with
                {
                    Plan =
                        new FlightSessionPlan(
                            "KJFK",
                            "KJFK")
                };
        }

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
                development
                    ? new PlayerCareerProfileStoreRecord(
                        Revision:
                            1,
                        PlayerCareerProfile.Start(
                            Guid.Parse(
                                "a0000000-0000-0000-0000-000000000098"),
                            "KJFK",
                            Epoch.AddHours(-2)),
                        SavedAt:
                            Epoch.AddHours(-1))
                    : InitialProfile());

        var profileRuntime =
            new PlayerCareerRuntimeState(
                profileStore);

        var experience =
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    profileStore,
                    profileRuntime,
                    contractStore));

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
                sessions);

        var terminalRecovery =
            new MemoryTerminalRecoveryStore();

        CareerJobPlayableLoopCoordinator CreateCoordinator() =>
            new(
                dispatch,
                flightStart,
                flightCompletion,
                contractCompletion,
                contractStore,
                terminal,
                terminalRecovery);

        CareerJobPlayableLoopCoordinator coordinator =
            CreateCoordinator();

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

        if (development)
        {
            request =
                request with
                {
                    LogbookContext =
                        request.LogbookContext with
                        {
                            ActualDeparture = "KJFK",
                            ActualArrival = "KJFK",
                            ReputationDelta = 0
                        }
                };
        }

        return new(
            coordinator,
            request,
            contractStore,
            ledger,
            logbook,
            profileStore,
            fleet,
            checkpointStore,
            sessions,
            flightPersistence,
            terminalRecovery,
            CreateCoordinator);
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
        FlightSessionCoordinator Sessions,
        FlightSessionPersistenceService FlightPersistence,
        MemoryTerminalRecoveryStore TerminalRecovery,
        Func<CareerJobPlayableLoopCoordinator> Restart);

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

        public bool FailNextPost { get; set; }

        public Task<LedgerPostResult> PostAsync(
            EconomyLedgerTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Validate();

            if (FailNextPost)
            {
                FailNextPost = false;
                throw new IOException("Injected settlement failure.");
            }

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
                FailNextAppend = false;
                throw new IOException("Injected Logbook failure.");
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

        public bool FailNextSave { get; set; }

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

            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Injected Career/Profile failure.");
            }

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

        public bool FailNextRelease { get; set; }

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

            if (FailNextRelease)
            {
                FailNextRelease = false;
                throw new IOException("Injected Fleet release failure.");
            }

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
                FailNextClear = false;
                throw new IOException("Injected checkpoint cleanup failure.");
            }

            ClearCount++;
            Checkpoint =
                null;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryTerminalRecoveryStore
        : ICareerJobTerminalRecoveryStore
    {
        public CareerJobPlayableCompletionRequest? Pending { get; private set; }

        public int ClearCount { get; private set; }

        public Task<CareerJobPlayableCompletionRequest?> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Pending);
        }

        public Task SaveAsync(
            CareerJobPlayableCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Validate();

            if (Pending is not null && Pending != request)
            {
                throw new InvalidOperationException(
                    "Conflicting terminal recovery request.");
            }

            Pending = request;
            return Task.CompletedTask;
        }

        public Task ClearAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Pending is not null && Pending.ContractId != contractId)
            {
                throw new InvalidOperationException(
                    "Terminal recovery contract mismatch.");
            }

            if (Pending is not null)
            {
                Pending = null;
                ClearCount++;
            }

            return Task.CompletedTask;
        }
    }
}
