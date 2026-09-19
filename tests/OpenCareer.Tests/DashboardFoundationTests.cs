using OpenCareer.Application.Dashboard;

namespace OpenCareer.Tests;

public sealed class DashboardFoundationTests
{
    [Fact]
    public void TopOpportunitiesShowsFourHighestAvailableJobs()
    {
        DashboardOpportunity[] opportunities =
        [
            Opportunity("green", OpportunityTier.Standard, true, 100, 900),
            Opportunity("blue-a", OpportunityTier.Specialist, true, 70, 1500),
            Opportunity("blue-b", OpportunityTier.Specialist, true, 90, 1200),
            Opportunity("purple", OpportunityTier.Elite, true, 60, 2200),
            Opportunity("legendary", OpportunityTier.Legendary, true, 45, 3500),
            Opportunity("locked-legendary", OpportunityTier.Legendary, false, 100, 9000)
        ];

        IReadOnlyList<DashboardOpportunity> selected =
            DashboardOpportunitySelector.SelectTopAvailable(opportunities);

        Assert.Equal(4, selected.Count);
        Assert.Equal("legendary", selected[0].Id);
        Assert.Equal("purple", selected[1].Id);
        Assert.Equal("blue-b", selected[2].Id);
        Assert.Equal("blue-a", selected[3].Id);
        Assert.DoesNotContain(selected, static job => job.Id == "locked-legendary");
    }

    [Fact]
    public void EqualTierUsesFitBeforeMoney()
    {
        DashboardOpportunity[] opportunities =
        [
            Opportunity("better-fit", OpportunityTier.Specialist, true, 95, 1000),
            Opportunity("more-money", OpportunityTier.Specialist, true, 80, 5000)
        ];

        IReadOnlyList<DashboardOpportunity> selected =
            DashboardOpportunitySelector.SelectTopAvailable(opportunities, 1);

        Assert.Single(selected);
        Assert.Equal("better-fit", selected[0].Id);
    }

    [Fact]
    public void GuidancePrioritizesCriticalBlockerOverRoutineNextAction()
    {
        var engine = new DashboardGuidanceEngine();

        DashboardGuidanceCandidate? selected = engine.SelectPrimary(
        [
            new(
                "jobs",
                DashboardGuidancePriority.Recommended,
                0,
                DashboardActionTarget.Jobs,
                "Find work",
                "Browse opportunities."),
            new(
                "maintenance",
                DashboardGuidancePriority.Critical,
                0,
                DashboardActionTarget.Maintenance,
                "Aircraft not ready",
                "Required maintenance blocks the selected operation."),
            new(
                "company",
                DashboardGuidancePriority.Important,
                0,
                DashboardActionTarget.Company,
                "Company review",
                "Your employment standing requires attention.")
        ]);

        Assert.NotNull(selected);
        Assert.Equal(DashboardActionTarget.Maintenance, selected.Target);
    }

    [Fact]
    public void GuidanceSkipsNonActionableCandidate()
    {
        var engine = new DashboardGuidanceEngine();

        DashboardGuidanceCandidate? selected = engine.SelectPrimary(
        [
            new(
                "blocked-critical",
                DashboardGuidancePriority.Critical,
                0,
                DashboardActionTarget.Maintenance,
                "Blocked",
                "Not currently actionable.",
                IsActionable: false),
            new(
                "dispatch",
                DashboardGuidancePriority.Important,
                0,
                DashboardActionTarget.Dispatch,
                "Prepare accepted job",
                "Dispatch is ready.")
        ]);

        Assert.NotNull(selected);
        Assert.Equal(DashboardActionTarget.Dispatch, selected.Target);
    }

    private static DashboardOpportunity Opportunity(
        string id,
        OpportunityTier tier,
        bool available,
        double fit,
        decimal net) =>
        new(
            id,
            id,
            "KAAA",
            "KBBB",
            "Cargo",
            tier,
            available,
            fit,
            GrossPay: net + 100,
            EstimatedNetPay: net);
}
