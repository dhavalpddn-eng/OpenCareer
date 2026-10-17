using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Tests;

public sealed class JobMarketPolicyTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 12, 25, 0, TimeSpan.Zero);

    [Fact]
    public void AccessFlagsMapOnlyToMatchingServiceTracks()
    {
        JobMarketAccess access =
            JobMarketAccess.CivilianEmployment
            | JobMarketAccess.GovernmentContract;

        Assert.True(
            access.Allows(
                ServiceTrack.CivilianEmployment));
        Assert.True(
            access.Allows(
                ServiceTrack.GovernmentContract));
        Assert.False(
            access.Allows(
                ServiceTrack.IndependentContract));
        Assert.False(
            access.Allows(
                ServiceTrack.MilitaryService));
    }

    [Fact]
    public void CapacityProfilesHaveStableMatureBoardSizes()
    {
        AirportMarketCapacity local =
            AirportMarketCapacity.ForScale(
                AirportMarketScale.Local);
        AirportMarketCapacity mega =
            AirportMarketCapacity.ForScale(
                AirportMarketScale.MegaHub);

        local.Validate();
        mega.Validate();

        Assert.Equal(
            10,
            local.MatureVisibleOfferCapacity);
        Assert.Equal(
            60,
            mega.MatureVisibleOfferCapacity);
        Assert.True(
            mega.PassengerActivityIndex
            > local.PassengerActivityIndex);
        Assert.True(
            mega.CargoActivityIndex
            > local.CargoActivityIndex);
    }

    [Fact]
    public void BoardSizeStartsSmallAndCanGrowToAirportCapacity()
    {
        JobMarketPolicy policy =
            JobMarketPolicy.Default;
        AirportMarketCapacity capacity =
            AirportMarketCapacity.ForScale(
                AirportMarketScale.MegaHub);

        int starting =
            policy.VisibleOfferCount(
                capacity,
                careerLevel: 1,
                new DeterministicRandom(123));

        int mature =
            policy.VisibleOfferCount(
                capacity,
                policy.CareerLevelCap,
                new DeterministicRandom(123));

        Assert.InRange(
            starting,
            policy.MinimumOffers,
            policy.MaximumOffers);
        Assert.Equal(
            capacity.MatureVisibleOfferCapacity,
            mature);
    }

    [Fact]
    public void EarlyCareerDurationPolicyPrefersTwoToFourHours()
    {
        JobMarketPolicy policy =
            JobMarketPolicy.Default;

        double inside =
            policy.DurationSuitability(
                ContractKind.Cargo,
                3,
                1);
        double shortFlight =
            policy.DurationSuitability(
                ContractKind.Cargo,
                0.5,
                1);
        double longFlight =
            policy.DurationSuitability(
                ContractKind.Cargo,
                7,
                1);
        double experiencedLongFlight =
            policy.DurationSuitability(
                ContractKind.Cargo,
                7,
                20);

        Assert.True(inside > shortFlight);
        Assert.True(inside > longFlight);
        Assert.Equal(1, experiencedLongFlight);
        Assert.True(
            policy.DurationSuitability(
                ContractKind.Ferry,
                7,
                1)
            >= 0.60);
    }

    [Fact]
    public void UrgentAndOrganOffersUseBoundedShorterLifetimes()
    {
        JobMarketPolicy policy =
            JobMarketPolicy.Default;

        TimeSpan express =
            policy.OfferLifetime(
                ContractKind.ExpressCargo,
                new DeterministicRandom(456));

        TimeSpan organ =
            policy.OfferLifetime(
                ContractKind.Medical,
                JobScenarioKind.OrganTransport,
                new DeterministicRandom(456));

        Assert.InRange(
            express,
            TimeSpan.FromMinutes(45),
            TimeSpan.FromHours(5));
        Assert.InRange(
            organ,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void AirportDemandWeightsTracksWithoutGrantingAccess()
    {
        JobMarketPolicy policy =
            JobMarketPolicy.Default;
        AirportCareerProfile airport =
            InitialAirportProfiles.GriffissInternational;

        double civilian =
            policy.TrackWeight(
                airport,
                ServiceTrack.CivilianEmployment);
        double government =
            policy.TrackWeight(
                airport,
                ServiceTrack.GovernmentContract);
        double military =
            policy.TrackWeight(
                airport,
                ServiceTrack.MilitaryService);

        Assert.True(civilian > 0);
        Assert.True(government > civilian);
        Assert.True(military > civilian);
    }

    [Fact]
    public void RefreshCycleUsesDeterministicUtcBuckets()
    {
        JobMarketPolicy policy =
            JobMarketPolicy.Default;

        DateTimeOffset start =
            policy.CycleStart(Epoch);
        DateTimeOffset sameBucket =
            policy.CycleStart(
                Epoch.AddMinutes(20));
        DateTimeOffset nextBucket =
            policy.CycleStart(
                Epoch.AddHours(1));

        Assert.Equal(start, sameBucket);
        Assert.Equal(
            policy.RefreshInterval,
            nextBucket - start);
        Assert.Equal(
            TimeSpan.Zero,
            start.Offset);
    }

    [Fact]
    public void InvalidPolicyAndCapacityFailFast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                (JobMarketPolicy.Default with
                {
                    DistanceDecayNm = double.NaN
                }).Validate());

        Assert.Throws<ArgumentException>(
            () =>
                (JobMarketPolicy.Default with
                {
                    CivilianEmploymentShare = 0.50
                }).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new AirportMarketCapacity(
                    AirportMarketScale.Regional,
                    20,
                    0.5,
                    1.1,
                    0.5)
                    .Validate());
    }
}
