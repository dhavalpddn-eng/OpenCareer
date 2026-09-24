using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Simulator;
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

    private static readonly string SelectedAircraftId =
        AircraftCanonicalIdentity.FromMsfsTitle(
            "Fixture Aircraft");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulContractStartCreatesPersistentContractLinkedSession(bool development)
    {
        PersistedJobContract accepted =
            AcceptedContract();
        if (development)
            accepted = accepted with { Contract = accepted.Contract with
            {
                MarketId = DevelopmentFlight.MarketId, OriginIcao = "KJFK", DestinationIcao = "KJFK"
            } };
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
                    OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one");

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
            development ? "KJFK" : "KRME",
            result.FlightSession.Plan?.PlannedOrigin);
        Assert.Equal(
            development ? "KJFK" : "KSYR",
            result.FlightSession.Plan?.PlannedDestination);
        Assert.Equal(
            result.FlightSession,
            sessionStore.Checkpoint);
        Assert.Equal(
            result.FlightSession,
            coordinator.Current);
        Assert.Equal(1, contractStore.UpdateCount);
        Assert.Equal(1, sessionStore.SaveCount);
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
        var liveAircraft =
            new FakeLiveAircraftIdentitySource(
                "Fixture Aircraft");
        AcceptedJobFlightSessionBridge bridge =
            CreateBridge(
                contractStore,
                sessionStore,
                coordinator,
                liveAircraft);

        StartedJobFlightSessionResult first =
            await bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one");

        liveAircraft.CurrentAircraftTitle =
            null;

        StartedJobFlightSessionResult replay =
            await bridge.StartAsync(
                DispatchResult(accepted),
                DispatchContext(
                    OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one");

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
    public async Task CorrectProviderAircraftPreservesProviderInstance()
    {
        ProviderAircraftAssignment provider =
            ProviderAircraftAssignment.CreateForOffer(
                Guid.Parse(
                    "96000000-0000-0000-0000-000000000001"),
                new ProviderAircraftType(
                    SelectedAircraftId,
                    "Fixture Aircraft"),
                "KRME");

        PersistedJobContract accepted =
            AcceptedContract(provider);

        StartedJobFlightSessionResult result =
            await CreateBridge(
                    new FakeContractStore(accepted),
                    new MemorySessionStore(),
                    new FlightSessionCoordinator())
                .StartAsync(
                    DispatchResult(accepted),
                    DispatchContext(
                        OfferedAt.AddMinutes(20)),
                    selectedProviderAircraftInstanceId: provider.ProviderAircraftInstanceId);

        Assert.Equal(
            provider,
            result.Contract.Contract.ProviderAircraft);
        Assert.Equal(
            ContractStatus.InProgress,
            result.Contract.Contract.Status);
    }

    [Fact]
    public async Task ReplayCannotSwitchBetweenTwoOwnedInstancesOfSameModel()
    {
        PersistedJobContract accepted = AcceptedContract();
        var contractStore = new FakeContractStore(accepted);
        var checkpoint = new MemorySessionStore();
        var bridge = CreateBridge(contractStore, checkpoint, new FlightSessionCoordinator());
        StartedJobFlightSessionResult first = await bridge.StartAsync(
            DispatchResult(accepted), DispatchContext(OfferedAt.AddMinutes(20)),
            selectedOwnershipId: "O1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.StartAsync(
            DispatchResult(accepted), DispatchContext(OfferedAt.AddMinutes(20)),
            selectedOwnershipId: "O2"));
        Assert.Equal("O1", checkpoint.Checkpoint?.AircraftIdentity?.InstanceId);
        Assert.Equal(first.FlightSession.SessionId, checkpoint.Checkpoint?.SessionId);
        Assert.Equal(1, checkpoint.SaveCount);
        Assert.Equal(1, contractStore.UpdateCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongAircraftBlocksWithoutMutationAndCorrectRetryStartsOnce(bool development)
    {
        ProviderAircraftAssignment provider =
            ProviderAircraftAssignment.CreateForOffer(
                Guid.Parse(
                    "96000000-0000-0000-0000-000000000001"),
                new ProviderAircraftType(
                    SelectedAircraftId,
                    "Fixture Aircraft"),
                "KRME");

        PersistedJobContract accepted =
            AcceptedContract(provider);
        if (development)
        {
            provider = provider with { OriginIcao = "KJFK" };
            accepted = accepted with { Contract = accepted.Contract with
            {
                MarketId = DevelopmentFlight.MarketId, OriginIcao = "KJFK", DestinationIcao = "KJFK",
                ProviderAircraft = provider
            } };
        }
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore();
        var liveAircraft =
            new FakeLiveAircraftIdentitySource(
                "Wrong Aircraft");

        AcceptedJobDispatchResult dispatch =
            DispatchResult(accepted);

        AcceptedJobFlightSessionBridge bridge =
            CreateBridge(
                contractStore,
                sessionStore,
                new FlightSessionCoordinator(),
                liveAircraft);

        InvalidOperationException blocked =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => bridge.StartAsync(
                    dispatch,
                    DispatchContext(
                        OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one"));

        Assert.Contains(
            "Load the selected aircraft in MSFS",
            blocked.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            ContractStatus.Accepted,
            contractStore.Current.Contract.Status);
        Assert.Equal(
            provider,
            contractStore.Current.Contract.ProviderAircraft);
        Assert.Equal(0, contractStore.UpdateCount);
        Assert.Equal(0, sessionStore.SaveCount);

        liveAircraft.CurrentAircraftTitle =
            "Fixture Aircraft";

        StartedJobFlightSessionResult started =
            await bridge.StartAsync(
                dispatch,
                DispatchContext(
                    OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one");

        Assert.Equal(
            ContractStatus.InProgress,
            started.Contract.Contract.Status);
        Assert.Equal(1, contractStore.UpdateCount);
        Assert.Equal(1, sessionStore.SaveCount);
        Assert.Equal(
            JobAcceptanceFleetBridge.GetReservationId(
                accepted.Contract.ContractId),
            dispatch.FleetResult.ReservationId);
    }

    [Fact]
    public async Task MissingLiveAircraftBlocksBeforeMutation()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore();

        InvalidOperationException blocked =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateBridge(
                        contractStore,
                        sessionStore,
                        new FlightSessionCoordinator(),
                        new FakeLiveAircraftIdentitySource(
                            currentAircraftTitle:
                                null))
                    .StartAsync(
                        DispatchResult(accepted),
                        DispatchContext(
                            OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one"));

        Assert.Contains(
            "MSFS is not ready",
            blocked.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, contractStore.UpdateCount);
        Assert.Equal(0, sessionStore.SaveCount);
    }

    [Fact]
    public async Task MissingLiveTitleKeepsSelectedProviderForRetry()
    {
        ProviderAircraftAssignment provider = ProviderAircraftAssignment.CreateForOffer(
            Guid.Parse("96000000-0000-0000-0000-000000000001"),
            new ProviderAircraftType(SelectedAircraftId, "Fixture Aircraft"), "KRME");
        PersistedJobContract accepted = AcceptedContract(provider);
        var contracts = new FakeContractStore(accepted);
        var sessions = new MemorySessionStore();
        var selected = new MemoryAirframeSelectionStore();
        var live = new FakeLiveAircraftIdentitySource(null);
        AcceptedJobFlightSessionBridge bridge = CreateBridge(
            contracts, sessions, new FlightSessionCoordinator(), live, selected);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.StartAsync(
            DispatchResult(accepted), DispatchContext(OfferedAt.AddMinutes(20)),
            selectedProviderAircraftInstanceId: provider.ProviderAircraftInstanceId));

        Assert.Equal(ContractStatus.Accepted, contracts.Current.Contract.Status);
        Assert.Equal(0, contracts.UpdateCount);
        Assert.Equal(0, sessions.SaveCount);
        Assert.Equal(provider.ProviderAircraftInstanceId.ToString("D"), selected.Identity?.InstanceId);

        live.CurrentAircraftTitle = "Fixture Aircraft";
        StartedJobFlightSessionResult retried = await bridge.StartAsync(
            DispatchResult(accepted), DispatchContext(OfferedAt.AddMinutes(20)));
        Assert.Equal(selected.Identity, retried.FlightSession.AircraftIdentity);
        Assert.Equal(1, contracts.UpdateCount);
        Assert.Equal(1, sessions.SaveCount);
    }

    [Fact]
    public async Task UnrecognizedLiveAircraftBlocksBeforeMutation()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var contractStore =
            new FakeContractStore(accepted);
        var sessionStore =
            new MemorySessionStore();

        InvalidOperationException blocked =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateBridge(
                        contractStore,
                        sessionStore,
                        new FlightSessionCoordinator(),
                        new FakeLiveAircraftIdentitySource(
                            "Unknown Aircraft"))
                    .StartAsync(
                        DispatchResult(accepted),
                        DispatchContext(
                            OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one"));

        Assert.Contains(
            "Load the selected aircraft in MSFS",
            blocked.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, contractStore.UpdateCount);
        Assert.Equal(0, sessionStore.SaveCount);
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
                    OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one"));

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
                    OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one");

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
                    OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one"));

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
                        "96000000-0000-0000-0000-000000000010"),
                aircraftIdentity: new FlightSessionAircraftIdentity(
                    FlightSessionAircraftKind.Owned, "ownership-one", SelectedAircraftId));

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
                    coordinator,
                    new FakeLiveAircraftIdentitySource(
                        currentAircraftTitle:
                            null))
                .StartAsync(
                    DispatchResult(accepted),
                    DispatchContext(
                        OfferedAt.AddMinutes(20)),
                    selectedOwnershipId: "ownership-one");

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
        FlightSessionCoordinator coordinator,
        ILiveAircraftIdentitySource? liveAircraft = null,
        IContractAirframeSelectionStore? airframeSelections = null)
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
            coordinator,
            liveAircraft
            ?? new FakeLiveAircraftIdentitySource(
                "Fixture Aircraft"),
            airframeSelections);
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
                SelectedAircraftId);

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

    private static PersistedJobContract AcceptedContract(
        ProviderAircraftAssignment? providerAircraft = null)
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
                    OfferedAt.AddMinutes(10),
                ProviderAircraft:
                    providerAircraft);

        contract.Validate();

        return new(
            contract,
            Version: 1);
    }

    private static ContractDispatchContext DispatchContext(
        DateTimeOffset time) =>
        new(
            time,
            new AircraftCapabilityProfile(
                SelectedAircraftId,
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

    private sealed class FakeLiveAircraftIdentitySource(
        string? currentAircraftTitle)
        : ILiveAircraftIdentitySource
    {
        public string? CurrentAircraftTitle { get; set; } =
            currentAircraftTitle;
    }

    private sealed class MemoryAirframeSelectionStore : IContractAirframeSelectionStore
    {
        public FlightSessionAircraftIdentity? Identity { get; private set; }

        public Task SaveAsync(Guid contractId, FlightSessionAircraftIdentity identity,
            CancellationToken cancellationToken = default)
        {
            if (Identity is not null && Identity != identity)
                throw new InvalidOperationException("Airframe selection changed on replay.");
            Identity = identity;
            return Task.CompletedTask;
        }

        public Task<FlightSessionAircraftIdentity?> ReadAsync(Guid contractId,
            CancellationToken cancellationToken = default) => Task.FromResult(Identity);
    }

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
