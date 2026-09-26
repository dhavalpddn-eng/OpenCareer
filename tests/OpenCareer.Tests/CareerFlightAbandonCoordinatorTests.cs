using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class CareerFlightAbandonCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActiveFlightAbandonCancelsReleasesAndClearsInAuthorityOrder()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        CareerFlightAbandonAvailability availability =
            await fixture.Action.ReadAvailabilityAsync();

        Assert.True(availability.CanAbandon);
        Assert.Equal(
            CareerFlightAbandonAvailabilityState.Ready,
            availability.State);
        Assert.Equal(fixture.SessionId, availability.SessionId);
        Assert.Equal(fixture.ContractId, availability.ContractId);

        CareerFlightAbandonResult result =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.Equal(CareerFlightAbandonStatus.Abandoned, result.Status);
        Assert.False(result.SessionWasAlreadyCancelled);
        Assert.False(result.ContractWasAlreadyCancelled);
        Assert.False(result.ReservationWasAlreadyReleased);
        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
        Assert.Contains(
            fixture.Checkpoints.SavedSessions,
            session =>
                session.SessionId == fixture.SessionId
                && session.Status == FlightSessionStatus.Cancelled
                && session.OperationState == FlightOperationState.Cancelled);
        Assert.Equal(
            ContractStatus.Cancelled,
            fixture.Contracts.Current.Contract.Status);
        Assert.Equal(
            900m,
            fixture.Contracts.Current.Contract.Compensation.GrossCustomerRevenue);
        Assert.Equal(
            300m,
            fixture.Contracts.Current.Contract.Compensation.PilotCompensation);
        Assert.Equal(
            1d,
            fixture.Contracts.Current.Contract.ReputationReward);
        Assert.Equal(
            1d,
            fixture.Contracts.Current.Contract.ReputationPenalty);
        Assert.Null(
            fixture.Contracts.Current.Contract.CompletedAt);
        Assert.Null(fixture.ContractRuntime.Find(fixture.ContractId));
        Assert.Null(
            await fixture.Fleet.FindByReservationIdAsync(
                fixture.ReservationId));
        Assert.NotNull(
            await fixture.Fleet.FindByReservationIdAsync(
                fixture.UnrelatedReservationId));
        Assert.Equal(1, fixture.Fleet.ReleaseCount);
        Assert.Equal(
            new[]
            {
                "session-cancelled",
                "contract-cancelled",
                "reservation-released",
                "checkpoint-cleared"
            },
            fixture.Trace);
    }

    [Fact]
    public async Task ReplayAfterSuccessfulCleanupMakesNoFurtherMutation()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        await fixture.Action.AbandonAsync(
            fixture.SessionId,
            fixture.ContractId);

        int contractUpdates =
            fixture.Contracts.UpdateCount;
        int releases =
            fixture.Fleet.ReleaseCount;
        int clears =
            fixture.Checkpoints.ClearCount;

        CareerFlightAbandonResult replay =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.Equal(
            CareerFlightAbandonStatus.NoActiveSession,
            replay.Status);
        Assert.Equal(contractUpdates, fixture.Contracts.UpdateCount);
        Assert.Equal(releases, fixture.Fleet.ReleaseCount);
        Assert.Equal(clears, fixture.Checkpoints.ClearCount);
    }

    [Fact]
    public async Task CancelledSessionRetryConvergesWithoutReapplyingSessionTransition()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionCancelled();
        fixture.Trace.Clear();
        fixture.Checkpoints.SavedSessions.Clear();

        CareerFlightAbandonResult result =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.True(result.SessionWasAlreadyCancelled);
        Assert.Equal(
            ContractStatus.Cancelled,
            fixture.Contracts.Current.Contract.Status);
        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
        Assert.Single(fixture.Checkpoints.SavedSessions);
        Assert.Equal(
            FlightSessionStatus.Cancelled,
            fixture.Checkpoints.SavedSessions[0].Status);
    }

    [Fact]
    public async Task AlreadyCancelledContractRetiresStaleRuntimeWithoutSecondWrite()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetContractCancelled();

        CareerFlightAbandonResult result =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.True(result.ContractWasAlreadyCancelled);
        Assert.Equal(0, fixture.Contracts.UpdateCount);
        Assert.Null(fixture.ContractRuntime.Find(fixture.ContractId));
        Assert.Null(fixture.Sessions.Current);
        Assert.Equal(1, fixture.Fleet.ReleaseCount);
    }

    [Fact]
    public async Task RetryAfterReleaseAndClearFailureDoesNotReleaseTwice()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.Checkpoints.FailNextClear = true;

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId));

        Assert.NotNull(fixture.Sessions.Current);
        Assert.Equal(
            FlightSessionStatus.Cancelled,
            fixture.Sessions.Current!.Status);
        Assert.NotNull(fixture.Checkpoints.Checkpoint);
        Assert.Equal(
            ContractStatus.Cancelled,
            fixture.Contracts.Current.Contract.Status);
        Assert.Equal(1, fixture.Fleet.ReleaseCount);

        CareerFlightAbandonResult retry =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.True(retry.SessionWasAlreadyCancelled);
        Assert.True(retry.ContractWasAlreadyCancelled);
        Assert.True(retry.ReservationWasAlreadyReleased);
        Assert.Equal(1, fixture.Fleet.ReleaseCount);
        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MismatchedIdentityIsRejectedBeforeMutation(
        bool mismatchSession)
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        Guid sessionId =
            mismatchSession
                ? Guid.NewGuid()
                : fixture.SessionId;
        Guid contractId =
            mismatchSession
                ? fixture.ContractId
                : Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Action.AbandonAsync(
                sessionId,
                contractId));

        Assert.Equal(0, fixture.Contracts.UpdateCount);
        Assert.Equal(0, fixture.Fleet.ReleaseCount);
        Assert.Equal(0, fixture.Checkpoints.ClearCount);
        Assert.Equal(
            FlightSessionStatus.Active,
            fixture.Sessions.Current!.Status);
        Assert.NotNull(
            await fixture.Fleet.FindByReservationIdAsync(
                fixture.UnrelatedReservationId));
    }

    [Fact]
    public async Task InterruptedFlightIsDiscardedWithoutRewritingTerminalState()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionInterrupted();
        FlightSession interrupted =
            fixture.Sessions.Current!;

        CareerFlightAbandonAvailability availability =
            await fixture.Action.ReadAvailabilityAsync();

        Assert.True(availability.CanAbandon);
        Assert.Equal(
            CareerFlightAbandonAvailabilityState.CleanupPending,
            availability.State);
        Assert.Equal(fixture.SessionId, availability.SessionId);
        Assert.Equal(fixture.ContractId, availability.ContractId);
        Assert.Contains(
            "interrupted flight will be discarded",
            availability.Detail,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "no settlement",
            availability.Detail,
            StringComparison.OrdinalIgnoreCase);

        CareerFlightAbandonResult result =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.Equal(CareerFlightAbandonStatus.Abandoned, result.Status);
        Assert.False(result.SessionWasAlreadyCancelled);
        Assert.False(result.ContractWasAlreadyCancelled);
        Assert.False(result.ReservationWasAlreadyReleased);
        Assert.Empty(fixture.Checkpoints.SavedSessions);
        Assert.Equal(interrupted, fixture.Checkpoints.LastClearAttempt);
        Assert.Equal(
            FlightSessionStatus.Interrupted,
            fixture.Checkpoints.LastClearAttempt!.Status);
        Assert.Equal(
            FlightTrackingState.Interrupted,
            fixture.Checkpoints.LastClearAttempt.Tracking.State);
        Assert.Equal(
            interrupted.OperationState,
            fixture.Checkpoints.LastClearAttempt.OperationState);
        Assert.Equal(
            interrupted.Milestones.InterruptedAt,
            fixture.Checkpoints.LastClearAttempt.Milestones.InterruptedAt);
        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
        Assert.Equal(
            ContractStatus.Cancelled,
            fixture.Contracts.Current.Contract.Status);
        Assert.Equal(
            900m,
            fixture.Contracts.Current.Contract.Compensation.GrossCustomerRevenue);
        Assert.Equal(
            300m,
            fixture.Contracts.Current.Contract.Compensation.PilotCompensation);
        Assert.Equal(
            1d,
            fixture.Contracts.Current.Contract.ReputationReward);
        Assert.Equal(
            1d,
            fixture.Contracts.Current.Contract.ReputationPenalty);
        Assert.Null(fixture.Contracts.Current.Contract.CompletedAt);
        Assert.Null(fixture.ContractRuntime.Find(fixture.ContractId));
        Assert.Equal(1, fixture.Fleet.ReleaseCount);
        Assert.NotNull(
            await fixture.Fleet.FindByReservationIdAsync(
                fixture.UnrelatedReservationId));
        Assert.Equal(
            new[]
            {
                "contract-cancelled",
                "reservation-released",
                "checkpoint-cleared"
            },
            fixture.Trace);
    }

    [Fact]
    public async Task InterruptedFlightWithCancelledContractRetiresRuntimeAndClears()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionInterrupted();
        fixture.SetContractCancelled();

        CareerFlightAbandonResult result =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.True(result.ContractWasAlreadyCancelled);
        Assert.Equal(0, fixture.Contracts.UpdateCount);
        Assert.Empty(fixture.Checkpoints.SavedSessions);
        Assert.Equal(
            FlightSessionStatus.Interrupted,
            fixture.Checkpoints.LastClearAttempt!.Status);
        Assert.Null(fixture.ContractRuntime.Find(fixture.ContractId));
        Assert.Equal(1, fixture.Fleet.ReleaseCount);
        Assert.Null(fixture.Sessions.Current);
    }

    [Fact]
    public async Task InterruptedFlightWithReleasedReservationDoesNotReleaseAnotherAircraft()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionInterrupted();
        fixture.Fleet.Remove(fixture.ReservationId);

        CareerFlightAbandonResult result =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.True(result.ReservationWasAlreadyReleased);
        Assert.Equal(0, fixture.Fleet.ReleaseCount);
        Assert.NotNull(
            await fixture.Fleet.FindByReservationIdAsync(
                fixture.UnrelatedReservationId));
        Assert.Null(fixture.Sessions.Current);
        Assert.Equal(
            ContractStatus.Cancelled,
            fixture.Contracts.Current.Contract.Status);
    }

    [Fact]
    public async Task InterruptedCleanupFailureRetriesWithoutCancellationOrSecondRelease()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionInterrupted();
        fixture.Checkpoints.FailNextClear = true;

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId));

        Assert.Equal(
            FlightSessionStatus.Interrupted,
            fixture.Sessions.Current!.Status);
        Assert.Equal(
            FlightTrackingState.Interrupted,
            fixture.Sessions.Current.Tracking.State);
        Assert.Equal(
            FlightSessionStatus.Interrupted,
            fixture.Checkpoints.Checkpoint!.Status);
        Assert.Empty(fixture.Checkpoints.SavedSessions);
        Assert.Equal(
            ContractStatus.Cancelled,
            fixture.Contracts.Current.Contract.Status);
        Assert.Equal(1, fixture.Contracts.UpdateCount);
        Assert.Equal(1, fixture.Fleet.ReleaseCount);

        CareerFlightAbandonResult retry =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.False(retry.SessionWasAlreadyCancelled);
        Assert.True(retry.ContractWasAlreadyCancelled);
        Assert.True(retry.ReservationWasAlreadyReleased);
        Assert.Equal(1, fixture.Contracts.UpdateCount);
        Assert.Equal(1, fixture.Fleet.ReleaseCount);
        Assert.Equal(2, fixture.Checkpoints.ClearCount);
        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
    }

    [Fact]
    public async Task InterruptedIdentityMismatchIsRejectedBeforeCleanup()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionInterrupted();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Action.AbandonAsync(
                fixture.SessionId,
                Guid.NewGuid()));

        Assert.Equal(0, fixture.Contracts.UpdateCount);
        Assert.Equal(0, fixture.Fleet.ReleaseCount);
        Assert.Equal(0, fixture.Checkpoints.ClearCount);
        Assert.Equal(
            FlightSessionStatus.Interrupted,
            fixture.Sessions.Current!.Status);
    }

    [Fact]
    public async Task CompletedFlightCannotBeAbandoned()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionTerminal(
            FlightSessionStatus.Completed);

        CareerFlightAbandonAvailability availability =
            await fixture.Action.ReadAvailabilityAsync();

        Assert.False(availability.CanAbandon);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId));

        Assert.Equal(0, fixture.Contracts.UpdateCount);
        Assert.Equal(0, fixture.Fleet.ReleaseCount);
        Assert.Equal(0, fixture.Checkpoints.ClearCount);
    }

    [Fact]
    public async Task NoCurrentSessionIsUnavailableAndExecutionIsHarmless()
    {
        Fixture fixture =
            await Fixture.CreateAsync();

        fixture.SetSessionCancelled();
        await fixture.Persistence.ClearTerminalAsync(
            fixture.SessionId,
            fixture.ContractId);

        int clearCount = fixture.Checkpoints.ClearCount;

        CareerFlightAbandonAvailability availability =
            await fixture.Action.ReadAvailabilityAsync();
        CareerFlightAbandonResult result =
            await fixture.Action.AbandonAsync(
                fixture.SessionId,
                fixture.ContractId);

        Assert.False(availability.CanAbandon);
        Assert.Equal(
            CareerFlightAbandonStatus.NoActiveSession,
            result.Status);
        Assert.Equal(0, fixture.Contracts.UpdateCount);
        Assert.Equal(0, fixture.Fleet.ReleaseCount);
        Assert.Equal(clearCount, fixture.Checkpoints.ClearCount);
    }

    [Fact]
    public async Task DevelopmentFlightUsesSameCleanupAndKeepsZeroProgressionTerms()
    {
        Fixture fixture =
            await Fixture.CreateAsync(
                developmentFlight: true);

        await fixture.Action.AbandonAsync(
            fixture.SessionId,
            fixture.ContractId);

        JobContract cancelled =
            fixture.Contracts.Current.Contract;

        Assert.True(DevelopmentFlight.IsDevelopment(cancelled));
        Assert.Equal(0m, cancelled.Compensation.GrossCustomerRevenue);
        Assert.Equal(0m, cancelled.Compensation.PilotCompensation);
        Assert.Equal(0d, cancelled.ReputationReward);
        Assert.Equal(0d, cancelled.ReputationPenalty);
        Assert.Equal(ContractStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.CompletedAt);
        Assert.Null(fixture.Sessions.Current);
        Assert.Equal(1, fixture.Fleet.ReleaseCount);
    }

    private sealed class Fixture
    {
        private Fixture(
            Guid sessionId,
            Guid contractId,
            string reservationId,
            string unrelatedReservationId,
            List<string> trace,
            FlightSessionCoordinator sessions,
            FlightSessionPersistenceService persistence,
            FakeCheckpointStore checkpoints,
            FakeContractStore contracts,
            JobContractRuntimeState contractRuntime,
            FakeFleet fleet,
            CareerFlightAbandonCoordinator action)
        {
            SessionId = sessionId;
            ContractId = contractId;
            ReservationId = reservationId;
            UnrelatedReservationId = unrelatedReservationId;
            Trace = trace;
            Sessions = sessions;
            Persistence = persistence;
            Checkpoints = checkpoints;
            Contracts = contracts;
            ContractRuntime = contractRuntime;
            Fleet = fleet;
            Action = action;
        }

        public Guid SessionId { get; }
        public Guid ContractId { get; }
        public string ReservationId { get; }
        public string UnrelatedReservationId { get; }
        public List<string> Trace { get; }
        public FlightSessionCoordinator Sessions { get; }
        public FlightSessionPersistenceService Persistence { get; }
        public FakeCheckpointStore Checkpoints { get; }
        public FakeContractStore Contracts { get; }
        public JobContractRuntimeState ContractRuntime { get; }
        public FakeFleet Fleet { get; }
        public CareerFlightAbandonCoordinator Action { get; }

        public static async Task<Fixture> CreateAsync(
            bool developmentFlight = false)
        {
            Guid contractId = Guid.NewGuid();
            Guid sessionId = Guid.NewGuid();
            var trace = new List<string>();

            JobContract contract =
                CreateInProgressContract(
                    contractId,
                    developmentFlight);

            var contracts =
                new FakeContractStore(
                    new PersistedJobContract(
                        contract,
                        Version: 2),
                    trace);

            var recovery =
                new SingleRecoverySource(
                    contracts.Current);

            var contractRuntime =
                new JobContractRuntimeState(
                    new JobContractRecoveryService(
                        recovery,
                        contracts));

            await contractRuntime.InitializeAsync();

            FlightSession session =
                FlightSession.Start(
                    Epoch.AddMinutes(2),
                    contractId,
                    sessionId);

            var sessions =
                new FlightSessionCoordinator();
            sessions.Restore(session);

            var checkpoints =
                new FakeCheckpointStore(
                    session,
                    trace);

            var persistence =
                new FlightSessionPersistenceService(
                    sessions,
                    checkpoints);

            string reservationId =
                JobAcceptanceFleetBridge.GetReservationId(
                    contractId);
            string unrelatedReservationId =
                JobAcceptanceFleetBridge.GetReservationId(
                    Guid.NewGuid());

            var fleet =
                new FakeFleet(trace);
            fleet.Add(
                "msfs-title:Cessna 172 Skyhawk",
                reservationId);
            fleet.Add(
                "msfs-title:Cessna 208B Grand Caravan",
                unrelatedReservationId);

            var action =
                new CareerFlightAbandonCoordinator(
                    sessions,
                    persistence,
                    contracts,
                    new JobContractLifecycleService(
                        contracts),
                    contractRuntime,
                    fleet,
                    fleet,
                    new FixedTimeProvider(
                        Epoch.AddMinutes(30)),
                    NullLogger<CareerFlightAbandonCoordinator>.Instance);

            return new(
                sessionId,
                contractId,
                reservationId,
                unrelatedReservationId,
                trace,
                sessions,
                persistence,
                checkpoints,
                contracts,
                contractRuntime,
                fleet,
                action);
        }

        public void SetSessionCancelled()
        {
            FlightSession current = Sessions.Current!;
            FlightSession cancelled =
                FlightSessionEngine.Advance(
                    current,
                    new FlightSessionAdvance(
                        new FlightStateEvidence(
                            current.UpdatedAt.AddSeconds(1),
                            Connected: false),
                        CancelRequested: true));

            Sessions.CommitPersisted(cancelled);
            Checkpoints.Checkpoint = cancelled;
        }

        public void SetSessionInterrupted()
        {
            FlightSession current = Sessions.Current!;
            FlightSession interrupted =
                FlightSessionEngine.Advance(
                    current,
                    new FlightSessionAdvance(
                        new FlightStateEvidence(
                            current.UpdatedAt.AddSeconds(1),
                            Connected: true,
                            CrashReported: true)));

            Sessions.CommitPersisted(interrupted);
            Checkpoints.Checkpoint = interrupted;
        }

        public void SetContractCancelled()
        {
            Contracts.SetCurrent(
                new PersistedJobContract(
                    Contracts.Current.Contract.Cancel(),
                    Contracts.Current.Version + 1));
        }

        public void SetSessionTerminal(
            FlightSessionStatus status)
        {
            FlightSession terminal =
                Sessions.Current! with
                {
                    Status = status,
                    OperationState =
                        status == FlightSessionStatus.Completed
                            ? FlightOperationState.Complete
                            : Sessions.Current!.OperationState,
                    UpdatedAt = Sessions.Current!.UpdatedAt.AddMinutes(1)
                };

            Sessions.CommitPersisted(terminal);
            Checkpoints.Checkpoint = terminal;
        }
    }

    private static JobContract CreateInProgressContract(
        Guid contractId,
        bool developmentFlight)
    {
        ContractCompensation compensation =
            developmentFlight
                ? new(
                    CompensationModel.MissionFee,
                    GrossCustomerRevenue: 0m,
                    PilotCompensation: 0m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true)
                : new(
                    CompensationModel.MissionFee,
                    GrossCustomerRevenue: 900m,
                    PilotCompensation: 300m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true);

        JobContract contract =
            new(
                ContractId: contractId,
                EmployerId: null,
                Kind: ContractKind.Ferry,
                ServiceTrack: ServiceTrack.IndependentContract,
                OriginIcao: developmentFlight ? "KJFK" : "KRME",
                DestinationIcao: developmentFlight ? "KJFK" : "KSYR",
                Compensation: compensation,
                OfferedAt: Epoch,
                MustStartBy: Epoch.AddHours(1),
                MustCompleteBy: Epoch.AddHours(3),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        AircraftCapability.Passenger,
                        AircraftAccess.Civilian,
                        MinimumPayloadPounds: 0,
                        MinimumRangeNauticalMiles: 0,
                        MinimumSeats: 1),
                Status: ContractStatus.InProgress,
                ReputationReward: developmentFlight ? 0 : 1,
                ReputationPenalty: developmentFlight ? 0 : 1,
                MarketId:
                    developmentFlight
                        ? DevelopmentFlight.MarketId
                        : "test-market",
                AcceptedAt: Epoch.AddMinutes(1),
                StartedAt: Epoch.AddMinutes(2));

        contract.Validate();
        return contract;
    }

    private sealed class FakeCheckpointStore(
        FlightSession checkpoint,
        List<string> trace)
        : IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; } = checkpoint;
        public List<FlightSession> SavedSessions { get; } = [];
        public FlightSession? LastClearAttempt { get; private set; }
        public int ClearCount { get; private set; }
        public bool FailNextClear { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint = session;
            SavedSessions.Add(session);

            if (session.Status == FlightSessionStatus.Cancelled)
                trace.Add("session-cancelled");

            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Checkpoint);
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastClearAttempt = Checkpoint;
            ClearCount++;

            if (FailNextClear)
            {
                FailNextClear = false;
                throw new IOException("Synthetic checkpoint clear failure.");
            }

            Checkpoint = null;
            trace.Add("checkpoint-cleared");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeContractStore(
        PersistedJobContract current,
        List<string> trace)
        : IJobContractStore
    {
        public PersistedJobContract Current { get; private set; } = current;
        public int UpdateCount { get; private set; }

        public void SetCurrent(PersistedJobContract persisted) =>
            Current = persisted;

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PersistedJobContract?>(Current);
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

            if (expectedVersion != Current.Version)
                throw new InvalidOperationException("Synthetic optimistic conflict.");

            if (contract == Current.Contract)
            {
                return Task.FromResult(
                    JobContractSaveResult.AlreadySaved);
            }

            UpdateCount++;
            Current =
                new PersistedJobContract(
                    contract,
                    checked(expectedVersion + 1));

            if (contract.Status == ContractStatus.Cancelled)
                trace.Add("contract-cancelled");

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }

    private sealed class FakeFleet(List<string> trace)
        : IAircraftReservationLookup,
            IAircraftReservationStore
    {
        private readonly Dictionary<string, AircraftReservationOwnership>
            _byReservation = new(StringComparer.Ordinal);

        public int ReleaseCount { get; private set; }

        public void Add(
            string aircraftId,
            string reservationId) =>
            _byReservation[reservationId] =
                new AircraftReservationOwnership(
                    aircraftId,
                    reservationId);

        public void Remove(string reservationId) =>
            _byReservation.Remove(reservationId);

        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _byReservation.TryGetValue(
                reservationId,
                out AircraftReservationOwnership? ownership);
            return Task.FromResult(ownership);
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

            if (!_byReservation.TryGetValue(
                    reservationId,
                    out AircraftReservationOwnership? ownership))
            {
                return Task.FromResult(
                    AircraftReservationReleaseResult.AlreadyReleased);
            }

            if (!string.Equals(
                    ownership.CanonicalAircraftId,
                    canonicalAircraftId,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    AircraftReservationReleaseResult.HeldByAnotherReservation);
            }

            ReleaseCount++;
            _byReservation.Remove(reservationId);
            trace.Add("reservation-released");
            return Task.FromResult(
                AircraftReservationReleaseResult.Released);
        }
    }

    private sealed class SingleRecoverySource(
        PersistedJobContract contract)
        : IJobContractRecoverySource
    {
        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<JobContractRecoveryCandidate> candidates =
                [
                    new(
                        contract.Contract.ContractId,
                        contract.Contract.Status,
                        contract.Version,
                        contract.Contract.StartedAt!.Value)
                ];

            return Task.FromResult(candidates);
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            utcNow;
    }
}
