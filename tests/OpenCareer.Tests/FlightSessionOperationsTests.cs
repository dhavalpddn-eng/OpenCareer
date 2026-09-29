using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionOperationsTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletionRequiresMissionAndPostFlightVerification()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.StartAsync(Epoch);

        await AdvanceToShutdownAsync(persistence);

        var service =
            new FlightSessionCompletionService(
                coordinator,
                persistence);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.CompleteAsync(
                    new FlightSessionCompletionRequest(
                        Epoch.AddSeconds(10),
                        MissionConditionsVerified: false,
                        PostFlightTasksVerified: true)));

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);
    }

    [Fact]
    public async Task VerifiedShutdownFlightCompletesExactlyOnce()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.StartAsync(Epoch);

        await AdvanceToShutdownAsync(persistence);

        var service =
            new FlightSessionCompletionService(
                coordinator,
                persistence);

        FlightSession completed =
            await service.CompleteAsync(
                new FlightSessionCompletionRequest(
                    Epoch.AddSeconds(10),
                    MissionConditionsVerified: true,
                    PostFlightTasksVerified: true));

        Assert.Equal(
            FlightSessionStatus.Completed,
            completed.Status);

        Assert.Equal(
            FlightOperationState.Complete,
            completed.OperationState);

        Assert.Equal(
            FlightTrackingState.Complete,
            completed.Tracking.State);

        FlightLeg completedLeg =
            Assert.Single(completed.EffectiveLegs);

        Assert.Equal(FlightLegStatus.Completed, completedLeg.Status);
        Assert.Equal(Epoch.AddSeconds(10), completedLeg.CompletedAt);

        int completedSaveCount = store.SaveCount;

        FlightSession repeated =
            await service.CompleteAsync(
                new FlightSessionCompletionRequest(
                    Epoch.AddSeconds(11),
                    MissionConditionsVerified: true,
                    PostFlightTasksVerified: true));

        Assert.Equal(
            completed,
            repeated);
        Assert.Equal(completedSaveCount, store.SaveCount);
        Assert.Equal(
            completedLeg.CompletedAt,
            Assert.Single(repeated.EffectiveLegs).CompletedAt);
    }

    [Fact]
    public async Task CompletionWriteFailureRetryClosesLegOnceAtSameTimestamp()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.StartAsync(Epoch);
        await AdvanceToShutdownAsync(persistence);

        var service =
            new FlightSessionCompletionService(
                coordinator,
                persistence);

        var request =
            new FlightSessionCompletionRequest(
                Epoch.AddSeconds(10),
                MissionConditionsVerified: true,
                PostFlightTasksVerified: true);

        store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(
            () => service.CompleteAsync(request));

        FlightLeg pendingLeg =
            Assert.Single(coordinator.Current!.EffectiveLegs);
        Assert.Equal(FlightLegStatus.Active, pendingLeg.Status);
        Assert.Null(pendingLeg.CompletedAt);

        store.FailWrites = false;
        FlightSession completed = await service.CompleteAsync(request);
        FlightLeg completedLeg = Assert.Single(completed.EffectiveLegs);

        Assert.Equal(FlightLegStatus.Completed, completedLeg.Status);
        Assert.Equal(Epoch.AddSeconds(10), completedLeg.CompletedAt);
        Assert.Equal(completed, store.Checkpoint);
    }

    [Fact]
    public async Task CapturedCompletionRequestUsesNewerAuthoritativeShutdownState()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.StartAsync(Epoch);
        await AdvanceToShutdownAsync(persistence);

        FlightSession captured =
            Assert.IsType<FlightSession>(coordinator.Current);

        var request =
            new FlightSessionCompletionRequest(
                captured.UpdatedAt,
                MissionConditionsVerified: true,
                PostFlightTasksVerified: true);

        FlightSession newer =
            await persistence.AdvanceAsync(
                Update(
                    8,
                    parking: true,
                    shutdown: true) with
                {
                    Observation =
                        new FlightSessionObservation(
                            Epoch.AddSeconds(8),
                            LatitudeDegrees: 32.9,
                            LongitudeDegrees: -97.0,
                            AltitudeMslFeet: 607,
                            IndicatedAirspeedKnots: 0,
                            GroundSpeedKnots: 0,
                            FuelTotalPounds: 123,
                            PayloadPounds: 456,
                            CaptureTrackPoint: true)
                });

        Assert.True(newer.UpdatedAt > request.Timestamp);
        Assert.Equal(123d, newer.EffectiveStatistics.LastFuelPounds);
        Assert.Equal(456d, newer.EffectiveStatistics.LastPayloadPounds);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => persistence.AdvanceAsync(
                Update(7, parking: true, shutdown: true)));

        var service =
            new FlightSessionCompletionService(
                coordinator,
                persistence);

        FlightSession completed =
            await service.CompleteAsync(request);

        Assert.Equal(FlightSessionStatus.Completed, completed.Status);
        Assert.Equal(newer.UpdatedAt, completed.UpdatedAt);
        Assert.Equal(newer.UpdatedAt, completed.Milestones.CompletedAt);
        Assert.Equal(newer.EffectiveStatistics, completed.EffectiveStatistics);
        Assert.Equal(newer.TimeLedger, completed.TimeLedger);
        Assert.Equal(newer.ContinuityAnchor, completed.ContinuityAnchor);
        Assert.Equal(newer.EffectiveLandingEpisodes, completed.EffectiveLandingEpisodes);
        Assert.Equal(completed, store.Checkpoint);
    }

    [Fact]
    public async Task CompletionTimestampBeforeSessionStillFailsClosed()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                new MemoryStore());

        await persistence.StartAsync(Epoch);
        await AdvanceToShutdownAsync(persistence);

        var service =
            new FlightSessionCompletionService(
                coordinator,
                persistence);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.CompleteAsync(
                new FlightSessionCompletionRequest(
                    Epoch.AddTicks(-1),
                    MissionConditionsVerified: true,
                    PostFlightTasksVerified: true)));
    }

    [Fact]
    public void ContractBridgeCreatesProviderNeutralPlanAndCompletesOnlyMatchingSession()
    {
        JobContract contract =
            Contract();

        FlightSessionPlan plan =
            FlightSessionContractBridge.CreatePlan(
                contract,
                routeText: "DCT TEST",
                sourceProvider: "phpVMS",
                sourceReference: "schedule-77");

        Assert.Equal("KDFW", plan.PlannedOrigin);
        Assert.Equal("KIAH", plan.PlannedDestination);
        Assert.Equal("phpVMS", plan.SourceProvider);
        Assert.Equal("schedule-77", plan.SourceReference);

        FlightSession completed =
            CompletedSession(
                contract.ContractId);

        JobContract finished =
            FlightSessionContractBridge.CompleteContract(
                contract,
                completed);

        Assert.Equal(
            ContractStatus.Completed,
            finished.Status);

        Assert.Equal(
            completed.Milestones.CompletedAt,
            finished.CompletedAt);
    }

    [Fact]
    public void ContractBridgeRejectsDifferentContractIdentity()
    {
        JobContract contract =
            Contract();

        FlightSession completed =
            CompletedSession(
                Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(
            () =>
                FlightSessionContractBridge.CompleteContract(
                    contract,
                    completed));
    }

    private static async Task AdvanceToShutdownAsync(
        FlightSessionPersistenceService persistence)
    {
        await persistence.AdvanceAsync(
            Update(1, stable: true, validAircraft: true));

        await persistence.AdvanceAsync(
            Update(2, movement: true));

        await persistence.AdvanceAsync(
            Update(3, takeoffCandidate: true));

        await persistence.AdvanceAsync(
            Update(4, airborne: true));

        await persistence.AdvanceAsync(
            Update(5, touchdown: true));

        await persistence.AdvanceAsync(
            Update(6, rollout: true));

        await persistence.AdvanceAsync(
            Update(7, parking: true, shutdown: true));
    }

    private static FlightSessionAdvance Update(
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
        new(
            new FlightStateEvidence(
                Epoch.AddSeconds(seconds),
                Connected: true,
                StableTelemetry: stable,
                ValidLoadedAircraft: validAircraft,
                ContinuityPlausible: true,
                SelfPoweredMovementForFlight: movement,
                TakeoffCandidate: takeoffCandidate,
                AirborneConfirmed: airborne,
                TouchdownConfirmed: touchdown,
                LandingRolloutConfirmed: rollout,
                ParkingConfirmed: parking),
            ShutdownConfirmed: shutdown);

    private static JobContract Contract() =>
        new(
            Guid.Parse(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            EmployerId: null,
            Kind: ContractKind.Cargo,
            ServiceTrack: ServiceTrack.CivilianEmployment,
            OriginIcao: "KDFW",
            DestinationIcao: "KIAH",
            Compensation:
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 0,
                    PilotCompensation: 1_000,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
            OfferedAt: Epoch.AddHours(-1),
            MustStartBy: null,
            MustCompleteBy: null,
            AircraftRequirements:
                new AircraftMissionRequirements(),
            Status: ContractStatus.InProgress,
            AcceptedAt: Epoch.AddMinutes(-30),
            StartedAt: Epoch);

    private static FlightSession CompletedSession(
        Guid contractId)
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId);

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    1,
                    stable: true,
                    validAircraft: true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    2,
                    movement: true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    3,
                    takeoffCandidate: true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    4,
                    airborne: true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    5,
                    touchdown: true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    6,
                    rollout: true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    7,
                    parking: true,
                    shutdown: true));

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(8),
                    Connected: true,
                    StableTelemetry: true,
                    ContinuityPlausible: true,
                    OperationCompleteConfirmed: true),
                ShutdownConfirmed: true));
    }

    private sealed class MemoryStore :
        IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public bool FailWrites { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites)
                throw new IOException("Synthetic persistence failure.");

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
}
