using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenCareer.Tests;

public sealed class DevelopmentFlightTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DevelopmentIdentityAndTermsSurviveSqliteRecreation()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new OpenCareerDatabaseOptions(Path.Combine(root, "opencareer.db"));
            var firstStore = new SqliteJobBoardStateStore(options, NullLogger<SqliteJobBoardStateStore>.Instance);
            var offer = DevelopmentFlight.CreateOffer(Guid.NewGuid(), Now);
            await firstStore.SaveAsync(JobBoardState.Empty("KJFK", Now).Reconcile(Now, 1, [offer]));
            var secondStore = new SqliteJobBoardStateStore(options, NullLogger<SqliteJobBoardStateStore>.Instance);
            var restored = Assert.Single((await secondStore.GetAsync("KJFK"))!.Offers);
            Assert.Equal(offer.OfferId, restored.OfferId);
            Assert.True(DevelopmentFlight.IsDevelopment(restored));
            Assert.Equal(0, restored.ContractTerms!.ReputationReward);
            Assert.Equal("KJFK", restored.DestinationIcao);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitGenerationPreservesProductionOffersAndReusesPendingTest()
    {
        var fixture = new Fixture();
        JobMarketOfferDraft normal = DevelopmentFlight.CreateOffer(Guid.NewGuid(), Now) with
        {
            DestinationIcao = "KALB", ContractTerms = null
        };
        fixture.Board = fixture.Board with { Offers = fixture.Board.Offers.Add(normal) };
        JobMarketOfferDraft first = await fixture.Service.GenerateAsync();
        JobMarketOfferDraft second = await fixture.Service.GenerateAsync();
        Assert.Equal(first, second);
        Assert.Equal("KJFK", first.OriginIcao);
        Assert.Equal("KJFK", first.DestinationIcao);
        Assert.True(first.IsLocalOperation);
        Assert.Contains(normal, fixture.Board.Offers);
        Assert.Single(fixture.Board.Offers.Where(DevelopmentFlight.IsDevelopment));
        Assert.Null(first.ContractTerms!.ProviderAircraft); // Assignment remains the standard resolver's responsibility.
        Assert.Equal(PilotQualificationState.Entry, first.ContractTerms.RequiredPilotQualifications);
        fixture.Board.Validate();
    }

    [Fact]
    public async Task RetiredCompletedTestCanBeReplacedWithoutWaitingForMarketRefresh()
    {
        var fixture = new Fixture();
        JobMarketOfferDraft first = await fixture.Service.GenerateAsync();
        fixture.Board = fixture.Board.Retire(first.OfferId, Now);
        fixture.Candidates = [new(first.OfferId, ContractStatus.Completed, 3, Now)];
        JobMarketOfferDraft second = await fixture.Service.GenerateAsync();
        Assert.NotEqual(first.OfferId, second.OfferId);
        Assert.Single(fixture.Board.Offers);
        Assert.Contains(first.OfferId, fixture.Board.RetiredOfferIds);
    }

    [Fact]
    public async Task RepeatedConcurrentGenerationCreatesOnlyOnePendingOffer()
    {
        var fixture = new Fixture();
        var offers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => fixture.Service.GenerateAsync()));
        Assert.Single(offers.Select(offer => offer.OfferId).Distinct());
        Assert.Single(fixture.Board.Offers);
    }

    [Theory]
    [InlineData(ContractStatus.Accepted)]
    [InlineData(ContractStatus.InProgress)]
    public async Task ActiveContractBlocksGenerationAndPositioning(ContractStatus status)
    {
        var fixture = new Fixture("KALB");
        fixture.Candidates = [new(Guid.NewGuid(), status, 1, Now)];
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GenerateAsync(true));
        Assert.Empty(fixture.Board.Offers);
        Assert.Equal("KALB", fixture.Profile.Profile.Location.CurrentAirportIcao);
    }

    [Fact]
    public async Task CheckpointBlocksGenerationEvenWhenContractCompleted()
    {
        var fixture = new Fixture();
        fixture.Checkpoint = FlightSession.Start(Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GenerateAsync());
        Assert.Empty(fixture.Board.Offers);
    }

    [Fact]
    public async Task PositioningRequiresExplicitOptInAndPreservesCareerHomeAndHistory()
    {
        var fixture = new Fixture("KALB");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GenerateAsync());
        var original = fixture.Profile.Profile;
        await fixture.Service.GenerateAsync(true);
        Assert.Equal("KJFK", fixture.Profile.Profile.Location.CurrentAirportIcao);
        Assert.Equal(original.Location.HomeAirportIcao, fixture.Profile.Profile.Location.HomeAirportIcao);
        Assert.Equal(original.Location.AppliedTravelContracts, fixture.Profile.Profile.Location.AppliedTravelContracts);
        Assert.Equal(original.Experience, fixture.Profile.Profile.Experience);
    }

    [Fact]
    public async Task ProviderResolutionNeedsNoInstalledCatalogAndTermsHaveZeroPay()
    {
        var fixture = new Fixture();
        JobMarketOfferDraft offer = await fixture.Service.GenerateAsync();
        var terms = Assert.IsType<CareerJobContractTermsEvidence>(
            await new PersistedJobContractTermsSource().ReadAsync(offer, fixture.Profile.Profile));
        var resolver = new ProviderAircraftAssignmentResolver();
        var provider = Assert.IsType<ProviderAircraftAssignment>(
            await resolver.ResolveAsync(offer, terms.AircraftRequirements));
        Assert.Equal(provider, await resolver.ResolveAsync(offer, terms.AircraftRequirements));
        Assert.Equal(PlayableLoopAircraftRegistrySource.AircraftId, provider.AircraftId);
        Assert.Equal("KJFK", provider.OriginIcao);
        var request = new JobContractCreationRequest(offer, Now, terms.AircraftRequirements,
            terms.EstimatedFlightHours, 0, 1, 0, 0, ReputationReward: 0, ReputationPenalty: 0,
            MarketId: DevelopmentFlight.MarketId, ProviderAircraft: provider);
        JobContract contract = JobContractFactory.Create(request);
        Assert.Equal(provider, contract.ProviderAircraft);
        Assert.Equal(0m, contract.Compensation.PilotCompensation);
        Assert.Equal(0m, contract.Compensation.GrossCustomerRevenue);
        Assert.Equal(0, contract.ReputationReward);
        Assert.Equal(0, contract.ReputationPenalty);
        Assert.True(contract.Compensation.EmployerCoversFuel);
        Assert.True(contract.Compensation.EmployerCoversMaintenance);
        Assert.True(contract.Compensation.EmployerCoversAirportFees);

        var production = offer with { ContractTerms = offer.ContractTerms! with { MarketId = null } };
        Assert.True(JobContractFactory.Create(request with { Offer = production, MarketId = null })
            .Compensation.PilotCompensation > 0);
    }

    private sealed class Fixture : IJobBoardStateStore, IPlayerCareerProfileStore,
        IJobContractRecoverySource, IFlightSessionCheckpointStore
    {
        public JobBoardState Board = JobBoardState.Empty("KJFK", Now);
        public PlayerCareerProfileStoreRecord Profile;
        public IReadOnlyList<JobContractRecoveryCandidate> Candidates = [];
        public FlightSession? Checkpoint;
        public DevelopmentFlightService Service { get; }
        public Fixture(string airport = "KJFK")
        {
            Profile = new(1, PlayerCareerProfile.Start(Guid.NewGuid(), airport, Now), Now);
            var runtime = new PlayerCareerRuntimeState(this);
            Service = new(new JobBoardGenerationService(this), runtime, this, this, new Clock(),
                new PlayerCareerLocationCoordinator(this, runtime));
        }
        public Task SaveAsync(JobBoardState state, CancellationToken cancellationToken = default)
        { Board = state; return Task.CompletedTask; }
        public Task<JobBoardState?> GetAsync(string airportIcao, CancellationToken cancellationToken = default) => Task.FromResult<JobBoardState?>(Board);
        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<JobBoardState>>([Board]);
        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<PlayerCareerProfileStoreRecord?>(Profile);
        public Task<PlayerCareerProfileStoreRecord> SaveAsync(PlayerCareerProfile profile, long? expectedRevision, DateTimeOffset savedAt, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Profile.Revision, expectedRevision);
            Profile = new(Profile.Revision + 1, profile, savedAt);
            return Task.FromResult(Profile);
        }
        public Task<IReadOnlyList<JobContractRecoveryCandidate>> ReadRecoveryCandidatesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Candidates);
        Task<FlightSession?> IFlightSessionCheckpointStore.LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Checkpoint);
        public Task SaveAsync(FlightSession session, CancellationToken cancellationToken = default)
        { Checkpoint = session; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        { Checkpoint = null; return Task.CompletedTask; }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
