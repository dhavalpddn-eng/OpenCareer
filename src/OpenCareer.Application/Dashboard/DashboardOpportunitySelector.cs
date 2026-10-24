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
            .Select(static (opportunity, index) => new
            {
                opportunity,
                index,
                ranked = opportunity.Tier.HasValue
                    && opportunity.FitScore.HasValue
            })
            .OrderByDescending(static item => item.ranked)
            .ThenByDescending(static item =>
                item.ranked ? item.opportunity.Tier : null)
            .ThenByDescending(static item =>
                item.ranked
                    ? ClampFit(item.opportunity.FitScore)
                    : double.MinValue)
            .ThenByDescending(static item =>
                item.ranked
                    ? item.opportunity.EstimatedNetPay ?? decimal.MinValue
                    : decimal.MinValue)
            .ThenBy(static item =>
                item.ranked
                    ? item.opportunity.RepositionDistanceNauticalMiles
                        ?? double.MaxValue
                    : double.MaxValue)
            .ThenBy(static item =>
                item.ranked ? item.opportunity.Id : string.Empty,
                StringComparer.Ordinal)
            .ThenBy(static item => item.index)
            .Take(count)
            .Select(static item => item.opportunity)
            .ToArray();
    }

    private static double ClampFit(double? value) =>
        value is { } known && double.IsFinite(known)
            ? Math.Clamp(known, 0, 100)
            : double.MinValue;
}
