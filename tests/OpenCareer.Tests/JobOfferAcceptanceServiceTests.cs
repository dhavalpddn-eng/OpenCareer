using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class JobOfferAcceptanceServiceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActiveOfferCreatesAcceptsAndRetiresPersistedOffer()
    {
        JobContractCreationRequest request =
            Request();

        var contractStore =
            new FakeContractStore();

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer));

        JobContractRuntimeState runtimeState =
            CreateRuntimeState(
                contractStore);

        var service =
            CreateService(
                contractStore,
                boardStore,
                runtimeState);

        PersistedJobContract result =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime));

        Assert.Equal(
            ContractStatus.Accepted,
            result.Contract.Status);
        Assert.Equal(
            request.AcceptanceTime,
            result.Contract.AcceptedAt);
        Assert.Equal(
            1,
            result.Version);

        Assert.Equal(
            CompensationModel.PilotWage,
            result.Contract.Compensation.Model);

        Assert.Equal(
            1,
            contractStore.CreateCount);
        Assert.Equal(
            1,
            contractStore.UpdateCount);

        Assert.Equal(
            1,
            boardStore.SaveCount);

        Assert.True(runtimeState.IsInitialized);
        Assert.Same(
            result,
            runtimeState.Find(
                result.Contract.ContractId));

        Assert.DoesNotContain(
            boardStore.State!.Offers,
            offer =>
                offer.OfferId
                == request.Offer.OfferId);

        Assert.Contains(
            request.Offer.OfferId,
            boardStore.State.RetiredOfferIds);
    }

    [Fact]
    public async Task SuccessfulAcceptanceRetryIsIdempotent()
    {
        JobContractCreationRequest request =
            Request();

        var contractStore =
            new FakeContractStore();

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer));

        JobContractRuntimeState runtimeState =
            CreateRuntimeState(
                contractStore);

        var service =
            CreateService(
                contractStore,
                boardStore,
                runtimeState);

        PersistedJobContract first =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime));

        PersistedJobContract second =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime));

        Assert.Equal(
            first.Contract,
            second.Contract);
        Assert.Equal(
            first.Version,
            second.Version);

        Assert.Equal(
            1,
            contractStore.CreateCount);
        Assert.Equal(
            1,
            contractStore.UpdateCount);
        Assert.Equal(
            1,
            boardStore.SaveCount);
        Assert.Single(runtimeState.Current);
        Assert.Equal(
            second,
            runtimeState.Find(
                second.Contract.ContractId));
    }

    [Fact]
    public async Task RetirementFailureCanBeRecoveredByRetry()
    {
        JobContractCreationRequest request =
            Request();

        var contractStore =
            new FakeContractStore();

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer))
            {
                FailNextSave = true
            };

        var service =
            CreateService(
                contractStore,
                boardStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime)));

        PersistedJobContract persistedAfterFailure =
            (await contractStore
                .ReadJobContractAsync(
                    request.Offer.OfferId))!;

        Assert.Equal(
            ContractStatus.Accepted,
            persistedAfterFailure.Contract.Status);

        Assert.Contains(
            boardStore.State!.Offers,
            offer =>
                offer.OfferId
                == request.Offer.OfferId);

        PersistedJobContract recovered =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime));

        Assert.Equal(
            ContractStatus.Accepted,
            recovered.Contract.Status);

        Assert.DoesNotContain(
            boardStore.State!.Offers,
            offer =>
                offer.OfferId
                == request.Offer.OfferId);

        Assert.Contains(
            request.Offer.OfferId,
            boardStore.State.RetiredOfferIds);

        Assert.Equal(
            1,
            contractStore.CreateCount);
        Assert.Equal(
            1,
            contractStore.UpdateCount);
        Assert.Equal(
            2,
            boardStore.SaveAttempts);
        Assert.Equal(
            1,
            boardStore.SaveCount);
    }

    [Fact]
    public async Task PersistedOfferedContractResumesAcceptanceAndRetiresOffer()
    {
        JobContractCreationRequest request =
            Request();

        JobContract offered =
            JobContractFactory.Create(
                request);

        var contractStore =
            new FakeContractStore(
                new PersistedJobContract(
                    offered,
                    Version: 0));

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer));

        var service =
            CreateService(
                contractStore,
                boardStore);

        PersistedJobContract result =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime));

        Assert.Equal(
            ContractStatus.Accepted,
            result.Contract.Status);
        Assert.Equal(
            0,
            contractStore.CreateCount);
        Assert.Equal(
            1,
            contractStore.UpdateCount);

        Assert.Contains(
            request.Offer.OfferId,
            boardStore.State!.RetiredOfferIds);
    }

    [Fact]
    public async Task MissingAuthoritativeBoardRejectsBeforeContractCreation()
    {
        JobContractCreationRequest request =
            Request();

        var contractStore =
            new FakeContractStore();

        var service =
            CreateService(
                contractStore,
                new FakeBoardStore(null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime)));

        Assert.Equal(
            0,
            contractStore.CreateCount);
        Assert.Equal(
            0,
            contractStore.UpdateCount);
    }

    [Fact]
    public async Task LockedPreviewIsRejectedBeforePersistence()
    {
        JobContractCreationRequest request =
            Request() with
            {
                Offer =
                    Offer() with
                    {
                        IsLockedPreview = true
                    }
            };

        var contractStore =
            new FakeContractStore();

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer));

        var service =
            CreateService(
                contractStore,
                boardStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime)));

        Assert.Equal(
            0,
            contractStore.CreateCount);
        Assert.Equal(
            0,
            boardStore.SaveCount);
    }

    [Fact]
    public async Task ExpiredOfferIsRejectedBeforePersistence()
    {
        JobContractCreationRequest request =
            Request() with
            {
                AcceptanceTime =
                    Offer().ExpiresAt
            };

        var contractStore =
            new FakeContractStore();

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer));

        var service =
            CreateService(
                contractStore,
                boardStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime)));

        Assert.Equal(
            0,
            contractStore.CreateCount);
        Assert.Equal(
            0,
            boardStore.SaveCount);
    }

    [Fact]
    public async Task InvalidDispatchLeavesRecoverableOfferedContractAndActiveOffer()
    {
        JobContractCreationRequest request =
            Request();

        var contractStore =
            new FakeContractStore();

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer));

        var service =
            CreateService(
                contractStore,
                boardStore);

        ContractDispatchContext invalid =
            DispatchContext(
                request.AcceptanceTime) with
            {
                QualificationsVerified = false
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                invalid));

        PersistedJobContract? persisted =
            await contractStore.ReadJobContractAsync(
                request.Offer.OfferId);

        Assert.NotNull(persisted);
        Assert.Equal(
            ContractStatus.Offered,
            persisted.Contract.Status);
        Assert.Equal(
            0,
            persisted.Version);

        Assert.Contains(
            boardStore.State!.Offers,
            offer =>
                offer.OfferId
                == request.Offer.OfferId);
        Assert.DoesNotContain(
            request.Offer.OfferId,
            boardStore.State.RetiredOfferIds);

        Assert.Equal(
            1,
            contractStore.CreateCount);
        Assert.Equal(
            0,
            contractStore.UpdateCount);
        Assert.Equal(
            0,
            boardStore.SaveCount);
    }

    [Fact]
    public async Task ExistingDifferentContractUnderOfferIdentityIsRejected()
    {
        JobContractCreationRequest request =
            Request();

        JobContract conflicting =
            JobContractFactory.Create(
                request) with
            {
                DestinationIcao = "KBUF"
            };

        conflicting.Validate();

        var contractStore =
            new FakeContractStore(
                new PersistedJobContract(
                    conflicting,
                    Version: 0));

        var boardStore =
            new FakeBoardStore(
                BoardWithOffer(
                    request.Offer));

        var service =
            CreateService(
                contractStore,
                boardStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(
                    request.AcceptanceTime)));

        Assert.Equal(
            0,
            contractStore.CreateCount);
        Assert.Equal(
            0,
            contractStore.UpdateCount);
        Assert.Equal(
            0,
            boardStore.SaveCount);
    }

    private static JobOfferAcceptanceService CreateService(
        IJobContractStore contractStore,
        IJobBoardStateStore boardStore,
        JobContractRuntimeState? runtimeState = null) =>
        new(
            contractStore,
            new JobContractLifecycleService(
                contractStore),
            boardStore,
            runtimeState
            ?? CreateRuntimeState(
                contractStore));

    private static JobContractRuntimeState CreateRuntimeState(
        IJobContractStore contractStore) =>
        new(
            new JobContractRecoveryService(
                new EmptyRecoverySource(),
                contractStore));

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
            Offer: Offer(),
            AcceptanceTime:
                OfferedAt.AddMinutes(10),
            AircraftRequirements:
                new AircraftMissionRequirements(
                    RequiredCapabilities:
                        AircraftCapability.Cargo,
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumPayloadPounds: 500,
                    MinimumRangeNauticalMiles: 150,
                    MinimumSeats: 0),
            EstimatedFlightHours: 1.5,
            PayloadPounds: 500,
            DemandAttractiveness: 1.2,
            Urgency: 0.1,
            Difficulty: 0.1,
            EstimatedPlayerOperatingCosts: 0m,
            MustStartBy:
                OfferedAt.AddHours(2),
            MustCompleteBy:
                OfferedAt.AddHours(4),
            MarketId:
                "KRME:general-cargo");

    private static JobMarketOfferDraft Offer() =>
        new(
            OfferId:
                Guid.Parse(
                    "93000000-0000-0000-0000-000000000001"),
            ServiceTrack:
                ServiceTrack.CivilianEmployment,
            Kind:
                ContractKind.Cargo,
            Scenario:
                JobScenarioKind.Standard,
            OriginIcao:
                "KRME",
            DestinationIcao:
                "KSYR",
            DistanceNm:
                150,
            EstimatedFlightHours:
                1.5,
            OfferedAt:
                OfferedAt,
            ExpiresAt:
                OfferedAt.AddHours(1),
            IsLockedPreview:
                false,
            RouteStrength:
                0.4,
            RelationshipStrength:
                0.3,
            MarketSelectionWeight:
                1.0);

    private static ContractDispatchContext DispatchContext(
        DateTimeOffset time) =>
        new(
            time,
            new AircraftCapabilityProfile(
                "cargo-fixture",
                "Cargo fixture",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                MaximumPayloadPounds: 2_000,
                MaximumRangeNauticalMiles: 1_000,
                TypicalCruiseKnots: 150,
                Seats: 4,
                EngineCount: 1,
                IfrCapable: true,
                Pressurized: false,
                RetractableGear: false),
            AircraftAccess.Civilian,
            new WorldEventEffects(),
            QualificationsVerified: true,
            DispatchFeasibilityVerified: true);

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

    private sealed class FakeContractStore : IJobContractStore
    {
        private PersistedJobContract? _current;

        public FakeContractStore(
            PersistedJobContract? initial = null)
        {
            _current = initial;
        }

        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PersistedJobContract? result =
                _current?.Contract.ContractId
                    == contractId
                    ? _current
                    : null;

            return Task.FromResult(result);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contract.Validate();
            CreateCount++;

            if (contract.Status
                != ContractStatus.Offered)
            {
                throw new InvalidOperationException(
                    "New contract must be offered.");
            }

            if (_current is null)
            {
                _current =
                    new PersistedJobContract(
                        contract,
                        Version: 0);

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

    private sealed class FakeBoardStore : IJobBoardStateStore
    {
        public FakeBoardStore(
            JobBoardState? initial)
        {
            State = initial;
        }

        public JobBoardState? State { get; private set; }
        public int SaveAttempts { get; private set; }
        public int SaveCount { get; private set; }
        public bool FailNextSave { get; set; }

        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Validate();
            SaveAttempts++;

            if (FailNextSave)
            {
                FailNextSave = false;

                throw new InvalidOperationException(
                    "Simulated board persistence failure.");
            }

            State = state;
            SaveCount++;
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

            IReadOnlyList<JobBoardState> states =
                State is null
                    ? Array.Empty<JobBoardState>()
                    : [State];

            return Task.FromResult(states);
        }
    }
}
