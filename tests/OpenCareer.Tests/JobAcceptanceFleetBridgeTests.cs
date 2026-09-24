using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class JobAcceptanceFleetBridgeTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptanceCreatesOneAuthoritativeReservationForDispatch()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var reservationStore = new StatefulReservationStore();
        JobAcceptanceFleetBridge bridge = CreateBridge(
            contractStore,
            new FakeBoardStore(BoardWithOffer(request.Offer)),
            reservationStore);

        JobAcceptanceFleetResult result =
            await bridge.AcceptAndReserveAsync(
                request,
                DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReserved,
            result.Status);
        Assert.Equal(
            ContractStatus.Accepted,
            result.AcceptedContract?.Contract.Status);
        Assert.Equal(
            JobAcceptanceFleetBridge.GetReservationId(request.Offer.OfferId),
            result.ReservationId);
        Assert.Equal(
            "canonical-aircraft",
            result.CanonicalAircraftId);
        Assert.Equal(
            result.ReservationId,
            reservationStore.State?.ReservationId);
        Assert.Equal(1, reservationStore.AcquiredCount);
    }

    [Fact]
    public async Task ReplayAfterBridgeRecreationReusesReservationWithoutDuplicateAcquire()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var boardStore = new FakeBoardStore(
            BoardWithOffer(request.Offer));
        var reservationStore = new StatefulReservationStore();

        JobAcceptanceFleetResult first =
            await CreateBridge(
                    contractStore,
                    boardStore,
                    reservationStore)
                .AcceptAndReserveAsync(
                    request,
                    DispatchContext(request.AcceptanceTime));

        JobAcceptanceFleetResult replay =
            await CreateBridge(
                    contractStore,
                    boardStore,
                    reservationStore)
                .AcceptAndReserveAsync(
                    request,
                    DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReserved,
            first.Status);
        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReservationReused,
            replay.Status);
        Assert.Equal(
            first.ReservationId,
            replay.ReservationId);
        Assert.Equal(1, reservationStore.AcquiredCount);
        Assert.Equal(2, reservationStore.TryReserveCount);
        Assert.Equal(
            replay.ReservationId,
            reservationStore.State?.ReservationId);
        Assert.Equal(1, contractStore.CreateCount);
        Assert.Equal(1, contractStore.UpdateCount);
    }

    [Fact]
    public async Task ExplicitlyUnavailableAircraftRejectsBeforeContractCreation()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var reservationStore = new StatefulReservationStore(
            new AircraftAvailabilityState(
                "canonical-aircraft",
                AircraftAvailabilityStatus.Unavailable));

        JobAcceptanceFleetResult result =
            await CreateBridge(
                    contractStore,
                    new FakeBoardStore(BoardWithOffer(request.Offer)),
                    reservationStore)
                .AcceptAndReserveAsync(
                    request,
                    DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            JobAcceptanceFleetStatus.AircraftUnavailable,
            result.Status);
        Assert.Null(result.AcceptedContract);
        Assert.Null(
            await contractStore.ReadJobContractAsync(
                request.Offer.OfferId));
        Assert.Equal(0, contractStore.CreateCount);
        Assert.Null(reservationStore.State?.ReservationId);
    }

    [Fact]
    public async Task UnrelatedReservationIsPreservedAndAcceptanceDoesNotStart()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var reservationStore = new StatefulReservationStore(
            new AircraftAvailabilityState(
                "canonical-aircraft",
                AircraftAvailabilityStatus.Unavailable,
                "dispatch:unrelated"));

        JobAcceptanceFleetResult result =
            await CreateBridge(
                    contractStore,
                    new FakeBoardStore(BoardWithOffer(request.Offer)),
                    reservationStore)
                .AcceptAndReserveAsync(
                    request,
                    DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            JobAcceptanceFleetStatus.AircraftReservedByAnother,
            result.Status);
        Assert.Null(result.AcceptedContract);
        Assert.Equal(
            "dispatch:unrelated",
            reservationStore.State?.ReservationId);
        Assert.Equal(0, reservationStore.ReleaseCount);
        Assert.Equal(0, contractStore.CreateCount);
    }

    [Fact]
    public async Task FailedAcceptanceReleasesOnlyReservationAcquiredByThisAttempt()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var reservationStore = new StatefulReservationStore();
        var boardStore = new FakeBoardStore(null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateBridge(
                    contractStore,
                    boardStore,
                    reservationStore)
                .AcceptAndReserveAsync(
                    request,
                    DispatchContext(request.AcceptanceTime)));

        Assert.Equal(1, reservationStore.AcquiredCount);
        Assert.Equal(1, reservationStore.ReleaseCount);
        Assert.True(reservationStore.State?.IsAvailable);
        Assert.Null(reservationStore.State?.ReservationId);
        Assert.Null(
            await contractStore.ReadJobContractAsync(
                request.Offer.OfferId));
    }

    [Fact]
    public async Task AcceptedReservationIsUsedForAuthoritativeDispatchEvaluation()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(request.Offer));
        var reservationStore =
            new StatefulReservationStore();

        AcceptedJobDispatchResult result =
            await CreateDispatchBridge(
                    contractStore,
                    boardStore,
                    reservationStore,
                    StandardAirports())
                .AcceptReserveAndEvaluateAsync(
                    request,
                    DispatchContext(request.AcceptanceTime),
                    new OperationDispatchRequirements(
                        PayloadPounds: 500,
                        RequiredRangeNauticalMiles: 150));

        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReserved,
            result.FleetResult.Status);
        Assert.NotNull(result.DispatchResult);
        Assert.Equal(
            DispatchFeasibilityStatus.Feasible,
            result.DispatchResult.Status);
        Assert.Equal(
            result.FleetResult.ReservationId,
            reservationStore.State?.ReservationId);
    }

    [Fact]
    public async Task DispatchReplayReusesContractReservationAndRemainsFeasible()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(request.Offer));
        var reservationStore =
            new StatefulReservationStore();

        AcceptedJobDispatchResult first =
            await CreateDispatchBridge(
                    contractStore,
                    boardStore,
                    reservationStore,
                    StandardAirports())
                .AcceptReserveAndEvaluateAsync(
                    request,
                    DispatchContext(request.AcceptanceTime),
                    new OperationDispatchRequirements(500, 150));

        AcceptedJobDispatchResult replay =
            await CreateDispatchBridge(
                    contractStore,
                    boardStore,
                    reservationStore,
                    StandardAirports())
                .AcceptReserveAndEvaluateAsync(
                    request,
                    DispatchContext(request.AcceptanceTime),
                    new OperationDispatchRequirements(500, 150));

        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReserved,
            first.FleetResult.Status);
        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReservationReused,
            replay.FleetResult.Status);
        Assert.Equal(
            first.FleetResult.ReservationId,
            replay.FleetResult.ReservationId);
        Assert.Equal(
            DispatchFeasibilityStatus.Feasible,
            replay.DispatchResult?.Status);
        Assert.Equal(1, reservationStore.AcquiredCount);
    }

    [Fact]
    public async Task UnrelatedReservationStopsBeforeDispatchEvaluation()
    {
        JobContractCreationRequest request = Request();
        var reservationStore =
            new StatefulReservationStore(
                new AircraftAvailabilityState(
                    "canonical-aircraft",
                    AircraftAvailabilityStatus.Unavailable,
                    "dispatch:unrelated"));

        AcceptedJobDispatchResult result =
            await CreateDispatchBridge(
                    new FakeContractStore(),
                    new FakeBoardStore(
                        BoardWithOffer(request.Offer)),
                    reservationStore,
                    StandardAirports())
                .AcceptReserveAndEvaluateAsync(
                    request,
                    DispatchContext(request.AcceptanceTime),
                    new OperationDispatchRequirements(500, 150));

        Assert.Equal(
            JobAcceptanceFleetStatus.AircraftReservedByAnother,
            result.FleetResult.Status);
        Assert.Null(result.DispatchResult);
        Assert.Equal(
            "dispatch:unrelated",
            reservationStore.State?.ReservationId);
    }

    [Fact]
    public async Task AuthoritativeDispatchFailureIsRetainedForAcceptedOperation()
    {
        JobContractCreationRequest request = Request();
        var contractStore = new FakeContractStore();
        var reservationStore =
            new StatefulReservationStore();

        AirportRecord[] airports =
        [
            Airport("KRME", 1200),
            Airport("KSYR", 5000)
        ];

        AcceptedJobDispatchResult result =
            await CreateDispatchBridge(
                    contractStore,
                    new FakeBoardStore(
                        BoardWithOffer(request.Offer)),
                    reservationStore,
                    airports)
                .AcceptReserveAndEvaluateAsync(
                    request,
                    DispatchContext(request.AcceptanceTime),
                    new OperationDispatchRequirements(500, 150));

        Assert.Equal(
            ContractStatus.Accepted,
            result.FleetResult.AcceptedContract?.Contract.Status);
        Assert.NotNull(result.DispatchResult);
        Assert.Equal(
            DispatchFeasibilityStatus.Infeasible,
            result.DispatchResult.Status);
        Assert.Contains(
            result.DispatchResult.Issues,
            issue =>
                issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason
                    == DispatchFeasibilityReason.RunwayTooShort);
        Assert.Equal(
            result.FleetResult.ReservationId,
            reservationStore.State?.ReservationId);
    }

    [Fact]
    public async Task DispatchCannotUnderstateAcceptedJobPayloadOrRoute()
    {
        JobContractCreationRequest request = Request();
        AcceptedJobDispatchBridge bridge =
            CreateDispatchBridge(
                new FakeContractStore(),
                new FakeBoardStore(
                    BoardWithOffer(request.Offer)),
                new StatefulReservationStore(),
                StandardAirports());

        await Assert.ThrowsAsync<ArgumentException>(
            () => bridge.AcceptReserveAndEvaluateAsync(
                request,
                DispatchContext(request.AcceptanceTime),
                new OperationDispatchRequirements(499, 150)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => bridge.AcceptReserveAndEvaluateAsync(
                request,
                DispatchContext(request.AcceptanceTime),
                new OperationDispatchRequirements(500, 149)));
    }

    [Fact]
    public async Task ContractProviderUsesSharedReservationAndDispatchForKnownAircraft()
    {
        JobContractCreationRequest baseRequest =
            Request();

        ProviderAircraftAssignment provider =
            ProviderAircraftAssignment.CreateForOffer(
                baseRequest.Offer.OfferId,
                new ProviderAircraftType(
                    "canonical-aircraft",
                    "Contract Provider Aircraft"),
                baseRequest.Offer.OriginIcao);

        JobContractCreationRequest request =
            baseRequest with
            {
                Offer =
                    baseRequest.Offer with
                    {
                        Kind =
                            ContractKind.Ferry
                    },
                AircraftRequirements =
                    new AircraftMissionRequirements(
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumRangeNauticalMiles:
                            baseRequest.Offer.DistanceNm),
                PayloadPounds =
                    0,
                ProviderAircraft =
                    provider
            };

        var contractStore =
            new FakeContractStore();
        var reservationStore =
            new StatefulReservationStore();

        AcceptedJobDispatchResult result =
            await CreateDispatchBridge(
                    contractStore,
                    new FakeBoardStore(
                        BoardWithOffer(request.Offer)),
                    reservationStore,
                    StandardAirports(),
                    isInstalled:
                        false)
                .AcceptReserveAndEvaluateAsync(
                    request,
                    DispatchContext(request.AcceptanceTime),
                    new OperationDispatchRequirements(0, 150),
                    provider.ProviderAircraftInstanceId);

        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReserved,
            result.FleetResult.Status);
        Assert.Equal(
            DispatchFeasibilityStatus.Feasible,
            result.DispatchResult?.Status);
        Assert.Equal(
            provider,
            result.FleetResult.AcceptedContract?.Contract.ProviderAircraft);
        Assert.Equal(
            "canonical-aircraft",
            result.FleetResult.CanonicalAircraftId);
        Assert.Equal(
            result.FleetResult.ReservationId,
            reservationStore.State?.ReservationId);
        Assert.Equal(
            provider.AircraftId,
            reservationStore.State?.CanonicalAircraftId);
        Assert.Equal(
            baseRequest.Offer.OriginIcao,
            provider.OriginIcao);
        Assert.NotEqual(
            baseRequest.Offer.OfferId,
            provider.ProviderAircraftInstanceId);

        // The offer itself has no airframe; a new bridge reuses the contract's
        // exact assignment and the existing reservation after recovery.
        Assert.Null(request.Offer.ContractTerms?.ProviderAircraft);
        AcceptedJobDispatchResult replay =
            await CreateDispatchBridge(
                    contractStore,
                    new FakeBoardStore(BoardWithOffer(request.Offer)),
                    reservationStore,
                    StandardAirports(),
                    isInstalled: false)
                .AcceptReserveAndEvaluateAsync(
                    request,
                    DispatchContext(request.AcceptanceTime),
                    new OperationDispatchRequirements(0, 150),
                    provider.ProviderAircraftInstanceId);

        Assert.Equal(provider,
            replay.FleetResult.AcceptedContract?.Contract.ProviderAircraft);
        Assert.Equal(JobAcceptanceFleetStatus.AcceptedAndReservationReused,
            replay.FleetResult.Status);
    }

    [Fact]
    public async Task KnownAircraftWithoutProviderInstanceCanBeReservedWithoutInstalledCatalog()
    {
        JobContractCreationRequest request =
            Request();

        var contractStore =
            new FakeContractStore();
        var reservationStore =
            new StatefulReservationStore();

        JobAcceptanceFleetResult result =
            await CreateBridge(
                    contractStore,
                    new FakeBoardStore(
                        BoardWithOffer(request.Offer)),
                    reservationStore,
                    isInstalled:
                        false)
                .AcceptAndReserveAsync(
                    request,
                    DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            JobAcceptanceFleetStatus.AcceptedAndReserved,
            result.Status);
        Assert.NotNull(
            result.AcceptedContract);
        Assert.Equal(
            1,
            reservationStore.TryReserveCount);
        Assert.NotNull(
            await contractStore.ReadJobContractAsync(
                request.Offer.OfferId));
    }

    private static JobAcceptanceFleetBridge CreateBridge(
        FakeContractStore contractStore,
        FakeBoardStore boardStore,
        StatefulReservationStore reservationStore,
        bool isInstalled = true)
    {
        JobContractRuntimeState runtimeState =
            new(
                new JobContractRecoveryService(
                    new EmptyRecoverySource(),
                    contractStore));

        var acceptance =
            new JobOfferAcceptanceService(
                contractStore,
                new JobContractLifecycleService(
                    contractStore,
                    runtimeState),
                boardStore,
                runtimeState);

        var reservations =
            new AircraftReservationCoordinator(
                new StubAircraftRegistrySource(
                    AircraftRegistryResolver.Resolve(
                    [
                        new AircraftRegistryObservation(
                            CanonicalAircraftId:
                                "canonical-aircraft",
                            ProviderId:
                                "test-source",
                            ProviderRecordId:
                                "fixture",
                            Confidence:
                                AircraftDataConfidence.Verified,
                            IsInstalled:
                                isInstalled)
                    ])),
                reservationStore);

        return new(
            acceptance,
            contractStore,
            reservations);
    }

    private static AcceptedJobDispatchBridge CreateDispatchBridge(
        FakeContractStore contractStore,
        FakeBoardStore boardStore,
        StatefulReservationStore reservationStore,
        IReadOnlyList<AirportRecord> airports,
        bool isInstalled = true) =>
        new(
            CreateBridge(
                contractStore,
                boardStore,
                reservationStore,
                isInstalled),
            new OperationDispatchPlanningService(
                new StubAircraftRegistrySource(
                    DispatchResolution(
                        isInstalled)),
                new StubAirportDataSource(
                    airports.ToDictionary(
                        airport => airport.Icao,
                        StringComparer.OrdinalIgnoreCase)),
                weatherSource: null,
                reservationStore));

    private static AircraftRegistryResolution DispatchResolution(
        bool isInstalled = true) =>
        AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId:
                    "canonical-aircraft",
                ProviderId:
                    "test-source",
                ProviderRecordId:
                    "fixture",
                Confidence:
                    AircraftDataConfidence.Verified,
                IsInstalled:
                    isInstalled,
                MaximumPayloadPounds:
                    2_000,
                MaximumRangeNauticalMiles:
                    1_000,
                RunwayPerformance:
                    new AircraftRunwayPerformanceProfile(
                        MinimumTakeoffRunwayFeet:
                            1_800,
                        MinimumLandingRunwayFeet:
                            1_600,
                        MinimumRunwayWidthFeet:
                            50,
                        SupportedSurfaces:
                            RunwaySurfaceSupport.Asphalt,
                        Confidence:
                            AircraftDataConfidence.Verified,
                        Source:
                            "test"))
        ]);

    private static AirportRecord[] StandardAirports() =>
    [
        Airport("KRME", 5_000),
        Airport("KSYR", 5_000)
    ];

    private static AirportRecord Airport(
        string icao,
        double runwayLengthFeet) =>
        new(
            icao,
            $"{icao} Fixture",
            [
                new RunwayRecord(
                    "18",
                    runwayLengthFeet,
                    100,
                    RunwaySurface.Asphalt)
            ]);

    private static JobBoardState BoardWithOffer(
        JobMarketOfferDraft offer) =>
        JobBoardState
            .Empty(
                offer.OriginIcao,
                offer.OfferedAt)
            .Reconcile(
                offer.OfferedAt,
                1,
                [offer]);

    private static JobContractCreationRequest Request() =>
        new(
            Offer:
                new JobMarketOfferDraft(
                    Guid.Parse(
                        "94000000-0000-0000-0000-000000000001"),
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Cargo,
                    JobScenarioKind.Standard,
                    "KRME",
                    "KSYR",
                    150,
                    1.5,
                    OfferedAt,
                    OfferedAt.AddHours(1),
                    false,
                    0.4,
                    0.3,
                    1.0),
            AcceptanceTime:
                OfferedAt.AddMinutes(10),
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
            EstimatedFlightHours:
                1.5,
            PayloadPounds:
                500,
            DemandAttractiveness:
                1.2,
            Urgency:
                0.1,
            Difficulty:
                0.1,
            MustStartBy:
                OfferedAt.AddHours(2),
            MustCompleteBy:
                OfferedAt.AddHours(4));

    private static ContractDispatchContext DispatchContext(
        DateTimeOffset time) =>
        new(
            time,
            new AircraftCapabilityProfile(
                "canonical-aircraft",
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
                true);

    private sealed class StubAircraftRegistrySource(
        AircraftRegistryResolution resolution)
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AircraftRegistryResolution?>(
                resolution);
        }
    }

    private sealed class StubAirportDataSource(
        IReadOnlyDictionary<string, AirportRecord> airports)
        : IAirportDataSource
    {
        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            airports.TryGetValue(
                icao,
                out AirportRecord? airport);
            return Task.FromResult(airport);
        }
    }

    private sealed class StatefulReservationStore(
        AircraftAvailabilityState? initial = null)
        : IAircraftReservationStore, IAircraftAvailabilityStore
    {
        public AircraftAvailabilityState? State { get; private set; } =
            initial;
        public int TryReserveCount { get; private set; }
        public int AcquiredCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<AircraftAvailabilityState?> FindAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AircraftAvailabilityState? result =
                State is not null
                && string.Equals(
                    State.CanonicalAircraftId,
                    canonicalAircraftId,
                    StringComparison.OrdinalIgnoreCase)
                    ? State
                    : null;

            return Task.FromResult(result);
        }

        public Task SetAsync(
            AircraftAvailabilityState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(state);
            State = state.Normalize();
            return Task.CompletedTask;
        }

        public Task<AircraftReservationAcquireResult> TryReserveAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryReserveCount++;

            if (State is null || State.IsAvailable)
            {
                State =
                    new AircraftAvailabilityState(
                        canonicalAircraftId,
                        AircraftAvailabilityStatus.Unavailable,
                        reservationId);
                AcquiredCount++;
                return Task.FromResult(
                    AircraftReservationAcquireResult.Acquired);
            }

            if (string.Equals(
                    State.ReservationId,
                    reservationId,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    AircraftReservationAcquireResult.AlreadyHeld);
            }

            return Task.FromResult(
                State.ReservationId is null
                    ? AircraftReservationAcquireResult.Unavailable
                    : AircraftReservationAcquireResult.HeldByAnotherReservation);
        }

        public Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;

            if (State is null || State.IsAvailable)
            {
                return Task.FromResult(
                    AircraftReservationReleaseResult.AlreadyReleased);
            }

            if (State.ReservationId is null)
            {
                return Task.FromResult(
                    AircraftReservationReleaseResult.NotReserved);
            }

            if (!string.Equals(
                    State.ReservationId,
                    reservationId,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    AircraftReservationReleaseResult.HeldByAnotherReservation);
            }

            State =
                new AircraftAvailabilityState(
                    canonicalAircraftId,
                    AircraftAvailabilityStatus.Available);

            return Task.FromResult(
                AircraftReservationReleaseResult.Released);
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

    private sealed class FakeContractStore
        : IJobContractStore
    {
        private PersistedJobContract? _current;

        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _current?.Contract.ContractId
                    == contractId
                        ? _current
                        : null);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contract.Validate();
            CreateCount++;

            if (_current is null)
            {
                _current =
                    new PersistedJobContract(
                        contract,
                        0);

                return Task.FromResult(
                    JobContractSaveResult.Created);
            }

            if (_current.Contract == contract)
            {
                return Task.FromResult(
                    JobContractSaveResult.AlreadySaved);
            }

            throw new InvalidOperationException(
                "Different contract already exists.");
        }

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contract.Validate();
            UpdateCount++;

            if (_current is null)
            {
                throw new InvalidOperationException(
                    "Job contract does not exist.");
            }

            if (_current.Contract == contract)
            {
                return Task.FromResult(
                    JobContractSaveResult.AlreadySaved);
            }

            if (_current.Version != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Version conflict.");
            }

            _current =
                new PersistedJobContract(
                    contract,
                    checked(expectedVersion + 1));

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }

    private sealed class FakeBoardStore(
        JobBoardState? initial)
        : IJobBoardStateStore
    {
        public JobBoardState? State { get; private set; } =
            initial;

        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Validate();
            State = state;
            return Task.CompletedTask;
        }

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JobBoardState? result =
                State is not null
                && string.Equals(
                    State.AirportIcao,
                    airportIcao.Trim().ToUpperInvariant(),
                    StringComparison.Ordinal)
                    ? State
                    : null;

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<JobBoardState> result =
                State is null
                    ? Array.Empty<JobBoardState>()
                    : [State];

            return Task.FromResult(result);
        }
    }
}
