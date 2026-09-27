using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class AcceptedJobFlightSessionBridgeTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuccessfulContractStartCreatesPersistentContractLinkedSession()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore();
        var coordinator =
            new FlightSessionCoordinator();

        AcceptedJobFlightSessionBridge bridge =
            CreateBridge(
                contractStore,
                sessionStore,
                coordinator);

        StartedJobFlightSessionResult result =
            await bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20)));

        Assert.Equal(
            ContractStatus.InProgress,
            result.Contract.Contract.Status);
        Assert.Equal(
            accepted.Contract.ContractId,
            result.FlightSession.ContractId);
        Assert.Equal(
            accepted.Contract.ContractId,
            result.FlightSession.SessionId);
        Assert.Equal(
            "KRME",
            result.FlightSession.Plan?.PlannedOrigin);
        Assert.Equal(
            "KSYR",
            result.FlightSession.Plan?.PlannedDestination);
        Assert.Equal(
            result.FlightSession,
            sessionStore.Checkpoint);
        Assert.Equal(
            result.FlightSession,
            coordinator.Current);
        Assert.Equal(1, contractStore.UpdateCount);
        Assert.Equal(1, sessionStore.SaveCount);
        Assert.Equal("canonical-aircraft", result.FlightSession.AircraftIdentity?.CanonicalAircraftId);
        Assert.Null(result.FlightSession.AircraftIdentity!.PhysicalAirframeId);
    }

    [Fact]
    public async Task DevelopmentContractUsesNormalPersistentFlightSessionStart()
    {
        PersistedJobContract accepted =
            AcceptedDevelopmentContract();
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore();
        var coordinator =
            new FlightSessionCoordinator();

        StartedJobFlightSessionResult result =
            await CreateBridge(
                    contractStore,
                    sessionStore,
                    coordinator)
                .StartAsync(
                    DispatchResult(accepted),
                    DispatchContext(
                        OfferedAt.AddMinutes(20)));

        Assert.Equal(
            ContractStatus.InProgress,
            result.Contract.Contract.Status);
        Assert.Equal(
            accepted.Contract.ContractId,
            result.FlightSession.ContractId);
        Assert.Equal(
            accepted.Contract.ContractId,
            result.FlightSession.SessionId);
        Assert.Equal(
            "KJFK",
            result.FlightSession.Plan?.PlannedOrigin);
        Assert.Equal(
            "KJFK",
            result.FlightSession.Plan?.PlannedDestination);
        Assert.Equal(
            result.FlightSession,
            sessionStore.Checkpoint);
        Assert.Equal(
            result.FlightSession,
            coordinator.Current);
        Assert.Equal(
            1,
            contractStore.UpdateCount);
        Assert.Equal(
            1,
            sessionStore.SaveCount);
    }

    [Fact]
    public async Task ReplayReusesExistingSessionAndDoesNotRestartContract()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore();
        var coordinator =
            new FlightSessionCoordinator();
        AcceptedJobFlightSessionBridge bridge =
            CreateBridge(
                contractStore,
                sessionStore,
                coordinator);

        StartedJobFlightSessionResult first =
            await bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20)));

        StartedJobFlightSessionResult replay =
            await bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20)));

        Assert.Equal(
            first.Contract,
            replay.Contract);
        Assert.Same(
            coordinator.Current,
            replay.FlightSession);
        Assert.Equal(
            first.FlightSession.SessionId,
            replay.FlightSession.SessionId);
        Assert.Equal(1, contractStore.UpdateCount);
        Assert.Equal(1, sessionStore.SaveCount);
    }

    [Fact]
    public async Task FailedSessionPersistenceLeavesStartedContractRecoverableOnRetry()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore
            {
                FailNextSave = true
            };
        var coordinator =
            new FlightSessionCoordinator();
        AcceptedJobFlightSessionBridge bridge =
            CreateBridge(
                contractStore,
                sessionStore,
                coordinator);

        await Assert.ThrowsAsync<IOException>(
            () => bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20))));

        Assert.Equal(
            ContractStatus.InProgress,
            contractStore.Current.Contract.Status);
        Assert.Null(coordinator.Current);
        Assert.Null(sessionStore.Checkpoint);
        Assert.Equal(1, contractStore.UpdateCount);

        StartedJobFlightSessionResult recovered =
            await bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20)));

        Assert.Equal(
            ContractStatus.InProgress,
            recovered.Contract.Contract.Status);
        Assert.Equal(
            accepted.Contract.ContractId,
            recovered.FlightSession.ContractId);
        Assert.Equal(1, contractStore.UpdateCount);
        Assert.Equal(1, sessionStore.SaveCount);
    }

    [Fact]
    public async Task UnrelatedActiveSessionBlocksBeforeContractMutation()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore();
        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(
            FlightSession.Start(
                OfferedAt,
                contractId:
                    Guid.Parse(
                        "96000000-0000-0000-0000-000000000099"),
                sessionId:
                    Guid.Parse(
                        "96000000-0000-0000-0000-000000000098")));

        AcceptedJobFlightSessionBridge bridge =
            CreateBridge(
                contractStore,
                sessionStore,
                coordinator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20))));

        Assert.Equal(
            ContractStatus.Accepted,
            contractStore.Current.Contract.Status);
        Assert.Equal(0, contractStore.UpdateCount);
        Assert.Equal(0, sessionStore.SaveCount);
    }

    [Fact]
    public async Task MatchingRecoveredSessionAndInProgressContractConvergeWithoutNewWrites()
    {
        PersistedJobContract accepted =
            AcceptedContract();

        JobContract inProgress =
            accepted.Contract with
            {
                Status =
                    ContractStatus.InProgress,
                StartedAt =
                    OfferedAt.AddMinutes(20)
            };

        var contractStore =
            new FakeContractStore(
                new PersistedJobContract(
                    inProgress,
                    Version: 2));

        FlightSession recovered =
            FlightSession.Start(
                OfferedAt.AddMinutes(20),
                accepted.Contract.ContractId,
                sessionId:
                    Guid.Parse(
                        "96000000-0000-0000-0000-000000000010"));

        var coordinator =
            new FlightSessionCoordinator();
        coordinator.Restore(recovered);

        var sessionStore =
            new MemorySessionStore
            {
                Checkpoint =
                    recovered
            };

        StartedJobFlightSessionResult result =
            await CreateBridge(
                    contractStore,
                    sessionStore,
                    coordinator)
                .StartAsync(
                    DispatchResult(accepted),
                    DispatchContext(
                        OfferedAt.AddMinutes(20)));

        Assert.Equal(
            ContractStatus.InProgress,
            result.Contract.Contract.Status);
        Assert.Equal(
            recovered.SessionId,
            result.FlightSession.SessionId);
        Assert.Equal(0, contractStore.UpdateCount);
        Assert.Equal(0, sessionStore.SaveCount);
    }

    private static AcceptedJobFlightSessionBridge CreateBridge(
        FakeContractStore contractStore,
        MemorySessionStore sessionStore,
        FlightSessionCoordinator coordinator)
    {
        var lifecycle =
            new JobContractLifecycleService(
                contractStore);

        var contractStart =
            new AcceptedJobStartBridge(
                lifecycle);

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                sessionStore);

        return new(
            contractStart,
            contractStore,
            persistence,
            coordinator);
    }

    private static AcceptedJobDispatchResult DispatchResult(
        PersistedJobContract accepted)
    {
        var fleet =
            new JobAcceptanceFleetResult(
                JobAcceptanceFleetStatus.AcceptedAndReserved,
                accepted,
                JobAcceptanceFleetBridge.GetReservationId(
                    accepted.Contract.ContractId),
                "canonical-aircraft");

        DispatchFeasibilityResult dispatch =
            DispatchFeasibilityResult.Create(
                DispatchFeasibilityStatus.Feasible,
                "18",
                "36",
                Array.Empty<DispatchFeasibilityIssue>());

        return new(
            fleet,
            dispatch);
    }

    private static PersistedJobContract AcceptedContract()
    {
        var contract =
            new JobContract(
                ContractId:
                    Guid.Parse(
                        "96000000-0000-0000-0000-000000000001"),
                EmployerId:
                    null,
                Kind:
                    ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CivilianEmployment,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.PilotWage,
                        1_200m,
                        450m,
                        true,
                        true,
                        true),
                OfferedAt:
                    OfferedAt,
                MustStartBy:
                    OfferedAt.AddHours(2),
                MustCompleteBy:
                    OfferedAt.AddHours(4),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumPayloadPounds:
                            500,
                        MinimumRangeNauticalMiles:
                            150,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.Accepted,
                AcceptedAt:
                    OfferedAt.AddMinutes(10));

        contract.Validate();

        return new(
            contract,
            Version: 1);
    }

    private static PersistedJobContract AcceptedDevelopmentContract()
    {
        JobMarketOfferDraft offer =
            DevelopmentFlight.CreateOffer(
                Guid.Parse(
                    "96000000-0000-0000-0000-000000000090"),
                OfferedAt);

        JobMarketContractTermsEnvelope terms =
            Assert.IsType<JobMarketContractTermsEnvelope>(
                offer.ContractTerms);

        var request =
            new JobContractCreationRequest(
                offer,
                OfferedAt.AddMinutes(10),
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
                    terms.MarketId);

        JobContract accepted =
            JobContractFactory
                .Create(request)
                .Accept(
                    DispatchContext(
                        request.AcceptanceTime) with
                    {
                        DispatchFeasibilityVerified =
                            true
                    });

        return new(
            accepted,
            Version:
                1);
    }

    private static ContractDispatchContext DispatchContext(
        DateTimeOffset time) =>
        new(
            time,
            new AircraftCapabilityProfile(
                "provider-aircraft",
                "Cargo fixture",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                2_000,
                1_000,
                150,
                4,
                1,
                true,
                false,
                false),
            AircraftAccess.Civilian,
            new WorldEventEffects(),
            QualificationsVerified:
                true,
            DispatchFeasibilityVerified:
                false);

    private sealed class FakeContractStore(
        PersistedJobContract initial)
        : IJobContractStore
    {
        public PersistedJobContract Current { get; private set; } =
            initial;

        public int UpdateCount { get; private set; }

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
            UpdateCount++;

            if (Current.Version
                != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Version conflict.");
            }

            Current =
                new PersistedJobContract(
                    contract,
                    checked(expectedVersion + 1));

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }

    private sealed class MemorySessionStore
        : IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public bool FailNextSave { get; set; }

        public int SaveCount { get; private set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException(
                    "Synthetic FlightSession persistence failure.");
            }

            SaveCount++;
            Checkpoint = session;
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
            Checkpoint = null;
            return Task.CompletedTask;
        }
    }
}
