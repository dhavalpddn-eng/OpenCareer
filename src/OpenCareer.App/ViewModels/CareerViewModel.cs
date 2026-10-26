using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.App.ViewModels;

public sealed class CareerViewModel : INotifyPropertyChanged
{
    private readonly PlayerCareerOnboardingCoordinator _onboarding;
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly ICareerQualificationRoadmapSource _qualificationRoadmap;
    private readonly IStudentTrainingOperationReadinessSource _studentTrainingReadiness;
    private readonly IStudentTrainingOperationStartAction _studentTrainingStart;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CareerViewModel>? _logger;

    private string _homeAirportIcao = string.Empty;
    private bool _careerAuthorityUnavailable;
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
    private StudentTrainingOperationReadiness _studentTraining =
        new(
            StudentTrainingOperationReadinessState.NoCareer,
            "Complete career onboarding before starting Student training.");
    private string _studentTrainingStatusText =
        "Complete career onboarding before starting Student training.";
    private string _studentTrainingRouteText =
        "LOCAL CIRCUIT · airport unavailable";
    private string _studentTrainingAircraftText =
        "Training aircraft: unavailable";
    private bool _isStartingStudentTraining;

    public CareerViewModel(
        PlayerCareerOnboardingCoordinator onboarding,
        PlayerCareerRuntimeState runtime,
        TimeProvider timeProvider)
        : this(
            onboarding,
            runtime,
            timeProvider,
            new CareerQualificationRoadmapSource(runtime),
            UnavailableStudentTrainingAccess.Instance,
            UnavailableStudentTrainingAccess.Instance)
    {
    }

    public CareerViewModel(
        PlayerCareerOnboardingCoordinator onboarding,
        PlayerCareerRuntimeState runtime,
        TimeProvider timeProvider,
        ICareerQualificationRoadmapSource qualificationRoadmap)
        : this(
            onboarding,
            runtime,
            timeProvider,
            qualificationRoadmap,
            UnavailableStudentTrainingAccess.Instance,
            UnavailableStudentTrainingAccess.Instance)
    {
    }

    public CareerViewModel(
        PlayerCareerOnboardingCoordinator onboarding,
        PlayerCareerRuntimeState runtime,
        TimeProvider timeProvider,
        ICareerQualificationRoadmapSource qualificationRoadmap,
        IStudentTrainingOperationReadinessSource studentTrainingReadiness,
        IStudentTrainingOperationStartAction studentTrainingStart,
        ILogger<CareerViewModel>? logger = null)
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
        _studentTrainingReadiness = studentTrainingReadiness
            ?? throw new ArgumentNullException(nameof(studentTrainingReadiness));
        _studentTrainingStart = studentTrainingStart
            ?? throw new ArgumentNullException(nameof(studentTrainingStart));
        _logger = logger;
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

    public string StudentTrainingStatusText => _studentTrainingStatusText;

    public string StudentTrainingRouteText => _studentTrainingRouteText;

    public string StudentTrainingAircraftText => _studentTrainingAircraftText;

    public bool IsStartingStudentTraining => _isStartingStudentTraining;

    public bool CanStartStudentTraining =>
        !_isStartingStudentTraining
        && _studentTraining.CanStart;

    public string StudentTrainingButtonText =>
        _isStartingStudentTraining
            ? "Starting Training…"
            : "Start Student Training";

    public bool CanStartCareer =>
        !_hasCareer
        && !_isCreating
        && !_careerAuthorityUnavailable
        && IsValidAirportIcao(_homeAirportIcao);

    public bool CanEditHomeAirport =>
        !_hasCareer
        && !_isCreating
        && !_careerAuthorityUnavailable;

    public string StartButtonText =>
        _isCreating
            ? "Starting Career…"
            : "Start Career";

    public void ReportInitializationFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _logger?.LogError(exception, "Career initialization failed.");
        SetCareerAuthorityUnavailable(true);

        PlayerCareerProfileStoreRecord? authoritative =
            _runtime.Current;

        if (authoritative is not null)
        {
            ApplyCurrent(authoritative);
            ApplyStudentTraining(
                new(
                    StudentTrainingOperationReadinessState.AuthorityUnavailable,
                    "Student training readiness could not be refreshed. Reopen Career before another training start. If this repeats, create a diagnostic bundle in Settings.",
                    AirportIcao: authoritative.Profile.Location.CurrentAirportIcao,
                    AircraftDisplayName: _studentTraining.AircraftDisplayName));
            SetField(
                ref _statusText,
                $"Career profile at {authoritative.Profile.Location.CurrentAirportIcao} is active, but its qualification or training details could not be refreshed. Reopen Career before continuing. If this repeats, create a diagnostic bundle in Settings.",
                nameof(StatusText));
            return;
        }

        SetCareerState(
            hasCareer: false,
            status: "Career profile authority could not be loaded. Reopen Career before starting or changing a profile. If this repeats, create a diagnostic bundle in Settings.",
            location: "Career profile authority unavailable.");
        ApplyRoadmap(null);
        ApplyStudentTraining(
            new(
                StudentTrainingOperationReadinessState.AuthorityUnavailable,
                "Student training is unavailable until Career profile authority loads successfully."));
    }

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
            ApplyStudentTraining(
                new(
                    StudentTrainingOperationReadinessState.NoCareer,
                    "Complete career onboarding before starting Student training."));
            SetCareerAuthorityUnavailable(false);
            return;
        }

        ApplyCurrent(current);
        await RefreshRoadmapAsync(cancellationToken);
        await RefreshStudentTrainingAsync(cancellationToken);
        SetCareerAuthorityUnavailable(false);
    }

    public async Task StartCareerAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isCreating)
            return;

        if (_careerAuthorityUnavailable)
        {
            SetField(
                ref _statusText,
                "Career profile authority is unavailable. Reopen Career before another start attempt. If this repeats, create a diagnostic bundle in Settings.",
                nameof(StatusText));
            return;
        }

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
            await RefreshStudentTrainingAsync(cancellationToken);

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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Career onboarding failed.");

            PlayerCareerProfileStoreRecord? authoritative =
                _runtime.Current;

            if (authoritative is not null)
            {
                ApplyCurrent(authoritative);
                SetField(
                    ref _statusText,
                    $"Career profile is saved at {authoritative.Profile.Location.CurrentAirportIcao}, but its qualification or training details could not finish loading. Reopen Career to refresh them. If this repeats, create a diagnostic bundle in Settings.",
                    nameof(StatusText));
                return;
            }

            SetField(
                ref _statusText,
                "Career did not start. Retry Start Career. If this repeats, create a diagnostic bundle in Settings.",
                nameof(StatusText));
        }
        finally
        {
            SetCreating(false);
        }
    }

    public async Task StartStudentTrainingAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isStartingStudentTraining
            || !_studentTraining.CanStart)
        {
            return;
        }

        SetStartingStudentTraining(true);
        try
        {
            await _studentTrainingStart
                .StartAsync(cancellationToken)
                .ConfigureAwait(true);

            await RefreshStudentTrainingAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Student training start failed.");

            try
            {
                await RefreshStudentTrainingAsync(cancellationToken);

                string recoveryDetail = _studentTraining.State switch
                {
                    StudentTrainingOperationReadinessState.Active =>
                        "Student training start did not confirm success, but an authoritative training flight is active. Open Current Flight to continue or recover it. If this repeats, create a diagnostic bundle in Settings.",
                    StudentTrainingOperationReadinessState.CompletedAwaitingFinalization =>
                        "Student training start did not confirm success, but the authoritative training flight is awaiting finalization. Open Current Flight to recover it. If this repeats, create a diagnostic bundle in Settings.",
                    StudentTrainingOperationReadinessState.Ready =>
                        "Student training did not start. Authoritative readiness was refreshed; retry Start Student Training. If this repeats, create a diagnostic bundle in Settings.",
                    StudentTrainingOperationReadinessState.OtherFlightActive =>
                        "Student training did not start. Open Current Flight to finish or clear the authoritative flight before retrying. If this repeats, create a diagnostic bundle in Settings.",
                    _ =>
                        $"{_studentTraining.Detail} Student training did not start; follow the refreshed readiness guidance before retrying. If this repeats, create a diagnostic bundle in Settings."
                };
                SetField(
                    ref _studentTrainingStatusText,
                    recoveryDetail,
                    nameof(StudentTrainingStatusText));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception refreshException)
            {
                _logger?.LogWarning(
                    refreshException,
                    "Student training readiness could not be refreshed after a failed start.");
                ApplyStudentTraining(
                    new(
                        StudentTrainingOperationReadinessState.AuthorityUnavailable,
                        "Student training did not confirm success, and recovery readiness could not be refreshed. Reopen Career before another start attempt. If this repeats, create a diagnostic bundle in Settings.",
                        AirportIcao: _studentTraining.AirportIcao,
                        AircraftDisplayName: _studentTraining.AircraftDisplayName));
            }
        }
        finally
        {
            SetStartingStudentTraining(false);
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

    private async Task RefreshStudentTrainingAsync(
        CancellationToken cancellationToken)
    {
        StudentTrainingOperationReadiness readiness =
            await _studentTrainingReadiness
                .ReadAsync(cancellationToken)
                .ConfigureAwait(true);

        ApplyStudentTraining(readiness);
    }

    private void ApplyStudentTraining(
        StudentTrainingOperationReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        _studentTraining = readiness;
        SetField(
            ref _studentTrainingStatusText,
            readiness.Detail,
            nameof(StudentTrainingStatusText));
        SetField(
            ref _studentTrainingRouteText,
            string.IsNullOrWhiteSpace(readiness.AirportIcao)
                ? "LOCAL CIRCUIT · airport unavailable"
                : $"{readiness.AirportIcao} → {readiness.AirportIcao} · LOCAL CIRCUIT",
            nameof(StudentTrainingRouteText));
        SetField(
            ref _studentTrainingAircraftText,
            string.IsNullOrWhiteSpace(readiness.AircraftDisplayName)
                ? "Training aircraft: unavailable"
                : $"Training aircraft: {readiness.AircraftDisplayName}",
            nameof(StudentTrainingAircraftText));
        OnPropertyChanged(nameof(CanStartStudentTraining));
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

    private void SetCareerAuthorityUnavailable(bool value)
    {
        if (_careerAuthorityUnavailable == value)
            return;

        _careerAuthorityUnavailable = value;
        OnPropertyChanged(nameof(CanStartCareer));
        OnPropertyChanged(nameof(CanEditHomeAirport));
    }

    private void SetStartingStudentTraining(bool value)
    {
        if (_isStartingStudentTraining == value)
            return;

        _isStartingStudentTraining = value;
        OnPropertyChanged(nameof(IsStartingStudentTraining));
        OnPropertyChanged(nameof(CanStartStudentTraining));
        OnPropertyChanged(nameof(StudentTrainingButtonText));
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

    private sealed class UnavailableStudentTrainingAccess
        : IStudentTrainingOperationReadinessSource,
          IStudentTrainingOperationStartAction
    {
        public static UnavailableStudentTrainingAccess Instance { get; } = new();

        public Task<StudentTrainingOperationReadiness> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new StudentTrainingOperationReadiness(
                    StudentTrainingOperationReadinessState.AuthorityUnavailable,
                    "Student training is unavailable in this application context."));
        }

        public Task<StudentTrainingOperationStartResult> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Student training is unavailable in this application context.");
        }
    }
}

public sealed record CareerQualificationItemViewModel(
    string Title,
    string Kind,
    string Status,
    string Description,
    string Prerequisites);
