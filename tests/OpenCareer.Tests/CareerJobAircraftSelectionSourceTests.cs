using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Tests;

public sealed class CareerJobAircraftSelectionSourceTests
{
    private static readonly Guid CareerId =
        Guid.Parse(
            "a6000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now =
        new(2026, 9, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ZeroOwnershipCareerReceivesPersistedProviderAircraftWithoutSimulatorDiscovery()
    {
        ProviderAircraftAssignment provider =
            ProviderAircraft(
                "provider-aircraft-1",
                "Provider Aircraft");

        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId));

        var availability =
            new TestAircraftAvailabilityStore();

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                ownership,
                availability,
                new FakeBoardStore(
                    BoardWithProvider(
                        provider)),
                new FixedTimeProvider(
                    Now));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        CareerJobAircraftOption option =
            Assert.Single(
                snapshot.Aircraft);

        Assert.Null(option.OwnershipId);
        Assert.Equal(
            provider.ProviderAircraftInstanceId,
            option.ProviderAircraftInstanceId);
        Assert.Equal(
            ProviderOfferId,
            option.ProviderOfferId);
        Assert.Equal(
            provider.AircraftId,
            option.AircraftId);
        Assert.Equal(
            "KRME",
            option.ProviderOriginIcao);
        Assert.Equal(
            provider.AircraftId,
            Assert.Single(
                availability.RequestedAircraftIds));
        Assert.Empty(
            ownership.Snapshot.Aircraft);
    }

    [Fact]
    public async Task ProviderAircraftPrecedesDistinctOwnedAlternative()
    {
        ProviderAircraftAssignment provider =
            ProviderAircraft(
                "provider-aircraft-a",
                "Provider Aircraft A");

        OwnedAircraft owned =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-instance-b",
                "owned-aircraft-b",
                "Owned Aircraft B");

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                new TestOwnershipStore(
                    CareerAircraftTestData.Snapshot(
                        CareerId,
                        owned)),
                new TestAircraftAvailabilityStore(),
                new FakeBoardStore(
                    BoardWithProvider(
                        provider)),
                new FixedTimeProvider(
                    Now));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.Equal(
            2,
            snapshot.Aircraft.Count);

        CareerJobAircraftOption supplied =
            snapshot.Aircraft[0];

        CareerJobAircraftOption alternative =
            snapshot.Aircraft[1];

        Assert.Equal(
            provider.ProviderAircraftInstanceId,
            supplied.ProviderAircraftInstanceId);
        Assert.Null(supplied.OwnershipId);
        Assert.Equal(
            "owned-instance-b",
            alternative.OwnershipId);
        Assert.Null(
            alternative.ProviderAircraftInstanceId);
        Assert.NotEqual(
            provider.ProviderAircraftInstanceId.ToString("D"),
            owned.OwnershipId);
        Assert.NotEqual(
            provider.AircraftId,
            owned.AircraftId);
    }

    [Fact]
    public async Task OwnedAircraftAppearsWithoutInstalledAircraftDiscovery()
    {
        OwnedAircraft owned =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-instance-1",
                "canonical-aircraft-1",
                "Career Aircraft");

        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId,
                    owned));

        var availability =
            new TestAircraftAvailabilityStore();

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                ownership,
                availability);

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        CareerJobAircraftOption option =
            Assert.Single(
                snapshot.Aircraft);

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(
            "ownership:owned-instance-1",
            option.SelectionId);
        Assert.Equal(
            "owned-instance-1",
            option.OwnershipId);
        Assert.Equal(
            "canonical-aircraft-1",
            option.AircraftId);
        Assert.Equal(
            "canonical-aircraft-1",
            Assert.Single(
                availability.RequestedAircraftIds));
    }

    [Fact]
    public async Task ZeroOwnershipIsValidEmptyStateWithoutStarterAircraft()
    {
        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId));

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                ownership,
                new TestAircraftAvailabilityStore());

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.True(snapshot.IsAvailable);
        Assert.Empty(snapshot.Aircraft);
        Assert.Contains(
            "No contract-supplied or available career-owned aircraft",
            snapshot.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyActiveLocalAvailableOwnedAircraftAreSurfaced()
    {
        OwnedAircraft available =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-available",
                "canonical-available",
                "Available");

        OwnedAircraft unavailable =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-unavailable",
                "canonical-unavailable",
                "Unavailable");

        OwnedAircraft remote =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-remote",
                "canonical-remote",
                "Remote",
                airportIcao:
                    "KDFW");

        OwnedAircraft sold =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-sold",
                "canonical-sold",
                "Sold",
                status:
                    OwnedAircraftStatus.Sold);

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                new TestOwnershipStore(
                    CareerAircraftTestData.Snapshot(
                        CareerId,
                        available,
                        unavailable,
                        remote,
                        sold)),
                new TestAircraftAvailabilityStore(
                    new AircraftAvailabilityState(
                        "canonical-unavailable",
                        AircraftAvailabilityStatus.Unavailable)));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.Equal(
            "owned-available",
            Assert.Single(snapshot.Aircraft).OwnershipId);
    }

    [Fact]
    public async Task OwnedInstancesRemainDistinctWhileAvailabilityUsesCanonicalIdentity()
    {
        var availability =
            new TestAircraftAvailabilityStore();

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                new TestOwnershipStore(
                    CareerAircraftTestData.Snapshot(
                        CareerId,
                        CareerAircraftTestData.Owned(
                            CareerId,
                            "owned-instance-a",
                            "canonical-shared",
                            "Shared Type A"),
                        CareerAircraftTestData.Owned(
                            CareerId,
                            "owned-instance-b",
                            "canonical-shared",
                            "Shared Type B"))),
                availability);

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.Equal(
            2,
            snapshot.Aircraft.Count);
        Assert.Equal(
            2,
            snapshot.Aircraft
                .Select(static option => option.OwnershipId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            snapshot.Aircraft,
            static option => Assert.Equal(
                "canonical-shared",
                option.AircraftId));
        Assert.Equal(
            "canonical-shared",
            Assert.Single(
                availability.RequestedAircraftIds));
    }

    [Fact]
    public async Task MissingActiveCareerDoesNotReadOwnership()
    {
        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId));

        var source =
            new CareerJobAircraftSelectionSource(
                new PlayerCareerRuntimeState(
                    new FakeProfileStore(
                        record:
                            null)),
                ownership,
                new TestAircraftAvailabilityStore());

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.False(snapshot.IsAvailable);
        Assert.Empty(snapshot.Aircraft);
        Assert.Empty(ownership.LoadedCareerIds);
    }

    private static PlayerCareerRuntimeState CareerRuntime()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                CareerId,
                "KRME",
                Now.AddDays(-10));

        return new PlayerCareerRuntimeState(
            new FakeProfileStore(
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        1,
                    profile,
                    SavedAt:
                        Now.AddDays(-1))));
    }

    private static readonly Guid ProviderOfferId =
        Guid.Parse(
            "a6000000-0000-0000-0000-000000000002");

    private static ProviderAircraftAssignment ProviderAircraft(
        string aircraftId,
        string displayName) =>
        new(
            Guid.Parse(
                "a6000000-0000-0000-0000-000000000003"),
            aircraftId,
            displayName,
            "KRME");

    private static JobBoardState BoardWithProvider(
        ProviderAircraftAssignment provider)
    {
        DateTimeOffset offeredAt =
            Now.AddHours(-1);

        var requirements =
            new AircraftMissionRequirements(
                AllowedAccess:
                    AircraftAccess.Civilian,
                MinimumRangeNauticalMiles:
                    45);

        var offer =
            new JobMarketOfferDraft(
                ProviderOfferId,
                ServiceTrack.CivilianEmployment,
                ContractKind.Ferry,
                JobScenarioKind.Standard,
                "KRME",
                "KSYR",
                DistanceNm:
                    45,
                EstimatedFlightHours:
                    0.5,
                offeredAt,
                Now.AddHours(1),
                IsLockedPreview:
                    false,
                RouteStrength:
                    0,
                RelationshipStrength:
                    0,
                MarketSelectionWeight:
                    1,
                ContractTerms:
                    new JobMarketContractTermsEnvelope(
                        PersistedJobContractTermsSource.AuthorityId,
                        requirements,
                        EstimatedFlightHours:
                            0.5,
                        PayloadPounds:
                            0,
                        DemandAttractiveness:
                            1,
                        Urgency:
                            0,
                        Difficulty:
                            0,
                        RequiredPilotQualifications:
                            PilotQualificationState.Entry,
                        AuthorizedAircraftAccess:
                            AircraftAccess.Civilian,
                        ProviderAircraft:
                            provider));

        return JobBoardState
            .Empty(
                "KRME",
                offeredAt)
            .Reconcile(
                Now,
                1,
                [offer]);
    }

    private sealed class FakeBoardStore(
        JobBoardState? state)
        : IJobBoardStateStore
    {
        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                state);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
