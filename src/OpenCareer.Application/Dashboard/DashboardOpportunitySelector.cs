namespace OpenCareer.Application.Dashboard;

public static class DashboardOpportunitySelector
{
    public const int DefaultCount = 4;

    public static IReadOnlyList<DashboardOpportunity> SelectTopAvailable(
        IEnumerable<DashboardOpportunity> opportunities,
        int count = DefaultCount)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        return opportunities
            .Where(static opportunity => opportunity.IsAvailable)
            .OrderByDescending(static opportunity => opportunity.Tier)
            .ThenByDescending(static opportunity => ClampFit(opportunity.FitScore))
            .ThenByDescending(static opportunity => opportunity.EstimatedNetPay ?? decimal.MinValue)
            .ThenBy(static opportunity => opportunity.RepositionDistanceNauticalMiles ?? double.MaxValue)
            .ThenBy(static opportunity => opportunity.Id, StringComparer.Ordinal)
            .Take(count)
            .ToArray();
    }

    private static double ClampFit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}
