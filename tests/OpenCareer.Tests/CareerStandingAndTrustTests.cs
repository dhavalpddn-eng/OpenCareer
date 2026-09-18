using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public class CareerStandingAndTrustTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CareerLevelIsDerivedFromVerifiedEvidenceAndStartsAtOne()
    {
        var policy = CareerLevelPolicy.Default;
        var start = policy.Evaluate(new CareerProgressEvidence(0, 0, 0, 0, 0));
        Assert.Equal(1, start.Level);
        Assert.Equal(0, start.MeritPoints);

        var experienced = policy.Evaluate(new CareerProgressEvidence(
            VerifiedFlightHours: 60,
            CompletedContracts: 30,
            EstablishedRouteMilestones: 2,
            EmployerTrustMilestones: 1,
            EarnedQualifications: 2));
        Assert.InRange(experienced.Level, 10, 15);
        Assert.True(experienced.NextLevelAt > experienced.MeritPoints);
    }

    [Fact]
    public void CareerLevelCapsWithoutReplacingAuthoritativeProgression()
    {
        var level = CareerLevelPolicy.Default.Evaluate(new CareerProgressEvidence(
            VerifiedFlightHours: 10_000,
            CompletedContracts: 10_000,
            EstablishedRouteMilestones: 1_000,
            EmployerTrustMilestones: 1_000,
            EarnedQualifications: 100));
        Assert.Equal(50, level.Level);
        Assert.Equal(long.MaxValue, level.NextLevelAt);
    }

    [Fact]
    public void RouteRequiresRepeatedSuccessToBecomeEstablishedAndPreferred()
    {
        var route = new RouteExperience("KRME", "KALB", 0, 0, 0);
        Assert.Equal(RouteFamiliarity.Untried, route.Familiarity);

        route = route.RecordSuccess(Epoch.AddHours(1), true);
        Assert.Equal(RouteFamiliarity.Discovered, route.Familiarity);
        route = route.RecordSuccess(Epoch.AddHours(2), true);
        Assert.Equal(RouteFamiliarity.Familiar, route.Familiarity);

        for (var i = 3; i <= 5; i++)
            route = route.RecordSuccess(Epoch.AddHours(i), true);
        Assert.Equal(RouteFamiliarity.Established, route.Familiarity);
        Assert.Equal(0.50, route.Strength);

        for (var i = 6; i <= 12; i++)
            route = route.RecordSuccess(Epoch.AddHours(i), i % 2 == 0);
        Assert.Equal(RouteFamiliarity.Preferred, route.Familiarity);
        Assert.True(route.OnTimeRate < 1);
    }

    [Fact]
    public void EmployerTrustUnlocksRelationshipBenefitsButFailuresMatter()
    {
        var employerId = Guid.NewGuid();
        var trust = EmployerTrustState.Start(employerId, Epoch);
        for (var i = 1; i <= 8; i++)
            trust = trust.RecordSuccess(Epoch.AddHours(i), onTime: true, safeOperation: true);

        Assert.Equal(EmployerTrustTier.Trusted, trust.Tier);
        var majorAirline = new EmployerOperatingProfile(
            employerId,
            "Real Airline Fixture",
            EmployerType.Airline,
            EmployerScale.MajorAirline,
            IsRealWorldCompany: true,
            DutyDeadheadProgramAvailable: true);
        Assert.True(trust.CanReceiveEmployerDeadhead(majorAirline));

        trust = trust.RecordFailure(Epoch.AddHours(9));
        Assert.Equal(1, trust.FailedJobs);
        Assert.True(trust.TrustScore < 48);
    }

    [Fact]
    public void SmallAndMegaHubCapacityPresetsMatchVisibilityDesign()
    {
        Assert.Equal(10, AirportMarketCapacity.ForScale(AirportMarketScale.Local).MatureVisibleOfferCapacity);
        Assert.Equal(16, AirportMarketCapacity.ForScale(AirportMarketScale.Small).MatureVisibleOfferCapacity);
        Assert.Equal(20, AirportMarketCapacity.ForScale(AirportMarketScale.Regional).MatureVisibleOfferCapacity);
        Assert.Equal(60, AirportMarketCapacity.ForScale(AirportMarketScale.MegaHub).MatureVisibleOfferCapacity);
    }
}
