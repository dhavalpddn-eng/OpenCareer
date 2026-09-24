using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class CareerJobPreFlightSessionRecoveryServiceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AcceptedAt =
        OfferedAt.AddMinutes(10);

    private static readonly DateTimeOffset StartedAt =
        OfferedAt.AddMinutes(20);

    private static readonly string FixtureAircraftId =
        AircraftCanonicalIdentity.FromMsfsTitle(
            "Fixture Aircraft");

    [Fact]
    public async Task AcceptedContractRecoversWithoutRetiredBoardOffer()
    {
        TestContext context =
            CreateContext(
                Persisted(
                    ContractStatus.Accepted),
                hasReservation:
                    true,
                now:
                    StartedAt);

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.True(
            result.Recovered);
        Assert.Equal(
            ContractStatus.InProgress,
            context.Contracts.Current.Contract.Status);
        Assert.Equal(
            StartedAt,
            context.Contracts.Current.Contract.StartedAt);
        Assert.Equal(
            context.Contracts.Current.Contract.ContractId,
            context.Sessions.Current?.ContractId);
        Assert.Equal(
            context.Contracts.Current.Contract.ContractId,
            context.Checkpoints.Checkpoint?.SessionId);
        Assert.Equal(
            1,
            context.Contracts.UpdateCount);
        Assert.Equal(
            1,
            context.Checkpoints.SaveCount);
    }

    [Fact]
    public async Task AcceptedProviderRecoversAfterLiveTitleWasUnavailableBeforeSelectionCheckpoint()
    {
        ProviderAircraftAssignment provider = ProviderAircraftAssignment.CreateForOffer(
            Guid.Parse("c1000000-0000-0000-0000-000000000001"),
            new ProviderAircraftType(FixtureAircraftId, "Fixture Aircraft"),
            "KRME");
        TestContext context = CreateContext(
            Persisted(ContractStatus.Accepted, provider),
            hasReservation: true,
            now: StartedAt,
            hasAirframeSelection: false);

        CareerJobPreFlightSessionRecoveryResult result = await context.Recovery.RecoverAsync();

        Assert.True(result.Recovered);
        Assert.Equal(FlightSessionAircraftKind.Provider, context.Sessions.Current?.AircraftIdentity?.Kind);
        Assert.Equal(provider.ProviderAircraftInstanceId.ToString("D"),
            context.Sessions.Current?.AircraftIdentity?.InstanceId);
        Assert.Equal(FixtureAircraftId, context.Sessions.Current?.AircraftIdentity?.AircraftId);
        Assert.Equal(1, context.Contracts.UpdateCount);
        Assert.Equal(1, context.Checkpoints.SaveCount);
    }

    [Fact]
    public async Task InProgressContractRecoversFlightSessionWithoutSecondLifecycleTransition()
    {
        TestContext context =
            CreateContext(
                Persisted(
                    ContractStatus.InProgress),
                hasReservation:
                    true,
                now:
                    StartedAt.AddMinutes(1));

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.True(
            result.Recovered);
        Assert.Equal(
            0,
            context.Contracts.UpdateCount);
        Assert.Equal(
            StartedAt,
            context.Sessions.Current?.CreatedAt);
        Assert.Equal(
            context.Contracts.Current.Contract.ContractId,
            context.Sessions.Current?.ContractId);
        Assert.Equal(
            0,
            context.Airports.Calls);
    }

    [Fact]
    public async Task MissingDeterministicReservationLeavesAcceptedContractUntouched()
    {
        TestContext context =
            CreateContext(
                Persisted(
                    ContractStatus.Accepted),
                hasReservation:
                    false,
                now:
                    StartedAt);

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.Equal(
            CareerJobPreFlightSessionRecoveryState.WaitingForReservation,
            result.State);
        Assert.Equal(
            ContractStatus.Accepted,
            context.Contracts.Current.Contract.Status);
        Assert.Null(
            context.Sessions.Current);
        Assert.Null(
            context.Checkpoints.Checkpoint);
        Assert.Equal(
            0,
            context.Contracts.UpdateCount);
    }

    [Fact]
    public async Task RepeatedRecoveryDoesNotDuplicateFlightSession()
    {
        TestContext context =
            CreateContext(
                Persisted(
                    ContractStatus.Accepted),
                hasReservation:
                    true,
                now:
                    StartedAt);

        CareerJobPreFlightSessionRecoveryResult first =
            await context.Recovery.RecoverAsync();

        CareerJobPreFlightSessionRecoveryResult second =
            await context.Recovery.RecoverAsync();

        Assert.True(
            first.Recovered);
        Assert.Equal(
            CareerJobPreFlightSessionRecoveryState.ActiveFlightSessionAlreadyPresent,
            second.State);
        Assert.Equal(
            1,
            context.Contracts.UpdateCount);
        Assert.Equal(
            1,
            context.Checkpoints.SaveCount);
    }

    private static TestContext CreateContext(
        PersistedJobContract persisted,
        bool hasReservation,
        DateTimeOffset now,
        bool hasAirframeSelection = true)
    {
        var contracts =
            new FakeContractStore(
                persisted);

        var runtime =
            new JobContractRuntimeState(
                new JobContractRecoveryService(
                    new FakeRecoverySource(
                        persisted),
                    contracts));

        var profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "c1000000-0000-0000-0000-000000000099"),
                persisted.Contract.OriginIcao,
                OfferedAt.AddDays(-30));

        var career =
            new PlayerCareerRuntimeState(
                new FakeProfileStore(
                    new PlayerCareerProfileStoreRecord(
                        Revision:
                            1,
                        profile,
                        SavedAt:
                            OfferedAt.AddDays(-1))));

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                persisted.Contract.ContractId);

        var reservations =
            new FakeReservationLookup(
                hasReservation
                    ? new AircraftReservationOwnership(
                        FixtureAircraftId,
                        reservationId)
                    : null);

        var aircraftRegistry =
            new AircraftRegistryCatalogService(
                [new FakeAircraftObservationSource()]);

        var airports =
            new FakeAirportObservationSource(
                Airport(
                    "KRME",
                    43.2338,
                    -75.4069),
                Airport(
                    "KSYR",
                    43.1112,
                    -76.1063));

        var dispatch =
            new OperationDispatchPlanningService(
                aircraftRegistry,
                new CachedAirportDataSource(
                    [airports]));

        var sessions =
            new FlightSessionCoordinator();

        var checkpoints =
            new MemoryCheckpointStore();

        var flightStart =
            new AcceptedJobFlightSessionBridge(
                new AcceptedJobStartBridge(
                    new JobContractLifecycleService(
                        contracts,
                        runtime)),
                contracts,
                new FlightSessionPersistenceService(
                    sessions,
                    checkpoints),
                sessions,
                new FixedLiveAircraftIdentitySource(),
                new FixedAirframeSelectionStore(FixtureAircraftId,
                    hasAirframeSelection));

        var service =
            new CareerJobPreFlightSessionRecoveryService(
                runtime,
                career,
                reservations,
                aircraftRegistry,
                dispatch,
                flightStart,
                sessions,
                new FixedTimeProvider(
                    now));

        return new(
            service,
            contracts,
            sessions,
            checkpoints,
            airports);
    }

    private static PersistedJobContract Persisted(
        ContractStatus status,
        ProviderAircraftAssignment? provider = null)
    {
        var contract =
            new JobContract(
                ContractId:
                    Guid.Parse(
                        "c1000000-0000-0000-0000-000000000001"),
                EmployerId:
                    null,
                Kind:
                    ContractKind.Ferry,
                ServiceTrack:
                    ServiceTrack.CivilianEmployment,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.PilotWage,
                        GrossCustomerRevenue:
                            1_000m,
                        PilotCompensation:
                            400m,
                        EmployerCoversFuel:
                            true,
                        EmployerCoversMaintenance:
                            true,
                        EmployerCoversAirportFees:
                            true),
                OfferedAt:
                    OfferedAt,
                MustStartBy:
                    OfferedAt.AddHours(2),
                MustCompleteBy:
                    OfferedAt.AddHours(6),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumRangeNauticalMiles:
                            120,
                        MinimumSeats:
                            0),
                Status:
                    status,
                ProviderAircraft:
                    provider,
                AcceptedAt:
                    AcceptedAt,
                StartedAt:
                    status == ContractStatus.InProgress
                        ? StartedAt
                        : null);

        contract.Validate();

        return new(
            contract,
            Version:
                status == ContractStatus.InProgress
                    ? 3
                    : 2);
    }

    private static AirportRecord Airport(
        string icao,
        double latitude,
        double longitude) =>
        new(
            icao,
            $"{icao} Fixture",
            [
                new RunwayRecord(
                    "01/19",
                    UsableLengthFeet:
                        6_000,
                    WidthFeet:
                        100,
                    RunwaySurface.Asphalt)
            ],
            latitude,
            longitude);

    private sealed record TestContext(
        CareerJobPreFlightSessionRecoveryService Recovery,
        FakeContractStore Contracts,
        FlightSessionCoordinator Sessions,
        MemoryCheckpointStore Checkpoints,
        FakeAirportObservationSource Airports);

    private sealed class FakeContractStore(
        PersistedJobContract current)
        : IJobContractStore
    {
        public PersistedJobContract Current { get; private set; } =
            current;

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

            if (Current.Version
                != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Synthetic stale contract version.");
            }

            UpdateCount++;

            Current =
                new PersistedJobContract(
                    contract,
                    checked(
                        expectedVersion + 1));

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }

    private sealed class FakeRecoverySource(
        PersistedJobContract persisted)
        : IJobContractRecoverySource
    {
        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<JobContractRecoveryCandidate>>(
                [
                    new JobContractRecoveryCandidate(
                        persisted.Contract.ContractId,
                        persisted.Contract.Status,
                        persisted.Version,
                        persisted.Contract.StartedAt
                            ?? persisted.Contract.AcceptedAt
                            ?? persisted.Contract.OfferedAt)
                ]);
        }
    }

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord record)
        : IPlayerCareerProfileStore
    {
        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PlayerCareerProfileStoreRecord?>(
                record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeReservationLookup(
        AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup
    {
        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                ownership is not null
                && string.Equals(
                    ownership.ReservationId,
                    reservationId,
                    StringComparison.Ordinal)
                    ? ownership
                    : null);
        }
    }

    private sealed class FakeAircraftObservationSource
        : IAircraftRegistryObservationSource
    {
        public Task<IReadOnlyList<AircraftRegistryObservation>>
            FindAircraftObservationsAsync(
                string canonicalAircraftId,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(
                    canonicalAircraftId,
                    FixtureAircraftId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>(
                    Array.Empty<AircraftRegistryObservation>());
            }

            return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>(
                [
                    new AircraftRegistryObservation(
                        FixtureAircraftId,
                        ProviderId:
                            "fixture",
                        ProviderRecordId:
                            FixtureAircraftId,
                        AircraftDataConfidence.Verified,
                        IsInstalled:
                            true,
                        DisplayName:
                            "Fixture Aircraft",
                        Capabilities:
                            AircraftCapability.None,
                        Access:
                            AircraftAccess.Civilian,
                        MaximumPayloadPounds:
                            1_000,
                        MaximumRangeNauticalMiles:
                            500,
                        TypicalCruiseKnots:
                            120,
                        Seats:
                            4,
                        EngineCount:
                            1,
                        IfrCapable:
                            true,
                        Pressurized:
                            false,
                        RetractableGear:
                            false,
                        RunwayPerformance:
                            new AircraftRunwayPerformanceProfile(
                                MinimumTakeoffRunwayFeet:
                                    1_000,
                                MinimumLandingRunwayFeet:
                                    1_000,
                                MinimumRunwayWidthFeet:
                                    30,
                                SupportedSurfaces:
                                    RunwaySurfaceSupport.Asphalt,
                                AircraftDataConfidence.Verified,
                                Source:
                                    "fixture"))
                ]);
        }
    }

    private sealed class FakeAirportObservationSource(
        params AirportRecord[] airports)
        : IAirportDataObservationSource
    {
        private readonly IReadOnlyDictionary<string, AirportRecord> _airports =
            airports.ToDictionary(
                static airport =>
                    airport.Icao,
                StringComparer.OrdinalIgnoreCase);

        public string SourceId =>
            "fixture-airports";

        public AirportDataAuthority Authority =>
            AirportDataAuthority.Reference;

        public int Calls { get; private set; }

        public Task<AirportDataObservation?> FindAirportObservationAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;

            _airports.TryGetValue(
                icao,
                out AirportRecord? airport);

            return Task.FromResult(
                airport is null
                    ? null
                    : new AirportDataObservation(
                        airport,
                        new AirportDataProvenance(
                            SourceId,
                            Authority,
                            OfferedAt)));
        }
    }

    private sealed class MemoryCheckpointStore
        : IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; private set; }

        public int SaveCount { get; private set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
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
            Checkpoint =
                null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedAirframeSelectionStore(string aircraftId, bool hasSelection)
        : IContractAirframeSelectionStore
    {
        private FlightSessionAircraftIdentity? _selected =
            hasSelection
                ? new(FlightSessionAircraftKind.Owned, "fixture-ownership", aircraftId)
                : null;

        public Task SaveAsync(Guid contractId, FlightSessionAircraftIdentity identity,
            CancellationToken cancellationToken = default)
        {
            if (_selected is not null && _selected != identity)
                throw new InvalidOperationException("Recovery cannot switch aircraft.");
            _selected = identity;
            return Task.CompletedTask;
        }

        public Task<FlightSessionAircraftIdentity?> ReadAsync(Guid contractId,
            CancellationToken cancellationToken = default) => Task.FromResult(_selected);
    }

    private sealed class FixedLiveAircraftIdentitySource
        : ILiveAircraftIdentitySource
    {
        public string? CurrentAircraftTitle =>
            "Fixture Aircraft";
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }
}
