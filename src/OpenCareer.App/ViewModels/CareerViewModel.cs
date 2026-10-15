using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.App.ViewModels;

public sealed class CareerViewModel : INotifyPropertyChanged
{
    private readonly PlayerCareerOnboardingCoordinator _onboarding;
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly ICareerQualificationRoadmapSource _qualificationRoadmap;
    private readonly TimeProvider _timeProvider;

    private string _homeAirportIcao = string.Empty;
    private string _statusText =
        "Choose the airport where your civilian career will begin.";
    private string _locationText =
        "No career profile exists yet.";
    private bool _hasCareer;
    private bool _isCreating;
    private string _currentLicenseText = "No career profile exists yet.";
    private string _earnedRatingsText = "None";
    private string _nextQualificationsText = "Complete onboarding to view the qualification roadmap.";
    private string _qualificationPolicyText = "Qualifications are awarded only from authoritative completion evidence.";
    private IReadOnlyList<CareerQualificationItemViewModel> _qualificationRoadmapItems =
        Array.Empty<CareerQualificationItemViewModel>();

    public CareerViewModel(
        PlayerCareerOnboardingCoordinator onboarding,
        PlayerCareerRuntimeState runtime,
        TimeProvider timeProvider)
        : this(
            onboarding,
            runtime,
            timeProvider,
            new CareerQualificationRoadmapSource(runtime))
    {
    }

    public CareerViewModel(
        PlayerCareerOnboardingCoordinator onboarding,
        PlayerCareerRuntimeState runtime,
        TimeProvider timeProvider,
        ICareerQualificationRoadmapSource qualificationRoadmap)
    {
        _onboarding =
            onboarding
            ?? throw new ArgumentNullException(nameof(onboarding));
        _runtime =
            runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _timeProvider =
            timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _qualificationRoadmap = qualificationRoadmap
            ?? throw new ArgumentNullException(nameof(qualificationRoadmap));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string HomeAirportIcao
    {
        get => _homeAirportIcao;
        set
        {
            if (SetField(
                    ref _homeAirportIcao,
                    value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanStartCareer));
            }
        }
    }

    public string StatusText => _statusText;

    public string LocationText => _locationText;

    public bool HasCareer => _hasCareer;

    public bool IsCreating => _isCreating;

    public string CurrentLicenseText => _currentLicenseText;

    public string EarnedRatingsText => _earnedRatingsText;

    public string NextQualificationsText => _nextQualificationsText;

    public string QualificationPolicyText => _qualificationPolicyText;

    public IReadOnlyList<CareerQualificationItemViewModel> QualificationRoadmapItems =>
        _qualificationRoadmapItems;

    public bool CanStartCareer =>
        !_hasCareer
        && !_isCreating
        && IsValidAirportIcao(_homeAirportIcao);

    public bool CanEditHomeAirport =>
        !_hasCareer
        && !_isCreating;

    public string StartButtonText =>
        _isCreating
            ? "Starting Career…"
            : "Start Career";

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        PlayerCareerProfileStoreRecord? current =
            await _runtime
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);

        if (current is null)
        {
            SetCareerState(
                hasCareer:
                    false,
                status:
                    "Choose your starting airport where your civilian career will begin.",
                location:
                    "No career profile exists yet.");
            ApplyRoadmap(null);
            return;
        }

        ApplyCurrent(current);
        await RefreshRoadmapAsync(cancellationToken);
    }

    public async Task StartCareerAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isCreating)
            return;

        PlayerCareerProfileStoreRecord? current =
            _runtime.Current;

        if (current is not null)
        {
            ApplyCurrent(current);
            return;
        }

        string homeAirportIcao =
            _homeAirportIcao
                .Trim()
                .ToUpperInvariant();

        if (!IsValidAirportIcao(homeAirportIcao))
        {
            SetField(
                ref _statusText,
                "Enter a four-letter airport ICAO such as KDFW.",
                nameof(StatusText));
            return;
        }

        SetCreating(true);

        try
        {
            PlayerCareerProfileStoreRecord created =
                await _onboarding
                    .CreateAsync(
                        Guid.NewGuid(),
                        homeAirportIcao,
                        _timeProvider.GetUtcNow(),
                        cancellationToken)
                    .ConfigureAwait(true);

            ApplyCurrent(created);
            await RefreshRoadmapAsync(cancellationToken);

            SetField(
                ref _statusText,
                $"Career started at {created.Profile.Location.CurrentAirportIcao}. Open Jobs to load the local job board.",
                nameof(StatusText));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            SetField(
                ref _statusText,
                $"Career could not be started: {ex.Message}",
                nameof(StatusText));
        }
        catch (InvalidOperationException ex)
        {
            SetField(
                ref _statusText,
                $"Career could not be started: {ex.Message}",
                nameof(StatusText));
        }
        finally
        {
            SetCreating(false);
        }
    }

    private void ApplyCurrent(
        PlayerCareerProfileStoreRecord current)
    {
        string home =
            current.Profile.Location.HomeAirportIcao;
        string location =
            current.Profile.Location.CurrentAirportIcao;

        SetField(
            ref _homeAirportIcao,
            home,
            nameof(HomeAirportIcao));

        SetCareerState(
            hasCareer:
                true,
            status:
                "Career onboarding is complete.",
            location:
                $"HOME {home} · CURRENT {location}");
    }

    private async Task RefreshRoadmapAsync(
        CancellationToken cancellationToken)
    {
        CareerQualificationRoadmap? roadmap =
            await _qualificationRoadmap
                .ReadAsync(cancellationToken)
                .ConfigureAwait(true);

        ApplyRoadmap(roadmap);
    }

    private void ApplyRoadmap(
        CareerQualificationRoadmap? roadmap)
    {
        if (roadmap is null)
        {
            SetField(ref _currentLicenseText, "No career profile exists yet.", nameof(CurrentLicenseText));
            SetField(ref _earnedRatingsText, "None", nameof(EarnedRatingsText));
            SetField(ref _nextQualificationsText, "Complete onboarding to view the qualification roadmap.", nameof(NextQualificationsText));
            SetField(ref _qualificationPolicyText, "Qualifications are awarded only from authoritative completion evidence.", nameof(QualificationPolicyText));
            SetField(ref _qualificationRoadmapItems, Array.Empty<CareerQualificationItemViewModel>(), nameof(QualificationRoadmapItems));
            return;
        }

        string currentLicense = roadmap.EarnedQualifications.License switch
        {
            PilotLicenseLevel.None => "No civilian license",
            PilotLicenseLevel.Student => "Student pilot",
            PilotLicenseLevel.Private => "Private pilot",
            PilotLicenseLevel.Commercial => "Commercial pilot",
            PilotLicenseLevel.AirlineTransport => "Airline transport pilot",
            _ => throw new ArgumentOutOfRangeException(nameof(roadmap))
        };
        string[] earnedRatings = roadmap.Items
            .Where(static item =>
                item.Identity.Kind == CareerQualificationKind.Rating
                && item.Status == CareerQualificationRoadmapStatus.Earned)
            .Select(static item => item.Title)
            .ToArray();
        string[] available = roadmap.Items
            .Where(static item => item.Status == CareerQualificationRoadmapStatus.Available)
            .Select(static item => item.Title)
            .ToArray();
        CareerQualificationItemViewModel[] items = roadmap.Items
            .OrderBy(static item => item.Sequence)
            .Select(static item => new CareerQualificationItemViewModel(
                item.Title,
                item.Identity.Kind == CareerQualificationKind.License ? "LICENSE" : "RATING",
                item.Status.ToString().ToUpperInvariant(),
                item.Description,
                FormatPrerequisites(item.Prerequisites)))
            .ToArray();

        SetField(ref _currentLicenseText, currentLicense, nameof(CurrentLicenseText));
        SetField(ref _earnedRatingsText, earnedRatings.Length == 0 ? "None" : string.Join(" · ", earnedRatings), nameof(EarnedRatingsText));
        SetField(ref _nextQualificationsText, available.Length == 0 ? "No next qualification is currently available." : string.Join(" · ", available), nameof(NextQualificationsText));
        SetField(ref _qualificationPolicyText, roadmap.TrainingPolicyDetail, nameof(QualificationPolicyText));
        SetField(ref _qualificationRoadmapItems, items, nameof(QualificationRoadmapItems));
    }

    private static string FormatPrerequisites(
        IEnumerable<CareerQualificationPrerequisite> prerequisites)
    {
        CareerQualificationPrerequisite[] values = prerequisites.ToArray();
        if (values.Length == 0)
            return "Prerequisites: none";

        return "Prerequisites: " + string.Join(
            " · ",
            values.Select(static prerequisite =>
                $"{prerequisite.Title} ({(prerequisite.IsMet ? "met" : "unmet")})"));
    }

    private void SetCareerState(
        bool hasCareer,
        string status,
        string location)
    {
        if (_hasCareer != hasCareer)
        {
            _hasCareer = hasCareer;
            OnPropertyChanged(nameof(HasCareer));
        }

        SetField(
            ref _statusText,
            status,
            nameof(StatusText));

        SetField(
            ref _locationText,
            location,
            nameof(LocationText));

        OnPropertyChanged(nameof(CanStartCareer));
        OnPropertyChanged(nameof(CanEditHomeAirport));
    }

    private void SetCreating(bool value)
    {
        if (_isCreating == value)
            return;

        _isCreating = value;
        OnPropertyChanged(nameof(IsCreating));
        OnPropertyChanged(nameof(CanStartCareer));
        OnPropertyChanged(nameof(CanEditHomeAirport));
        OnPropertyChanged(nameof(StartButtonText));
    }

    private static bool IsValidAirportIcao(
        string value)
    {
        string normalized =
            value.Trim();

        return normalized.Length == 4
            && normalized.All(
                static c =>
                    c is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z');
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(
                field,
                value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed record CareerQualificationItemViewModel(
    string Title,
    string Kind,
    string Status,
    string Description,
    string Prerequisites);
