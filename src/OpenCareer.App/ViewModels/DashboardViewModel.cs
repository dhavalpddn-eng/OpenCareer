using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Dashboard;

namespace OpenCareer.App.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly ShellViewModel _shell;
    private readonly IDashboardSnapshotSource _snapshotSource;
    private readonly DashboardGuidanceEngine _guidanceEngine;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private DashboardSnapshot _snapshot = DashboardSnapshot.Empty;
    private IReadOnlyList<DashboardOpportunityItemViewModel> _topOpportunities =
        Array.Empty<DashboardOpportunityItemViewModel>();
    private IReadOnlyList<DashboardActivityItemViewModel> _recentActivity =
        Array.Empty<DashboardActivityItemViewModel>();
    private IReadOnlyList<DashboardSocialItemViewModel> _socialFeed =
        Array.Empty<DashboardSocialItemViewModel>();

    private string _socialSearch = string.Empty;
    private string _primaryActionTitle = "No urgent career action";
    private string _primaryActionDetail =
        "OpenCareer will surface the most important next step here as career systems come online.";
    private DashboardActionTarget _primaryActionTarget = DashboardActionTarget.None;

    public DashboardViewModel(
        ShellViewModel shell,
        IDashboardSnapshotSource snapshotSource,
        DashboardGuidanceEngine guidanceEngine)
    {
        _shell = shell;
        _snapshotSource = snapshotSource;
        _guidanceEngine = guidanceEngine;
        _shell.PropertyChanged += OnShellPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DashboardNavigationRequestedEventArgs>? NavigationRequested;

    public ShellViewModel Shell => _shell;

    public IReadOnlyList<DashboardOpportunityItemViewModel> TopOpportunities =>
        _topOpportunities;

    public IReadOnlyList<DashboardActivityItemViewModel> RecentActivity =>
        _recentActivity;

    public IReadOnlyList<DashboardSocialItemViewModel> SocialFeed =>
        _socialFeed;

    public string PrimaryActionTitle => _primaryActionTitle;
    public string PrimaryActionDetail => _primaryActionDetail;
    public bool HasPrimaryAction => _primaryActionTarget != DashboardActionTarget.None;

    public string OpportunityStatusText =>
        _topOpportunities.Count == 0
            ? "No eligible jobs are available from the Jobs system yet."
            : $"Showing the {_topOpportunities.Count} highest-ranked eligible opportunities.";

    public string SocialFeedStatusText =>
        _socialFeed.Count == 0
            ? "No OpenCareer Network posts are available for this search yet."
            : $"{_socialFeed.Count} OpenCareer Network post(s) match.";

    public string ActivityStatusText =>
        _recentActivity.Count == 0
            ? "No career activity has been recorded yet."
            : $"{_recentActivity.Count} recent career event(s).";

    public string CareerLevelText =>
        _snapshot.Career?.Level is int level ? $"Level {level}" : "Level —";

    public string CareerXpText =>
        _snapshot.Career is { CurrentXp: long current, XpForNextLevel: long next }
            ? $"{current:N0} / {next:N0} XP"
            : "XP —";

    public string LicenseText =>
        _snapshot.Career?.LicenseSummary ?? "Licenses / ratings not available yet";

    public string FlightHoursText =>
        _snapshot.Career?.TotalFlightHours is double hours
            ? $"{hours:0.0} total hours"
            : "Flight hours —";

    public string AircraftOwnedText =>
        _snapshot.Career?.AircraftOwned is int count
            ? $"{count} owned"
            : "Owned aircraft —";

    public string NextMilestoneText =>
        _snapshot.Career?.NextMilestone ?? "Next milestone not available yet";

    public string RecentAchievementText =>
        _snapshot.Career?.RecentAchievement ?? "No recent milestone yet";

    public string CompanyNameText =>
        _snapshot.Employment?.CompanyName ?? "No active employer";

    public string CompanyRankText =>
        _snapshot.Employment?.Rank ?? "Rank —";

    public string CompanyStandingText =>
        _snapshot.Employment?.StandingPercent is double standing
            ? $"{standing:0}% standing"
            : "Standing —";

    public string EmploymentStatusText =>
        _snapshot.Employment?.Status switch
        {
            EmploymentStatus.Active => "Active",
            EmploymentStatus.Probation => "Probation",
            EmploymentStatus.Suspended => "Suspended",
            EmploymentStatus.Terminated => "Employment ended",
            _ => "Independent / not employed"
        };

    public string EmploymentMessageText =>
        _snapshot.Employment?.StatusMessage ??
        "Company rank, standing, probation and termination status will appear here.";

    public bool HasEmployer =>
        _snapshot.Employment is { } employment &&
        employment.Status != EmploymentStatus.NotEmployed;

    public string CashText =>
        _snapshot.Finances?.Cash is decimal cash ? $"{cash:C0}" : "Cash —";

    public string TodayNetText =>
        _snapshot.Finances?.TodayNet is decimal net
            ? $"{net:+$#,##0;-$#,##0;$0} today"
            : "Today —";

    public string UpcomingObligationsText =>
        _snapshot.Finances?.UpcomingObligations is decimal obligations
            ? $"{obligations:C0} upcoming"
            : "Upcoming obligations —";

    public bool HasActiveOperation => _snapshot.ActiveOperation is not null;

    public string ActiveOperationTitleText =>
        _snapshot.ActiveOperation?.Title ?? "No active operation";

    public string ActiveOperationRouteText =>
        _snapshot.ActiveOperation is { } operation
            ? $"{operation.Origin} → {operation.Destination}"
            : "Accept a job to create an active operation.";

    public string ActiveOperationStageText =>
        _snapshot.ActiveOperation?.Stage switch
        {
            ActiveOperationStage.Accepted => "ACCEPTED",
            ActiveOperationStage.PreparationRequired => "PREPARATION REQUIRED",
            ActiveOperationStage.ReadyToStart => "READY TO START",
            ActiveOperationStage.InProgress => "IN PROGRESS",
            ActiveOperationStage.PostFlight => "POST-FLIGHT",
            ActiveOperationStage.AwaitingSettlement => "AWAITING SETTLEMENT",
            _ => "NO ACTIVE OPERATION"
        };

    public string ActiveOperationDetailText =>
        _snapshot.ActiveOperation?.BlockingReason ??
        _snapshot.ActiveOperation?.Detail ??
        "Your accepted job, checklist state and next required action will appear here.";

    public string ActiveOperationChecklistText =>
        _snapshot.ActiveOperation is null
            ? "Checklist —"
            : _snapshot.ActiveOperation.ChecklistRequired
                ? "Checklist required"
                : "No checklist requirement";

    public string ActiveOperationPayText =>
        _snapshot.ActiveOperation?.EstimatedNetPay is decimal net
            ? $"{net:C0} est. net"
            : _snapshot.ActiveOperation?.GrossPay is decimal gross
                ? $"{gross:C0} gross"
                : "Pay —";

    public string ActiveOperationActionText =>
        _snapshot.ActiveOperation?.NextActionTitle ?? "Open Jobs";

    public string AircraftNameText =>
        _snapshot.Aircraft?.AircraftName ?? "No career aircraft selected";

    public string AircraftAccessText =>
        _snapshot.Aircraft?.AccessType ?? "Access type —";

    public string AircraftReadinessText =>
        _snapshot.Aircraft?.ReadyForWork switch
        {
            true => "READY FOR WORK",
            false => "NOT READY FOR WORK",
            null => "READINESS UNKNOWN"
        };

    public string AircraftLocationText =>
        _snapshot.Aircraft?.AircraftLocation ?? "Aircraft location —";

    public string PlayerLocationText =>
        _snapshot.Aircraft?.PlayerLocation ??
        (_shell.HasTelemetry ? _shell.PositionSummary : "Player location —");

    public string DistanceToAircraftText =>
        _snapshot.Aircraft?.DistanceToPlayerNauticalMiles is double distance
            ? $"{distance:0.0} NM to aircraft"
            : "Distance to aircraft —";

    public string AircraftBlockingText =>
        _snapshot.Aircraft?.BlockingReason ??
        "Maintenance and dispatch blockers will appear here when available.";

    public string HomeBaseText =>
        _snapshot.World?.HomeBase ?? "Home base not configured";

    public string WorldSummaryText =>
        _snapshot.World is null
            ? "World layers are waiting for Map / World, Jobs and market systems."
            : $"{_snapshot.World.NearbyOpportunityCount} nearby jobs • " +
              $"{_snapshot.World.ActiveWorldEventCount} events • " +
              $"{_snapshot.World.ActiveMarketSignalCount} market signals • " +
              $"{_snapshot.World.ActiveGovernmentSignalCount} government signals";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        try
        {
            DashboardSnapshot snapshot =
                await _snapshotSource.GetAsync(cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            _snapshot = snapshot;

            _topOpportunities = DashboardOpportunitySelector
                .SelectTopAvailable(_snapshot.Opportunities)
                .Select(static opportunity => new DashboardOpportunityItemViewModel(opportunity))
                .ToArray();

            _recentActivity = _snapshot.RecentActivity
                .OrderByDescending(static item => item.Timestamp)
                .Take(6)
                .Select(static item => new DashboardActivityItemViewModel(item))
                .ToArray();

            ApplySocialFilter();
            RefreshGuidance();
            RaiseAll();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void SetSocialSearch(string value)
    {
        value ??= string.Empty;
        if (string.Equals(_socialSearch, value, StringComparison.Ordinal))
            return;

        _socialSearch = value;
        ApplySocialFilter();
        OnPropertyChanged(nameof(SocialFeed));
        OnPropertyChanged(nameof(SocialFeedStatusText));
    }

    public void RequestPrimaryAction()
    {
        if (_primaryActionTarget != DashboardActionTarget.None)
            RequestNavigation(_primaryActionTarget);
    }

    public void RequestActiveOperationAction()
    {
        DashboardActionTarget target =
            _snapshot.ActiveOperation?.NextActionTarget ?? DashboardActionTarget.Jobs;

        RequestNavigation(target);
    }

    public void RequestNavigation(DashboardActionTarget target)
    {
        if (target == DashboardActionTarget.None)
            return;

        NavigationRequested?.Invoke(
            this,
            new DashboardNavigationRequestedEventArgs(target));
    }

    private void RefreshGuidance()
    {
        var candidates = _guidanceEngine
            .ComposeSnapshotCandidates(_snapshot, _topOpportunities.Count > 0)
            .ToList();

        if (_shell.IsSimulatorConnected && _shell.HasTelemetry)
        {
            candidates.Add(new(
                "live-aircraft",
                DashboardGuidancePriority.Recommended,
                50,
                DashboardActionTarget.CurrentFlight,
                "Aircraft telemetry is live",
                "Open Current Flight for the live aircraft workspace."));
        }
        else if (_shell.IsSimulatorConnected)
        {
            candidates.Add(new(
                "waiting-telemetry",
                DashboardGuidancePriority.Informational,
                50,
                DashboardActionTarget.CurrentFlight,
                "Waiting for aircraft telemetry",
                "MSFS is connected, but no stable aircraft telemetry is available yet."));
        }

        DashboardGuidanceCandidate? primary =
            _guidanceEngine.SelectPrimary(candidates);

        if (primary is null)
        {
            _primaryActionTitle = "No urgent career action";
            _primaryActionDetail =
                _shell.IsSimulatorConnected
                    ? "OpenCareer is connected. Career systems will add prioritized actions here as they become authoritative."
                    : "MSFS can remain closed while you review career information. Start it when you are ready to fly.";
            _primaryActionTarget = DashboardActionTarget.None;
        }
        else
        {
            _primaryActionTitle = primary.Title;
            _primaryActionDetail = primary.Detail;
            _primaryActionTarget = primary.Target;
        }

        OnPropertyChanged(nameof(PrimaryActionTitle));
        OnPropertyChanged(nameof(PrimaryActionDetail));
        OnPropertyChanged(nameof(HasPrimaryAction));
    }

    private void ApplySocialFilter()
    {
        IEnumerable<DashboardSocialPost> posts = _snapshot.SocialFeed;

        if (!string.IsNullOrWhiteSpace(_socialSearch))
        {
            string query = _socialSearch.Trim();
            posts = posts.Where(post =>
                Contains(post.Area, query) ||
                Contains(post.Author, query) ||
                Contains(post.Company, query) ||
                Contains(post.Airport, query) ||
                Contains(post.Text, query));
        }

        _socialFeed = posts
            .OrderByDescending(static post => post.Timestamp)
            .Take(20)
            .Select(static post => new DashboardSocialItemViewModel(post))
            .ToArray();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsSimulatorConnected) or
            nameof(ShellViewModel.HasTelemetry))
        {
            RefreshGuidance();
        }

        if (e.PropertyName is nameof(ShellViewModel.PositionSummary) or
            nameof(ShellViewModel.HasTelemetry))
        {
            OnPropertyChanged(nameof(PlayerLocationText));
        }
    }

    private void RaiseAll()
    {
        string[] properties =
        [
            nameof(TopOpportunities),
            nameof(RecentActivity),
            nameof(SocialFeed),
            nameof(OpportunityStatusText),
            nameof(SocialFeedStatusText),
            nameof(ActivityStatusText),
            nameof(CareerLevelText),
            nameof(CareerXpText),
            nameof(LicenseText),
            nameof(FlightHoursText),
            nameof(AircraftOwnedText),
            nameof(NextMilestoneText),
            nameof(RecentAchievementText),
            nameof(CompanyNameText),
            nameof(CompanyRankText),
            nameof(CompanyStandingText),
            nameof(EmploymentStatusText),
            nameof(EmploymentMessageText),
            nameof(HasEmployer),
            nameof(CashText),
            nameof(TodayNetText),
            nameof(UpcomingObligationsText),
            nameof(HasActiveOperation),
            nameof(ActiveOperationTitleText),
            nameof(ActiveOperationRouteText),
            nameof(ActiveOperationStageText),
            nameof(ActiveOperationDetailText),
            nameof(ActiveOperationChecklistText),
            nameof(ActiveOperationPayText),
            nameof(ActiveOperationActionText),
            nameof(AircraftNameText),
            nameof(AircraftAccessText),
            nameof(AircraftReadinessText),
            nameof(AircraftLocationText),
            nameof(PlayerLocationText),
            nameof(DistanceToAircraftText),
            nameof(AircraftBlockingText),
            nameof(HomeBaseText),
            nameof(WorldSummaryText)
        ];

        foreach (string property in properties)
            OnPropertyChanged(property);
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DashboardNavigationRequestedEventArgs(
    DashboardActionTarget target) : EventArgs
{
    public DashboardActionTarget Target { get; } = target;
}

public sealed class DashboardOpportunityItemViewModel
{
    public DashboardOpportunityItemViewModel(DashboardOpportunity opportunity)
    {
        Tier = opportunity.Tier;
        TierText = opportunity.Tier.ToString().ToUpperInvariant();
        Title = opportunity.Title;
        Route = $"{opportunity.Origin} → {opportunity.Destination}";
        JobFamily = opportunity.JobFamily;
        Pay = opportunity.GrossPay is decimal pay ? $"{pay:C0}" : "Pay —";
        NetPay = opportunity.EstimatedNetPay is decimal net ? $"{net:C0} est. net" : "Net —";
        Duration = opportunity.EstimatedDuration is TimeSpan duration
            ? $"{duration.TotalHours:0.#} hr"
            : "Duration —";
        Reposition = opportunity.RepositionDistanceNauticalMiles is double distance
            ? $"{distance:0} NM reposition"
            : "No reposition estimate";
        Aircraft = opportunity.AircraftRequirement ?? "Aircraft requirement —";
        Fit = $"{Math.Clamp(opportunity.FitScore, 0, 100):0}% fit";
    }

    public OpportunityTier Tier { get; }
    public string TierText { get; }
    public string Title { get; }
    public string Route { get; }
    public string JobFamily { get; }
    public string Pay { get; }
    public string NetPay { get; }
    public string Duration { get; }
    public string Reposition { get; }
    public string Aircraft { get; }
    public string Fit { get; }
}

public sealed class DashboardActivityItemViewModel
{
    public DashboardActivityItemViewModel(DashboardRecentActivity item)
    {
        When = item.Timestamp.ToLocalTime().ToString("g");
        Category = item.Category.ToUpperInvariant();
        Text = item.Text;
        Target = item.Target;
    }

    public string When { get; }
    public string Category { get; }
    public string Text { get; }
    public DashboardActionTarget Target { get; }
}

public sealed class DashboardSocialItemViewModel
{
    public DashboardSocialItemViewModel(DashboardSocialPost post)
    {
        When = post.Timestamp.ToLocalTime().ToString("g");
        Author = post.Author;
        AuthorType = post.AuthorType;
        Area = post.Area;
        Text = post.Text;
    }

    public string When { get; }
    public string Author { get; }
    public string AuthorType { get; }
    public string Area { get; }
    public string Text { get; }
}
