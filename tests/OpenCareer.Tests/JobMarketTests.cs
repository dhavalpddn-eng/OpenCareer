using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public class JobMarketTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JobMarketDestination[] Destinations =
    [
        new("KSYR", 45, 0.80, 0.60),
        new("KALB", 80, 0.45, 0.30),
        new("KBUF", 150, 0.10, 0.05),
        new("KBOS", 220),
        new("KPHL", 260),
        new("KIAD", 300),
        new("KORD", 550),
        new("KATL", 780)
    ];

    [Fact]
    public void SameSeedAirportAndCycleProduceSameBoard()
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
            Assert.Equal(offer.OfferedAt + JobMarketPolicy.Default.RefreshInterval, offer.ExpiresAt);
        });
    }

    [Fact]
    public void NewMarketCycleChangesDeterministicBoard()
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
    public void LockedPreviewPolicyNeverOverridesAccess()
    {
        var visible = GenerateMany(
            InitialAirportProfiles.GriffissInternational,
            JobMarketAccess.CivilianEmployment,
            JobMarketPolicy.Default,
            80).ToArray();
        Assert.Contains(visible, x => x.IsLockedPreview);
        Assert.All(visible.Where(x => !x.IsLockedPreview), x => Assert.Equal(ServiceTrack.CivilianEmployment, x.ServiceTrack));

        var hiddenPolicy = JobMarketPolicy.Default with { ShowLockedPreviews = false };
        var hidden = GenerateMany(
            InitialAirportProfiles.GriffissInternational,
            JobMarketAccess.CivilianEmployment,
            hiddenPolicy,
            40).ToArray();
        Assert.NotEmpty(hidden);
        Assert.All(hidden, x =>
        {
            Assert.False(x.IsLockedPreview);
            Assert.Equal(ServiceTrack.CivilianEmployment, x.ServiceTrack);
        });
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

        var krme = GenerateMany(InitialAirportProfiles.GriffissInternational, JobMarketAccess.All, JobMarketPolicy.Default, 300).ToArray();
        var ordinary = GenerateMany(lowDefense, JobMarketAccess.All, JobMarketPolicy.Default, 300).ToArray();
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
            new JobMarketDestination("KAAA", 180, RouteStrength: 1, RelationshipStrength: 1),
            new JobMarketDestination("KBBB", 180)
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

        var high = GenerateMany(highUas, JobMarketAccess.All, JobMarketPolicy.Default, 300).Count(IsResearchStyle);
        var low = GenerateMany(noUas, JobMarketAccess.All, JobMarketPolicy.Default, 300).Count(IsResearchStyle);

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
        Assert.Throws<ArgumentException>(() =>
            new JobMarketDestination("BAD", 10).Validate());
    }

    private static JobMarketGenerationRequest Request(
        AirportCareerProfile airport,
        DateTimeOffset time,
        JobMarketAccess access,
        JobMarketPolicy? policy = null) =>
        new(123456789, time, airport, Destinations, access, policy);

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
