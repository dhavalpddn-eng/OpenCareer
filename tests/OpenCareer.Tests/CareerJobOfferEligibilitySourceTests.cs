using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class CareerJobOfferEligibilitySourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NoCareerFailsClosedWithoutReadingBoardOrAircraft()
    {
        var fixture = Fixture(profile: null, board: null);

        CareerJobOfferEligibilitySnapshot snapshot =
            await fixture.Source.ReadAsync();

        Assert.Equal(CareerJobOfferEligibilityState.NoCareer, snapshot.State);
        Assert.Empty(snapshot.Offers);
        Assert.Equal(0, fixture.Boards.ReadCount);
        Assert.Equal(0, fixture.Discovery.ReadCount);
        AssertNoWrites(fixture);
    }

    [Fact]
    public async Task MissingLocalBoardFailsClosedWithoutReadingAircraft()
    {
        var fixture = Fixture(Profile(), board: null);

        CareerJobOfferEligibilitySnapshot snapshot =
            await fixture.Source.ReadAsync();

        Assert.Equal(CareerJobOfferEligibilityState.NoBoard, snapshot.State);
        Assert.Empty(snapshot.Offers);
        Assert.Equal("KDFW", fixture.Boards.LastAirportIcao);
        Assert.Equal(0, fixture.Discovery.ReadCount);
        AssertNoWrites(fixture);
    }

    [Fact]
    public async Task UnavailableAircraftDiscoveryFailsClosed()
    {
        JobMarketOfferDraft offer = Offer(1);
        var fixture = Fixture(
            Profile(),
            Board(offer),
            InstalledAircraftDiscoverySnapshot.Unavailable);

        CareerJobOfferEligibilitySnapshot snapshot =
            await fixture.Source.ReadAsync();

        Assert.Equal(
            CareerJobOfferEligibilityState.AircraftDiscoveryUnavailable,
            snapshot.State);
        Assert.Empty(snapshot.Offers);
        Assert.Empty(fixture.Action.Reads);
        AssertNoWrites(fixture);
    }

    [Fact]
    public async Task LockedExpiredAndFutureOffersAreExcludedBeforeReadiness()
    {
        JobMarketOfferDraft locked = Offer(1, locked: true);
        JobMarketOfferDraft expired = Offer(
            2,
            offeredAt: Now.AddHours(-2),
            expiresAt: Now);
        JobMarketOfferDraft future = Offer(
            3,
            offeredAt: Now.AddMinutes(1),
            expiresAt: Now.AddHours(2));
        JobMarketOfferDraft active = Offer(4);
        var fixture = Fixture(
            Profile(),
            Board(locked, expired, future, active));
        fixture.Action.CanStart.Add((active.OfferId, "aircraft-a"));

        CareerJobOfferEligibilitySnapshot snapshot =
            await fixture.Source.ReadAsync();

        CareerJobEligibleOffer eligible = Assert.Single(snapshot.Offers);
        Assert.Equal(active.OfferId, eligible.Offer.OfferId);
        Assert.All(
            fixture.Action.Reads,
            read => Assert.Equal(active.OfferId, read.OfferId));
        AssertNoWrites(fixture);
    }

    [Fact]
    public async Task UnstartableOffersAreExcludedAndEmptyResultIsExplicit()
    {
        JobMarketOfferDraft offer = Offer(1);
        var fixture = Fixture(Profile(), Board(offer));

        CareerJobOfferEligibilitySnapshot snapshot =
            await fixture.Source.ReadAsync();

        Assert.Equal(
            CareerJobOfferEligibilityState.NoEligibleOffers,
            snapshot.State);
        Assert.False(snapshot.IsReady);
        Assert.Empty(snapshot.Offers);
        Assert.Equal(2, fixture.Action.Reads.Count);
        AssertNoWrites(fixture);
    }

    [Fact]
    public async Task StartableOffersRetainOnlyQualifyingAircraftInSourceOrder()
    {
        JobMarketOfferDraft first = Offer(1);
        JobMarketOfferDraft second = Offer(2);
        var fixture = Fixture(Profile(), Board(second, first));
        fixture.Action.CanStart.Add((second.OfferId, "aircraft-z"));
        fixture.Action.CanStart.Add((first.OfferId, "aircraft-a"));
        fixture.Action.CanStart.Add((first.OfferId, "aircraft-z"));

        CareerJobOfferEligibilitySnapshot snapshot =
            await fixture.Source.ReadAsync();

        Assert.True(snapshot.IsReady);
        Assert.Equal(
            [second.OfferId, first.OfferId],
            snapshot.Offers.Select(item => item.Offer.OfferId));
        Assert.Equal(
            ["aircraft-z"],
            snapshot.Offers[0].QualifyingAircraft.Select(item => item.AircraftId));
        Assert.Equal(
            ["aircraft-a", "aircraft-z"],
            snapshot.Offers[1].QualifyingAircraft.Select(item => item.AircraftId));
        Assert.Equal(
            ["Alpha", "Zulu"],
            snapshot.Offers[1].QualifyingAircraft.Select(item => item.DisplayName));
        Assert.Equal(
            [
                (second.OfferId, "aircraft-a"),
                (second.OfferId, "aircraft-z"),
                (first.OfferId, "aircraft-a"),
                (first.OfferId, "aircraft-z")
            ],
            fixture.Action.Reads);
        Assert.Equal(0, fixture.Action.StartCount);
        AssertNoWrites(fixture);
    }

    private static TestFixture Fixture(
        PlayerCareerProfile? profile,
        JobBoardState? board,
        InstalledAircraftDiscoverySnapshot? discovery = null)
    {
        var profiles =
            new FakeProfileStore(
                profile is null
                    ? null
                    : new PlayerCareerProfileStoreRecord(
                        1,
                        profile,
                        Now));
        var boards = new FakeBoardStore(board);
        var installed =
            new CountingDiscovery(
                discovery
                ?? new InstalledAircraftDiscoverySnapshot(
                    InstalledAircraftDiscoveryAvailability.Available,
                    [
                        Observation("aircraft-z", "Zulu"),
                        Observation("aircraft-a", "Alpha")
                    ]));
        var action = new FakeStartAction();
        var source =
            new CareerJobOfferEligibilitySource(
                boards,
                new PlayerCareerRuntimeState(profiles),
                new CareerJobAircraftSelectionSource(installed),
                action,
                new FixedTimeProvider(Now));

        return new(source, profiles, boards, installed, action);
    }

    private static PlayerCareerProfile Profile() =>
        PlayerCareerProfile.Start(
            Guid.Parse("0ec05ca7-e55e-4306-824d-653d14fbd30b"),
            "KDFW",
            Now.AddDays(-1));

    private static JobBoardState Board(
        params JobMarketOfferDraft[] offers)
    {
        var board =
            new JobBoardState(
                "KDFW",
                Now.AddMinutes(-10),
                offers.ToImmutableArray(),
                ImmutableHashSet<Guid>.Empty);
        board.Validate();
        return board;
    }

    private static JobMarketOfferDraft Offer(
        int id,
        bool locked = false,
        DateTimeOffset? offeredAt = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            new Guid(id, 0, 0, new byte[8]),
            ServiceTrack.CivilianEmployment,
            ContractKind.Ferry,
            JobScenarioKind.Standard,
            "KDFW",
            "KDAL",
            DistanceNm: 25,
            EstimatedFlightHours: 0.3,
            OfferedAt: offeredAt ?? Now.AddHours(-1),
            ExpiresAt: expiresAt ?? Now.AddHours(1),
            IsLockedPreview: locked,
            RouteStrength: 0.5,
            RelationshipStrength: 0.5,
            MarketSelectionWeight: 1);

    private static AircraftRegistryObservation Observation(
        string aircraftId,
        string displayName) =>
        new(
            aircraftId,
            ProviderId: "fixture",
            ProviderRecordId: displayName,
            AircraftDataConfidence.Verified,
            IsInstalled: true,
            DisplayName: displayName);

    private static void AssertNoWrites(TestFixture fixture)
    {
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Equal(0, fixture.Boards.SaveCount);
        Assert.Equal(0, fixture.Action.StartCount);
    }

    private sealed record TestFixture(
        CareerJobOfferEligibilitySource Source,
        FakeProfileStore Profiles,
        FakeBoardStore Boards,
        CountingDiscovery Discovery,
        FakeStartAction Action);

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            throw new InvalidOperationException("Eligibility reads must not save the career.");
        }
    }

    private sealed class FakeBoardStore(JobBoardState? board)
        : IJobBoardStateStore
    {
        public int ReadCount { get; private set; }
        public int SaveCount { get; private set; }
        public string? LastAirportIcao { get; private set; }

        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            throw new InvalidOperationException("Eligibility reads must not save the board.");
        }

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            LastAirportIcao = airportIcao;
            return Task.FromResult(board);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Eligibility reads only the local board.");
    }

    private sealed class CountingDiscovery(
        InstalledAircraftDiscoverySnapshot snapshot)
        : IInstalledAircraftDiscoverySource
    {
        private int _readCount;

        public int ReadCount => _readCount;

        public InstalledAircraftDiscoverySnapshot Current
        {
            get
            {
                _readCount++;
                return snapshot;
            }
        }
    }

    private sealed class FakeStartAction : ICareerJobStartAction
    {
        public HashSet<(Guid OfferId, string AircraftId)> CanStart { get; } = [];
        public List<(Guid OfferId, string AircraftId)> Reads { get; } = [];
        public int StartCount { get; private set; }

        public Task<CareerJobStartActionAvailability> ReadAvailabilityAsync(
            Guid offerId,
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add((offerId, aircraftId));
            bool canStart = CanStart.Contains((offerId, aircraftId));
            return Task.FromResult(
                new CareerJobStartActionAvailability(
                    canStart,
                    canStart
                        ? CareerJobStartInputState.Ready
                        : CareerJobStartInputState.PreflightInfeasible,
                    canStart ? "Ready." : "Blocked."));
        }

        public Task<CareerJobPlayableStartResult> StartAsync(
            Guid offerId,
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            throw new InvalidOperationException("Eligibility must never start a job.");
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
