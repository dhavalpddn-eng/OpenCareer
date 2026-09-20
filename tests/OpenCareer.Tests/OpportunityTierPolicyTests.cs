using OpenCareer.Application.Dashboard;

namespace OpenCareer.Tests;

public sealed class OpportunityTierPolicyTests
{
    [Fact]
    public void RoutineWorkIsStandard()
    {
        OpportunityTier tier = OpportunityTierPolicy.Default.Classify(
            new(20, 25, 25, 20, 20, 20));

        Assert.Equal(OpportunityTier.Standard, tier);
    }

    [Fact]
    public void SkilledWorkCanBeSpecialist()
    {
        OpportunityTier tier = OpportunityTierPolicy.Default.Classify(
            new(55, 65, 60, 50, 55, 55));

        Assert.Equal(OpportunityTier.Specialist, tier);
    }

    [Fact]
    public void DemandingWorkCanBeEliteWithoutBeingLegendary()
    {
        OpportunityTier tier = OpportunityTierPolicy.Default.Classify(
            new(80, 80, 80, 80, 80, 80));

        Assert.Equal(OpportunityTier.Elite, tier);
    }

    [Fact]
    public void RareHighDemandWorkCanBeLegendary()
    {
        OpportunityTier tier = OpportunityTierPolicy.Default.Classify(
            new(95, 90, 95, 90, 85, 90));

        Assert.Equal(OpportunityTier.Legendary, tier);
    }

    [Fact]
    public void HighRewardAloneCannotCreateLegendaryWork()
    {
        OpportunityTier tier = OpportunityTierPolicy.Default.Classify(
            new(35, 45, 45, 40, 45, 100));

        Assert.NotEqual(OpportunityTier.Legendary, tier);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void InvalidSignalsAreRejected(double invalid)
    {
        var signals = new OpportunityTierSignals(
            invalid,
            50,
            50,
            50,
            50,
            50);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => OpportunityTierPolicy.Default.Classify(signals));
    }
}
