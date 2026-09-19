using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public class JobMarketTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JobMarketDestination[] Destinations =
    [
        new("KSYR", 45, 0.80, 0.60, EstimatedFlightHours: 0.5),
        new("KALB", 80, 0.45, 0.30, EstimatedFlightHours: 0.8),
        new("KBUF", 150, 0.10, 0.05, EstimatedFlightHours: 1.3),
        new("KBOS", 220, EstimatedFlightHours: 1.9),
        new("KPHL", 260, EstimatedFlightHours: 2.2),
        new("KIAD", 300, EstimatedFlightHours: 2.6),
        new("KORD", 550, EstimatedFlightHours: 4.0),
        new("KATL", 780, EstimatedFlightHours: 5.6)
    ];

    [Fact]
    public void SameSeedAirportAndGenerationBucketProduceSameBoard()
    {
        var request = Request(InitialAirportProfiles.GriffissInternational, Epoch, JobMarketAccess.All);
        var first = JobMarketGenerator.Generate(request).ToArray();
        var second = JobMarketGenerator.Generate(request).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.All(first, offer =>
        {
            Assert.Equal("KRME", offer.OriginIcao);
            Assert.Equal(JobMarketPolicy.Default.CycleStart(Epoch), offer.OfferedAt);
            Assert.True(offer.ExpiresAt > offer.OfferedAt);
            Assert.InRange(
                offer.ExpiresAt - offer.OfferedAt,
                TimeSpan.FromMinutes(45),
                JobMarketPolicy.Default.EffectiveMaximumOfferLifetime);
        });
    }

    [Fact]
    public void NewGenerationBucketChangesDeterministicBatch()
    {
        var policy = JobMarketPolicy.Default;
        var first = JobMarketGenerator.Generate(Request(InitialAirportProfiles.GriffissInternational, Epoch, JobMarketAccess.All));
        var second = JobMarketGenerator.Generate(Request(
            InitialAirportProfiles.GriffissInternational,
            Epoch + policy.RefreshInterval,
            JobMarketAccess.All));

        Assert.NotEqual(first.Select(x => x.OfferId), second.Select(x => x.OfferId));
    }

    [Fact]
    public void LevelOneShowsOneOrTwoJobsAndMegaHubCanGrowToSixty()
    {
        var megaHub = AirportMarketCapacity.ForScale(AirportMarketScale.MegaHub);
        var starting = JobMarketGenerator.Generate(Request(
            InitialAirportProfiles.GriffissInternational,
            Epoch,
            JobMarketAccess.All,
            capacity: megaHub,
            standing: CareerLevelSnapshot.Starting));
        Assert.InRange(starting.Count, 1, 2);

        var maxStanding = new CareerLevelSnapshot(50, 500_000, long.MaxValue);
        var mature = JobMarketGenerator.Generate(Request(
            InitialAirportProfiles.GriffissInternational,
            Epoch,
            JobMarketAccess.All,
            capacity: megaHub,
            standing: maxStanding));
        Assert.Equal(60, mature.Count);

        var small = JobMarketGenerator.Generate(Request(
            InitialAirportProfiles.GriffissInternational,
            Epoch,
            JobMarketAccess.All,
            capacity: AirportMarketCapacity.ForScale(AirportMarketScale.Small),
            standing: maxStanding));
        Assert.Equal(16, small.Count);
    }

    [Fact]
    public void LockedDreamJobsAreLimitedAndNeverGrantAccess()
    {
        var standing = new CareerLevelSnapshot(30, 50_000, 60_000);
        var visible = JobMarketGenerator.Generate(Request(
            InitialAirportProfiles.GriffissInternational,
            Epoch,
            JobMarketAccess.CivilianEmployment,
            capacity: AirportMarketCapacity.ForScale(AirportMarketScale.MajorHub),
            standing: standing));
        var locked = visible.Where(x => x.IsLockedPreview).ToArray();
        Assert.InRange(locked.Length, 1, 2);
        Assert.All(visible.Where(x => !x.IsLockedPreview), x => Assert.Equal(ServiceTrack.CivilianEmployment, x.ServiceTrack));

        var hiddenPolicy = JobMarketPolicy.Default with { ShowLockedPreviews = false };
        var hidden = JobMarketGenerator.Generate(Request(
            InitialAirportProfiles.GriffissInternational,
            Epoch,
            JobMarketAccess.CivilianEmployment,
            hiddenPolicy,
            AirportMarketCapacity.ForScale(AirportMarketScale.MajorHub),
            standing));
        Assert.NotEmpty(hidden);
        Assert.All(hidden, x =>
        {
            Assert.False(x.IsLockedPreview);
            Assert.Equal(ServiceTrack.CivilianEmployment, x.ServiceTrack);
        });
    }

    [Fact]
    public void CareerBoardPrefersShortSessionsAndDoesNotRequireMarathonJobs()
    {
        var policy = JobMarketPolicy.Default;
        var inside = policy.DurationSuitability(ContractKind.Cargo, 3, 1);
        var oneHour = policy.DurationSuitability(ContractKind.Cargo, 1, 1);
        var shortFlight = policy.DurationSuitability(ContractKind.Cargo, 0.5, 1);
        var longFlight = policy.DurationSuitability(ContractKind.Cargo, 7, 1);
        var experiencedLongFlight = policy.DurationSuitability(ContractKind.Cargo, 7, 20);
        var marathonFlight = policy.DurationSuitability(ContractKind.Cargo, 15, 50);

        Assert.Equal(1.35, oneHour);
        Assert.True(inside > shortFlight);
        Assert.Equal(0, longFlight);
        Assert.Equal(0, experiencedLongFlight);
        Assert.Equal(0, marathonFlight);
        Assert.False(CareerSessionPolicy.FitsJobDuration(TimeSpan.FromHours(15)));

        // Long ferry/reposition work may still exist as optional special work.
        Assert.True(policy.DurationSuitability(ContractKind.Ferry, 7, 1) >= 0.60);
        Assert.True(policy.DurationSuitability(ContractKind.Ferry, 15, 50) > 0);
    }

    [Fact]
    public void GriffissProducesFarMoreMilitaryWorkThanLowDefenseMixedAirport()
    {
        var lowDefense = new AirportCareerProfile(
            "KTST",
            "Low Defense Mixed Airport",
            AirportOpportunity.Civilian | AirportOpportunity.Government | AirportOpportunity.Military,
            CivilianDemand: 0.82,
            GovernmentDemand: 0.15,
            MilitaryDemand: 0.05,
            UasResearchDemand: 0,
            MonthlyStorageCostIndex: 1,
            StorageScarcity: 0.30);

        var krme = GenerateMany(InitialAirportProfiles.GriffissInternational, JobMarketAccess.All, JobMarketPolicy.Default, 500).ToArray();
        var ordinary = GenerateMany(lowDefense, JobMarketAccess.All, JobMarketPolicy.Default, 500).ToArray();
        var krmeShare = Share(krme, ServiceTrack.MilitaryService);
        var ordinaryShare = Share(ordinary, ServiceTrack.MilitaryService);

        Assert.True(krmeShare > ordinaryShare * 3,
            $"Expected KRME military share to be substantially higher; KRME={krmeShare:P1}, comparison={ordinaryShare:P1}.");
        Assert.Contains(krme, x => x.ServiceTrack == ServiceTrack.CivilianEmployment);
        Assert.Contains(krme, x => x.ServiceTrack == ServiceTrack.GovernmentContract);
    }

    [Fact]
    public void EstablishedRelationshipRouteWinsMoreEqualDistanceSelections()
    {
        var policy = JobMarketPolicy.Default with
        {
            MinimumOffers = 1,
            MaximumOffers = 1,
            ShowLockedPreviews = false
        };
        var destinations = new[]
        {
            new JobMarketDestination("KAAA", 180, RouteStrength: 1, RelationshipStrength: 1, EstimatedFlightHours: 2.5),
            new JobMarketDestination("KBBB", 180, EstimatedFlightHours: 2.5)
        };
        var familiar = 0;
        var unfamiliar = 0;
        for (var i = 0; i < 500; i++)
        {
            var time = Epoch.AddTicks(policy.RefreshInterval.Ticks * i);
            var request = new JobMarketGenerationRequest(
                777,
                time,
                InitialAirportProfiles.GriffissInternational,
                destinations,
                JobMarketAccess.CivilianEmployment,
                policy);
            var offer = Assert.Single(JobMarketGenerator.Generate(request));
            if (offer.DestinationIcao == "KAAA") familiar++;
            if (offer.DestinationIcao == "KBBB") unfamiliar++;
        }

        Assert.True(familiar > unfamiliar * 1.5,
            $"Expected established route to win clearly; familiar={familiar}, unfamiliar={unfamiliar}.");
    }

    [Fact]
    public void UasHeavyAirportGeneratesMoreResearchStyleWork()
    {
        var highUas = InitialAirportProfiles.GriffissInternational;
        var noUas = highUas with
        {
            Icao = "KLOW",
            Opportunities = highUas.Opportunities & ~AirportOpportunity.UasResearch,
            UasResearchDemand = 0
        };

        var high = GenerateMany(highUas, JobMarketAccess.All, JobMarketPolicy.Default, 500).Count(IsResearchStyle);
        var low = GenerateMany(noUas, JobMarketAccess.All, JobMarketPolicy.Default, 500).Count(IsResearchStyle);

        Assert.True(high > low * 1.15, $"Expected UAS demand to raise research-style jobs; high={high}, low={low}.");
    }

    [Fact]
    public void InvalidPolicyAndDestinationInputsFailFast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (JobMarketPolicy.Default with { DistanceDecayNm = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (JobMarketPolicy.Default with { CivilianEmploymentShare = 0.5 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JobMarketDestination("KSYR", -1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new JobMarketDestination("KSYR", 10, EstimatedFlightHours: double.NaN).Validate());
        Assert.Throws<ArgumentException>(() =>
            new JobMarketDestination("BAD", 10).Validate());
    }

    private static JobMarketGenerationRequest Request(
        AirportCareerProfile airport,
        DateTimeOffset time,
        JobMarketAccess access,
        JobMarketPolicy? policy = null,
        AirportMarketCapacity? capacity = null,
        CareerLevelSnapshot? standing = null) =>
        new(123456789, time, airport, Destinations, access, policy, standing, capacity);

    private static IEnumerable<JobMarketOfferDraft> GenerateMany(
        AirportCareerProfile airport,
        JobMarketAccess access,
        JobMarketPolicy policy,
        int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            var time = Epoch.AddTicks(policy.RefreshInterval.Ticks * i);
            foreach (var offer in JobMarketGenerator.Generate(Request(airport, time, access, policy)))
                yield return offer;
        }
    }

    private static double Share(IReadOnlyCollection<JobMarketOfferDraft> offers, ServiceTrack track) =>
        offers.Count == 0 ? 0 : offers.Count(x => x.ServiceTrack == track) / (double)offers.Count;

    private static bool IsResearchStyle(JobMarketOfferDraft offer) => offer.Kind is
        ContractKind.Survey or ContractKind.Photography or ContractKind.MilitarySurveillance;
}
