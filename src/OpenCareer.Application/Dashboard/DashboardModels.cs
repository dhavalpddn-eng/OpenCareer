namespace OpenCareer.Application.Dashboard;

public enum OpportunityTier
{
    Standard = 1,
    Specialist = 2,
    Elite = 3,
    Legendary = 4
}

public enum EmploymentStatus
{
    NotEmployed,
    Active,
    Probation,
    Suspended,
    Terminated
}

public enum DashboardActionTarget
{
    None,
    Jobs,
    Dispatch,
    CurrentFlight,
    MapWorld,
    Aircraft,
    Maintenance,
    Company,
    Finances,
    Career,
    Logbook
}

public enum DashboardGuidancePriority
{
    Informational = 0,
    Recommended = 100,
    Important = 200,
    Critical = 300
}

public sealed record DashboardOpportunity(
    string Id,
    string Title,
    string Origin,
    string Destination,
    string JobFamily,
    OpportunityTier Tier,
    bool IsAvailable,
    double FitScore,
    decimal? GrossPay = null,
    decimal? EstimatedNetPay = null,
    TimeSpan? EstimatedDuration = null,
    double? RepositionDistanceNauticalMiles = null,
    string? AircraftRequirement = null,
    string? UnavailableReason = null);

public sealed record DashboardGuidanceCandidate(
    string Id,
    DashboardGuidancePriority Priority,
    int OrderWithinPriority,
    DashboardActionTarget Target,
    string Title,
    string Detail,
    bool IsActionable = true);

public sealed record DashboardCareerSummary(
    int? Level,
    long? CurrentXp,
    long? XpForNextLevel,
    string LicenseSummary,
    double? TotalFlightHours,
    int? AircraftOwned,
    string? NextMilestone,
    string? RecentAchievement);

public sealed record DashboardEmploymentSummary(
    EmploymentStatus Status,
    string? CompanyName,
    string? Rank,
    double? StandingPercent,
    string? StatusMessage);

public sealed record DashboardFinanceSummary(
    decimal? Cash,
    decimal? TodayNet,
    decimal? UpcomingObligations);

public sealed record DashboardAircraftSummary(
    string? AircraftName,
    string? AccessType,
    bool? ReadyForWork,
    string? AircraftLocation,
    string? PlayerLocation,
    double? DistanceToPlayerNauticalMiles,
    string? BlockingReason);

public sealed record DashboardRecentActivity(
    DateTimeOffset Timestamp,
    string Category,
    string Text,
    DashboardActionTarget Target = DashboardActionTarget.None);

public sealed record DashboardSocialPost(
    string Id,
    DateTimeOffset Timestamp,
    string Author,
    string AuthorType,
    string Area,
    string Text,
    string? Company = null,
    string? Airport = null,
    string? RelatedJobId = null);

public sealed record DashboardWorldSummary(
    string? PlayerLocation,
    string? HomeBase,
    int NearbyOpportunityCount,
    int ActiveWorldEventCount,
    int ActiveMarketSignalCount,
    int ActiveGovernmentSignalCount);

public sealed record DashboardSnapshot(
    DashboardCareerSummary? Career,
    DashboardEmploymentSummary? Employment,
    DashboardFinanceSummary? Finances,
    DashboardAircraftSummary? Aircraft,
    DashboardWorldSummary? World,
    IReadOnlyList<DashboardOpportunity> Opportunities,
    IReadOnlyList<DashboardRecentActivity> RecentActivity,
    IReadOnlyList<DashboardSocialPost> SocialFeed)
{
    public static DashboardSnapshot Empty { get; } =
        new(
            null,
            null,
            null,
            null,
            null,
            Array.Empty<DashboardOpportunity>(),
            Array.Empty<DashboardRecentActivity>(),
            Array.Empty<DashboardSocialPost>());
}
