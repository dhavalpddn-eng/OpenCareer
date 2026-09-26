using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class DevelopmentFlightTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DevelopmentIdentityAndTermsSurviveSqliteRecreation()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        try
        {
            var options =
                new OpenCareerDatabaseOptions(
                    Path.Combine(
                        root,
                        "opencareer.db"));

            var firstStore =
                new SqliteJobBoardStateStore(
                    options,
                    NullLogger<SqliteJobBoardStateStore>.Instance);

            JobMarketOfferDraft offer =
                DevelopmentFlight.CreateOffer(
                    Guid.NewGuid(),
                    Now);

            await firstStore.SaveAsync(
                JobBoardState
                    .Empty(
                        "KJFK",
                        Now)
                    .Reconcile(
                        Now,
                        1,
                        [offer]));

            var secondStore =
                new SqliteJobBoardStateStore(
                    options,
                    NullLogger<SqliteJobBoardStateStore>.Instance);

            JobMarketOfferDraft restored =
                Assert.Single(
                    (await secondStore.GetAsync("KJFK"))!
                        .Offers);

            Assert.Equal(
                offer.OfferId,
                restored.OfferId);
            Assert.True(
                DevelopmentFlight.IsDevelopment(restored));
            Assert.Equal(
                0,
                restored.ContractTerms?.ReputationReward);
            Assert.Equal(
                0,
                restored.ContractTerms?.ReputationPenalty);
            Assert.Equal(
                "KJFK",
                restored.OriginIcao);
            Assert.Equal(
                "KJFK",
                restored.DestinationIcao);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (Directory.Exists(root))
            {
                Directory.Delete(
                    root,
                    recursive:
                        true);
            }
        }
    }

    [Fact]
    public async Task ExplicitGenerationPreservesProductionOffersAndReusesPendingTest()
    {
        var fixture =
            new Fixture();

        JobMarketOfferDraft production =
            DevelopmentFlight.CreateOffer(
                Guid.NewGuid(),
                Now) with
            {
                DestinationIcao = "KALB",
                ContractTerms = null
            };

        fixture.Board =
            fixture.Board with
            {
                Offers = fixture.Board.Offers.Add(production)
            };

        JobMarketOfferDraft first =
            await fixture.Service.GenerateAsync();

        JobMarketOfferDraft second =
            await fixture.Service.GenerateAsync();

        Assert.Equal(
            first,
            second);
        Assert.Equal(
            "KJFK",
            first.OriginIcao);
        Assert.Equal(
            "KJFK",
            first.DestinationIcao);
        Assert.True(
            first.IsLocalOperation);
        Assert.Contains(
            production,
            fixture.Board.Offers);
        Assert.Single(
            fixture.Board.Offers.Where(
                DevelopmentFlight.IsDevelopment));
        Assert.Equal(
            PilotQualificationState.Entry,
            first.ContractTerms?.RequiredPilotQualifications);

        fixture.Board.Validate();
    }

    [Fact]
    public async Task RetiredCompletedTestCanBeReplacedImmediately()
    {
        var fixture =
            new Fixture();

        JobMarketOfferDraft first =
            await fixture.Service.GenerateAsync();

        fixture.Board =
            fixture.Board.Retire(
                first.OfferId,
                Now);

        fixture.Candidates =
        [
            new JobContractRecoveryCandidate(
                first.OfferId,
                ContractStatus.Completed,
                Version:
                    3,
                UpdatedAt:
                    Now)
        ];

        JobMarketOfferDraft second =
            await fixture.Service.GenerateAsync();

        Assert.NotEqual(
            first.OfferId,
            second.OfferId);
        Assert.Single(
            fixture.Board.Offers);
        Assert.Contains(
            first.OfferId,
            fixture.Board.RetiredOfferIds);
    }

    [Fact]
    public async Task RepeatedConcurrentGenerationCreatesOnePendingOffer()
    {
        var fixture =
            new Fixture();

        JobMarketOfferDraft[] offers =
            await Task.WhenAll(
                Enumerable
                    .Range(
                        0,
                        5)
                    .Select(
                        _ => fixture.Service.GenerateAsync()));

        Assert.Single(
            offers
                .Select(
                    static offer => offer.OfferId)
                .Distinct());
        Assert.Single(
            fixture.Board.Offers);
    }

    [Theory]
    [InlineData(ContractStatus.Accepted)]
    [InlineData(ContractStatus.InProgress)]
    public async Task ActiveContractBlocksGenerationAndPositioning(
        ContractStatus status)
    {
        var fixture =
            new Fixture("KALB");

        fixture.Candidates =
        [
            new JobContractRecoveryCandidate(
                Guid.NewGuid(),
                status,
                Version:
                    1,
                UpdatedAt:
                    Now)
        ];

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.GenerateAsync(
                positionPilotAtKjfk:
                    true));

        Assert.Empty(
            fixture.Board.Offers);
        Assert.Equal(
            "KALB",
            fixture.Profile.Profile.Location.CurrentAirportIcao);
    }

    [Fact]
    public async Task FlightCheckpointBlocksGeneration()
    {
        var fixture =
            new Fixture();

        fixture.Checkpoint =
            FlightSession.Start(Now);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.GenerateAsync());

        Assert.Empty(
            fixture.Board.Offers);
    }

    [Fact]
    public async Task PositioningRequiresExplicitOptInAndPreservesHomeAndProgress()
    {
        var fixture =
            new Fixture("KALB");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.GenerateAsync());

        PlayerCareerProfile original =
            fixture.Profile.Profile;

        await fixture.Service.GenerateAsync(
            positionPilotAtKjfk:
                true);

        Assert.Equal(
            "KJFK",
            fixture.Profile.Profile.Location.CurrentAirportIcao);
        Assert.Equal(
            original.Location.HomeAirportIcao,
            fixture.Profile.Profile.Location.HomeAirportIcao);
        Assert.Equal(
            original.Location.AppliedTravelContracts,
            fixture.Profile.Profile.Location.AppliedTravelContracts);
        Assert.Equal(
            original.Experience,
            fixture.Profile.Profile.Experience);
        Assert.Equal(
            original.Qualifications,
            fixture.Profile.Profile.Qualifications);
    }

    [Fact]
    public void DevelopmentTermsAreZeroRewardWithoutChangingProductionPay()
    {
        JobMarketOfferDraft offer =
            DevelopmentFlight.CreateOffer(
                Guid.NewGuid(),
                Now);

        JobMarketContractTermsEnvelope terms =
            Assert.IsType<JobMarketContractTermsEnvelope>(
                offer.ContractTerms);

        var request =
            new JobContractCreationRequest(
                offer,
                Now,
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

        JobContract development =
            JobContractFactory.Create(request);

        Assert.Equal(
            0m,
            development.Compensation.PilotCompensation);
        Assert.Equal(
            0m,
            development.Compensation.GrossCustomerRevenue);
        Assert.Equal(
            0,
            development.ReputationReward);
        Assert.Equal(
            0,
            development.ReputationPenalty);
        Assert.True(
            development.Compensation.EmployerCoversFuel);
        Assert.True(
            development.Compensation.EmployerCoversMaintenance);
        Assert.True(
            development.Compensation.EmployerCoversAirportFees);

        JobMarketOfferDraft productionOffer =
            offer with
            {
                ContractTerms = terms with
                {
                    MarketId = null
                }
            };

        JobContract production =
            JobContractFactory.Create(
                request with
                {
                    Offer = productionOffer,
                    MarketId = null
                });

        Assert.True(
            production.Compensation.PilotCompensation > 0);
    }

    [Fact]
    public void DevelopmentIdentityMustMatchOfferAndAcceptedTerms()
    {
        JobMarketOfferDraft developmentOffer =
            DevelopmentFlight.CreateOffer(
                Guid.NewGuid(),
                Now);

        JobMarketContractTermsEnvelope terms =
            Assert.IsType<JobMarketContractTermsEnvelope>(
                developmentOffer.ContractTerms);

        var request =
            new JobContractCreationRequest(
                developmentOffer,
                Now,
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
                    null);

        Assert.Throws<InvalidOperationException>(
            () => JobContractFactory.Create(request));

        JobMarketOfferDraft productionOffer =
            developmentOffer with
            {
                ContractTerms = terms with
                {
                    MarketId = null
                }
            };

        Assert.Throws<InvalidOperationException>(
            () => JobContractFactory.Create(
                request with
                {
                    Offer = productionOffer,
                    MarketId = DevelopmentFlight.MarketId
                }));
    }

    private sealed class Fixture
        : IJobBoardStateStore,
          IPlayerCareerProfileStore,
          IJobContractRecoverySource,
          IFlightSessionCheckpointStore
    {
        public Fixture(
            string airport = "KJFK")
        {
            Profile =
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        1,
                    PlayerCareerProfile.Start(
                        Guid.NewGuid(),
                        airport,
                        Now),
                    SavedAt:
                        Now);

            var runtime =
                new PlayerCareerRuntimeState(this);

            Service =
                new DevelopmentFlightService(
                    new JobBoardGenerationService(this),
                    runtime,
                    this,
                    this,
                    new FixedClock(),
                    new PlayerCareerLocationCoordinator(
                        this,
                        runtime));
        }

        public JobBoardState Board { get; set; } =
            JobBoardState.Empty(
                "KJFK",
                Now);

        public PlayerCareerProfileStoreRecord Profile { get; private set; }

        public IReadOnlyList<JobContractRecoveryCandidate> Candidates { get; set; } =
            Array.Empty<JobContractRecoveryCandidate>();

        public FlightSession? Checkpoint { get; set; }

        public DevelopmentFlightService Service { get; }

        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Board = state;
            return Task.CompletedTask;
        }

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<JobBoardState?>(Board);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<JobBoardState>>(
                [Board]);
        }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PlayerCareerProfileStoreRecord?>(
                Profile);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(
                Profile.Revision,
                expectedRevision);

            Profile =
                new PlayerCareerProfileStoreRecord(
                    checked(Profile.Revision + 1),
                    profile,
                    savedAt);

            return Task.FromResult(Profile);
        }

        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Candidates);
        }

        Task<FlightSession?> IFlightSessionCheckpointStore.LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Checkpoint);
        }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint = session;
            return Task.CompletedTask;
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            Now;
    }
}
