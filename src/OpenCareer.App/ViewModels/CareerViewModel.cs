using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Careers;

namespace OpenCareer.App.ViewModels;

public sealed class CareerViewModel : INotifyPropertyChanged
{
    private readonly PlayerCareerOnboardingCoordinator _onboarding;
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly TimeProvider _timeProvider;

    private string _homeAirportIcao = string.Empty;
    private string _statusText =
        "Choose the airport where your civilian career will begin.";
    private string _locationText =
        "No career profile exists yet.";
    private bool _hasCareer;
    private bool _isCreating;

    public CareerViewModel(
        PlayerCareerOnboardingCoordinator onboarding,
        PlayerCareerRuntimeState runtime,
        TimeProvider timeProvider)
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
            return;
        }

        ApplyCurrent(current);
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
