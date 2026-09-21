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
    public async Task ActiveOfferCreatesQuotedContractAndAcceptsIt()
    {
        var store = new FakeStore();
        var lifecycle =
            new JobContractLifecycleService(store);
        var service =
            new JobOfferAcceptanceService(
                store,
                lifecycle);

        JobContractCreationRequest request =
            Request();

        PersistedJobContract result =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            request.Offer.OfferId,
            result.Contract.ContractId);
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
        Assert.True(
            result.Contract.Compensation.PilotCompensation > 0m);
        Assert.True(
            result.Contract.Compensation.EmployerCoversFuel);
        Assert.Equal(1, store.CreateCount);
        Assert.Equal(1, store.UpdateCount);
    }

    [Fact]
    public async Task SuccessfulAcceptanceRetryIsIdempotent()
    {
        var store = new FakeStore();
        var lifecycle =
            new JobContractLifecycleService(store);
        var service =
            new JobOfferAcceptanceService(
                store,
                lifecycle);

        JobContractCreationRequest request =
            Request();

        PersistedJobContract first =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(request.AcceptanceTime));

        PersistedJobContract second =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            first.Contract,
            second.Contract);
        Assert.Equal(
            first.Version,
            second.Version);
        Assert.Equal(1, store.CreateCount);
        Assert.Equal(1, store.UpdateCount);
    }

    [Fact]
    public async Task PersistedOfferedContractResumesAcceptanceAfterInterruptedFlow()
    {
        JobContractCreationRequest request =
            Request();

        JobContract offered =
            JobContractFactory.Create(request);

        var store =
            new FakeStore(
                new PersistedJobContract(
                    offered,
                    Version: 0));

        var service =
            new JobOfferAcceptanceService(
                store,
                new JobContractLifecycleService(store));

        PersistedJobContract result =
            await service.AcceptOfferAsync(
                request,
                DispatchContext(request.AcceptanceTime));

        Assert.Equal(
            ContractStatus.Accepted,
            result.Contract.Status);
        Assert.Equal(0, store.CreateCount);
        Assert.Equal(1, store.UpdateCount);
    }

    [Fact]
    public async Task LockedPreviewIsRejectedBeforePersistence()
    {
        var store = new FakeStore();
        var service =
            new JobOfferAcceptanceService(
                store,
                new JobContractLifecycleService(store));

        JobContractCreationRequest request =
            Request() with
            {
                Offer =
                    Offer() with
                    {
                        IsLockedPreview = true
                    }
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(request.AcceptanceTime)));

        Assert.Equal(0, store.CreateCount);
        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task ExpiredOfferIsRejectedBeforePersistence()
    {
        var store = new FakeStore();
        var service =
            new JobOfferAcceptanceService(
                store,
                new JobContractLifecycleService(store));

        JobContractCreationRequest request =
            Request() with
            {
                AcceptanceTime =
                    Offer().ExpiresAt
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(request.AcceptanceTime)));

        Assert.Equal(0, store.CreateCount);
        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task InvalidDispatchLeavesRecoverableOfferedContract()
    {
        var store = new FakeStore();
        var service =
            new JobOfferAcceptanceService(
                store,
                new JobContractLifecycleService(store));

        JobContractCreationRequest request =
            Request();

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
            await store.ReadJobContractAsync(
                request.Offer.OfferId);

        Assert.NotNull(persisted);
        Assert.Equal(
            ContractStatus.Offered,
            persisted.Contract.Status);
        Assert.Equal(0, persisted.Version);
        Assert.Equal(1, store.CreateCount);
        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task ExistingDifferentContractUnderOfferIdentityIsRejected()
    {
        JobContractCreationRequest request =
            Request();

        JobContract conflicting =
            JobContractFactory.Create(request) with
            {
                DestinationIcao = "KBUF"
            };
        conflicting.Validate();

        var store =
            new FakeStore(
                new PersistedJobContract(
                    conflicting,
                    Version: 0));

        var service =
            new JobOfferAcceptanceService(
                store,
                new JobContractLifecycleService(store));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptOfferAsync(
                request,
                DispatchContext(request.AcceptanceTime)));

        Assert.Equal(0, store.CreateCount);
        Assert.Equal(0, store.UpdateCount);
    }

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

    private sealed class FakeStore : IJobContractStore
    {
        private PersistedJobContract? _current;

        public FakeStore(
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
                _current?.Contract.ContractId == contractId
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

            if (contract.Status != ContractStatus.Offered)
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
}
