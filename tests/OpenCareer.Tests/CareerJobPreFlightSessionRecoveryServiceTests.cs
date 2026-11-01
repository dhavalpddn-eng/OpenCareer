using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class CareerJobPreFlightSessionRecoveryServiceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AcceptedAt =
        OfferedAt.AddMinutes(10);

    private static readonly DateTimeOffset StartedAt =
        OfferedAt.AddMinutes(20);

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
    public async Task SessionAppearingDuringReservationReadStopsRecoveryWithoutMutation()
    {
        TestContext context =
            CreateContext(
                Persisted(
                    ContractStatus.Accepted),
                hasReservation:
                    true,
                now:
                    StartedAt);
        var readStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var release =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        context.Reservations.BeforeFindAsync =
            async cancellationToken =>
            {
                readStarted.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            };

        Task<CareerJobPreFlightSessionRecoveryResult> recovery =
            context.Recovery.RecoverAsync();
        await readStarted.Task;

        FlightSession competingSession =
            context.Sessions.Start(
                StartedAt.AddMinutes(1));
        release.TrySetResult();

        CareerJobPreFlightSessionRecoveryResult result =
            await recovery;

        Assert.Equal(
            CareerJobPreFlightSessionRecoveryState.ActiveFlightSessionAlreadyPresent,
            result.State);
        Assert.Equal(
            competingSession.SessionId,
            context.Sessions.Current?.SessionId);
        Assert.Equal(
            ContractStatus.Accepted,
            context.Contracts.Current.Contract.Status);
        Assert.Equal(0, context.Contracts.UpdateCount);
        Assert.Equal(0, context.Checkpoints.SaveCount);
        Assert.Null(context.Checkpoints.Checkpoint);
    }

    [Fact]
    public async Task MissingPreparedPlanLeavesAcceptedContractUntouched()
    {
        TestContext context =
            CreateContext(
                Persisted(
                    ContractStatus.Accepted),
                hasReservation:
                    true,
                now:
                    StartedAt,
                preparedResult:
                    new(
                        PreparedDispatchStartAuthorityStatus.PlanUnavailable,
                        "No prepared dispatch plan exists for the accepted contract."));

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.Equal(
            CareerJobPreFlightSessionRecoveryState.WaitingForPreparedDispatch,
            result.State);
        Assert.Equal(
            ContractStatus.Accepted,
            context.Contracts.Current.Contract.Status);
        Assert.Null(
            context.Sessions.Current);
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

    [Fact]
    public async Task MultipleAcceptedContractsRemainAvailableForJobsCancellation()
    {
        TestContext context = CreateContext(
            Persisted(ContractStatus.Accepted),
            hasReservation: true,
            now: StartedAt);
        await context.Runtime.InitializeAsync();
        await context.Runtime.PublishAuthoritativeAsync(
            Persisted(
                ContractStatus.Accepted,
                contractId:
                    Guid.Parse(
                        "c1000000-0000-0000-0000-000000000002")));

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.Equal(
            CareerJobPreFlightSessionRecoveryState.MultipleAcceptedContracts,
            result.State);
        Assert.Null(result.ContractId);
        Assert.Contains(
            "use Jobs to cancel",
            result.Detail,
            StringComparison.Ordinal);
        Assert.Collection(
            context.Runtime.Current,
            static persisted =>
                Assert.Equal(
                    ContractStatus.Accepted,
                    persisted.Contract.Status),
            static persisted =>
                Assert.Equal(
                    ContractStatus.Accepted,
                    persisted.Contract.Status));
        Assert.Null(context.Sessions.Current);
        Assert.Null(context.Checkpoints.Checkpoint);
        Assert.Equal(0, context.Contracts.UpdateCount);
        Assert.Equal(0, context.Reservations.FindCount);
    }

    [Theory]
    [InlineData(ContractStatus.Accepted)]
    [InlineData(ContractStatus.InProgress)]
    public async Task PassengerContractRetainsManifestAuthorityAcrossRecovery(
        ContractStatus status)
    {
        TestContext context = CreateContext(
            Persisted(status, ContractKind.Passenger),
            hasReservation: true,
            now: StartedAt.AddMinutes(1));

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.True(result.Recovered, result.Detail);
        Assert.Equal(
            ContractStatus.InProgress,
            context.Contracts.Current.Contract.Status);

        AcceptedPassengerManifest manifest =
            AcceptedPassengerManifest.Create(
                context.Contracts.Current.Contract);

        Assert.Equal(3, manifest.PassengerCount);
        Assert.Equal(540, manifest.PassengerAndBaggageWeightPounds);
    }

    [Fact]
    public async Task AcceptedPassengerContractWithoutManifestIsNotRecovered()
    {
        TestContext context = CreateContext(
            Persisted(
                ContractStatus.Accepted,
                ContractKind.Passenger,
                includePassengerManifest: false),
            hasReservation: true,
            now: StartedAt);

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.Equal(
            CareerJobPreFlightSessionRecoveryState.NothingToRecover,
            result.State);
        Assert.Equal(
            ContractStatus.Accepted,
            context.Contracts.Current.Contract.Status);
        Assert.Null(context.Sessions.Current);
        Assert.Equal(0, context.Contracts.UpdateCount);
    }

    [Fact]
    public async Task InProgressLegacyPassengerRecoversWithoutInventingManifest()
    {
        TestContext context = CreateContext(
            Persisted(
                ContractStatus.InProgress,
                ContractKind.Passenger,
                includePassengerManifest: false),
            hasReservation: true,
            now: StartedAt.AddMinutes(1));

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.True(result.Recovered, result.Detail);
        Assert.Equal(0, context.Contracts.UpdateCount);
        Assert.Equal(
            context.Contracts.Current.Contract.ContractId,
            context.Sessions.Current?.ContractId);
        Assert.False(
            AcceptedPassengerManifest.TryCreate(
                context.Contracts.Current.Contract,
                out _));
    }

    [Theory]
    [InlineData(ContractKind.Cargo, ContractStatus.Accepted)]
    [InlineData(ContractKind.Cargo, ContractStatus.InProgress)]
    [InlineData(ContractKind.ExpressCargo, ContractStatus.Accepted)]
    [InlineData(ContractKind.ExpressCargo, ContractStatus.InProgress)]
    [InlineData(ContractKind.AogPartsDelivery, ContractStatus.Accepted)]
    [InlineData(ContractKind.AogPartsDelivery, ContractStatus.InProgress)]
    public async Task NamedCargoContractRetainsManifestAuthorityAcrossRecovery(
        ContractKind kind,
        ContractStatus status)
    {
        TestContext context = CreateContext(
            Persisted(status, kind),
            hasReservation: true,
            now: StartedAt.AddMinutes(1));

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.True(result.Recovered, result.Detail);
        Assert.Equal(
            ContractStatus.InProgress,
            context.Contracts.Current.Contract.Status);

        AcceptedCargoManifest manifest =
            AcceptedCargoManifest.Create(
                context.Contracts.Current.Contract);

        AcceptedCargoLot lot = Assert.Single(manifest.Lots);
        Assert.Equal("coffee.roasted", lot.CommodityId);
        Assert.Equal(500, manifest.TotalMassPounds);
    }

    [Fact]
    public async Task AcceptedCargoContractWithoutNamedManifestIsNotRecovered()
    {
        TestContext context = CreateContext(
            Persisted(
                ContractStatus.Accepted,
                ContractKind.Cargo,
                includeCargoManifest: false),
            hasReservation: true,
            now: StartedAt);

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.Equal(
            CareerJobPreFlightSessionRecoveryState.NothingToRecover,
            result.State);
        Assert.Equal(
            ContractStatus.Accepted,
            context.Contracts.Current.Contract.Status);
        Assert.Null(context.Sessions.Current);
        Assert.Equal(0, context.Contracts.UpdateCount);
    }

    [Theory]
    [InlineData(ContractKind.Cargo)]
    [InlineData(ContractKind.ExpressCargo)]
    [InlineData(ContractKind.AogPartsDelivery)]
    public async Task InProgressLegacyCargoRecoversWithoutInventingManifest(
        ContractKind kind)
    {
        TestContext context = CreateContext(
            Persisted(
                ContractStatus.InProgress,
                kind,
                includeCargoManifest: false),
            hasReservation: true,
            now: StartedAt.AddMinutes(1));

        CareerJobPreFlightSessionRecoveryResult result =
            await context.Recovery.RecoverAsync();

        Assert.True(result.Recovered, result.Detail);
        Assert.Equal(0, context.Contracts.UpdateCount);
        Assert.Equal(
            context.Contracts.Current.Contract.ContractId,
            context.Sessions.Current?.ContractId);
        Assert.Throws<ArgumentException>(
            () => AcceptedCargoManifest.Create(
                context.Contracts.Current.Contract));
    }

    private static TestContext CreateContext(
        PersistedJobContract persisted,
        bool hasReservation,
        DateTimeOffset now,
        PreparedDispatchStartAuthorityResult? preparedResult = null)
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
                        "fixture-aircraft",
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
                sessions);

        var service =
            new CareerJobPreFlightSessionRecoveryService(
                runtime,
                career,
                reservations,
                aircraftRegistry,
                new StubPreparedDispatchStartAuthority(
                    preparedResult
                    ?? ReadyPreparedResult(persisted)),
                flightStart,
                sessions,
                new FixedTimeProvider(
                    now));

        return new(
            service,
            contracts,
            runtime,
            reservations,
            sessions,
            checkpoints,
            airports);
    }

    private static PreparedDispatchStartAuthorityResult ReadyPreparedResult(
        PersistedJobContract persisted)
    {
        string reservationId = JobAcceptanceFleetBridge.GetReservationId(
            persisted.Contract.ContractId);
        var routeFuel = new RouteFuelPlanningRequest(
            120,
            100,
            10,
            8,
            3,
            2,
            new(5, 30, 1));
        var fuel = new RouteFuelWeightPlan(0, 6, 30, 180, 50, 300);
        double payload =
            persisted.Contract.PlanningTerms?.PayloadPounds
            ?? 0;
        OperationDispatchRequirements requirements = fuel.ApplyTo(new(
            PayloadPounds: payload,
            RequiredRangeNauticalMiles: 120));
        var plan = new PreparedDispatchPlan(
            persisted.Contract.ContractId,
            persisted.Version,
            "fixture-aircraft",
            reservationId,
            persisted.Contract.OriginIcao,
            persisted.Contract.DestinationIcao,
            new(0, 0, 6000, 0, 12000),
            routeFuel,
            requirements,
            fuel,
            "01",
            "19",
            StartedAt);

        return new(
            PreparedDispatchStartAuthorityStatus.Ready,
            "Prepared dispatch remains current.",
            plan,
            DispatchFeasibilityResult.Create(
                DispatchFeasibilityStatus.Feasible,
                "01",
                "19",
                []));
    }

    private static PersistedJobContract Persisted(
        ContractStatus status,
        ContractKind kind = ContractKind.Ferry,
        bool includeCargoManifest = true,
        bool includePassengerManifest = true,
        Guid? contractId = null)
    {
        bool passenger = kind == ContractKind.Passenger;
        bool cargo = kind is
            ContractKind.Cargo
            or ContractKind.ExpressCargo
            or ContractKind.AogPartsDelivery;
        double payload =
            passenger
                ? 540
                : cargo ? 500 : 0;
        AircraftCapability requiredCapabilities =
            passenger
                ? AircraftCapability.Passenger
                : cargo
                    ? AircraftCapability.Cargo
                    : AircraftCapability.None;

        var contract =
            new JobContract(
                ContractId:
                    contractId
                    ?? Guid.Parse(
                        "c1000000-0000-0000-0000-000000000001"),
                EmployerId:
                    null,
                Kind:
                    kind,
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
                        RequiredCapabilities:
                            requiredCapabilities,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumPayloadPounds:
                            payload,
                        MinimumRangeNauticalMiles:
                            120,
                        MinimumSeats:
                            passenger ? 1 : 0,
                        MinimumPassengerCapacity:
                            passenger ? 3 : 0),
                Status:
                    status,
                AcceptedAt:
                    AcceptedAt,
                StartedAt:
                    status == ContractStatus.InProgress
                        ? StartedAt
                        : null,
                PlanningTerms:
                    new JobContractPlanningTerms(
                        RouteDistanceNauticalMiles: 120,
                        PayloadPounds:
                            passenger && !includePassengerManifest
                                ? 0
                                : payload,
                        CargoLots:
                            cargo && includeCargoManifest
                                ? [CargoLot()]
                                : default));

        contract.Validate();

        return new(
            contract,
            Version:
                status == ContractStatus.InProgress
                    ? 3
                    : 2);
    }

    private static AcceptedCargoLot CargoLot() =>
        new(
            Guid.Parse(
                "c1000000-0000-0000-0000-000000000010"),
            "coffee.roasted",
            "Roasted coffee",
            CargoCommodityCategory.GeneralFreight,
            Quantity:
                500m,
            CargoUnitOfTrade.Pound,
            MassPounds:
                500,
            VolumeCubicFeet:
                null,
            "KRME",
            "KSYR",
            "Mohawk Valley Roasters",
            "Syracuse Coffee Supply",
            DeclaredValue:
                3_700m,
            OriginMarketUnitValue:
                7.40m,
            DestinationMarketUnitValue:
                8.05m,
            CargoHandlingRequirement.None);

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
        JobContractRuntimeState Runtime,
        FakeReservationLookup Reservations,
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
        public Func<CancellationToken, Task>? BeforeFindAsync { get; set; }

        public int FindCount { get; private set; }

        public async Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCount++;

            if (BeforeFindAsync is not null)
                await BeforeFindAsync(cancellationToken);

            return ownership is not null
                && string.Equals(
                    ownership.ReservationId,
                    reservationId,
                    StringComparison.Ordinal)
                    ? ownership
                    : null;
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
                    "fixture-aircraft",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>(
                    Array.Empty<AircraftRegistryObservation>());
            }

            return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>(
                [
                    new AircraftRegistryObservation(
                        "fixture-aircraft",
                        ProviderId:
                            "fixture",
                        ProviderRecordId:
                            "fixture-aircraft",
                        AircraftDataConfidence.Verified,
                        IsInstalled:
                            true,
                        DisplayName:
                            "Fixture Aircraft",
                        Capabilities:
                            AircraftCapability.Passenger
                            | AircraftCapability.Cargo,
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
                        PassengerCapacity:
                            3,
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

    private sealed class StubPreparedDispatchStartAuthority(
        PreparedDispatchStartAuthorityResult result)
        : IPreparedDispatchStartAuthority
    {
        public Task<PreparedDispatchStartAuthorityResult> RevalidateAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
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

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }
}
