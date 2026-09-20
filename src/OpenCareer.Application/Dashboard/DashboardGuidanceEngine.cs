namespace OpenCareer.Application.Dashboard;

public sealed class DashboardGuidanceEngine
{
    public IReadOnlyList<DashboardGuidanceCandidate> ComposeSnapshotCandidates(
        DashboardSnapshot snapshot,
        bool hasEligibleOpportunities)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var candidates = new List<DashboardGuidanceCandidate>(snapshot.Guidance);

        if (snapshot.ActiveOperation is { } operation &&
            operation.NextActionTarget != DashboardActionTarget.None)
        {
            candidates.Add(new(
                "active-operation",
                operation.IsBlocked
                    ? DashboardGuidancePriority.Critical
                    : DashboardGuidancePriority.Important,
                5,
                operation.NextActionTarget,
                operation.NextActionTitle,
                operation.BlockingReason ?? operation.Detail));
        }

        if (snapshot.Aircraft?.ReadyForWork == false)
        {
            candidates.Add(new(
                "aircraft-not-ready",
                DashboardGuidancePriority.Critical,
                0,
                DashboardActionTarget.Maintenance,
                "Aircraft needs attention",
                snapshot.Aircraft.BlockingReason ??
                "The selected aircraft is not ready for work."));
        }

        if (snapshot.Employment is { } employment)
        {
            switch (employment.Status)
            {
                case EmploymentStatus.Probation:
                case EmploymentStatus.Suspended:
                    candidates.Add(new(
                        "company-standing",
                        DashboardGuidancePriority.Important,
                        0,
                        DashboardActionTarget.Company,
                        "Company standing needs attention",
                        employment.StatusMessage ??
                        "Review your company standing before taking more company work."));
                    break;

                case EmploymentStatus.Terminated:
                    candidates.Add(new(
                        "company-terminated",
                        DashboardGuidancePriority.Important,
                        0,
                        DashboardActionTarget.Company,
                        "Employment ended",
                        employment.StatusMessage ??
                        "Review the company outcome and your available recovery paths."));
                    break;
            }
        }

        if (hasEligibleOpportunities)
        {
            candidates.Add(new(
                "top-jobs",
                DashboardGuidancePriority.Recommended,
                20,
                DashboardActionTarget.Jobs,
                "Review your best available jobs",
                "The Jobs board has ranked your four strongest eligible opportunities."));
        }

        return candidates;
    }

    public DashboardGuidanceCandidate? SelectPrimary(
        IEnumerable<DashboardGuidanceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(static candidate => candidate.IsActionable)
            .OrderByDescending(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.OrderWithinPriority)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
