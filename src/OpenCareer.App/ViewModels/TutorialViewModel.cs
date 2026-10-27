using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Tutorials;

namespace OpenCareer.App.ViewModels;

public sealed class TutorialViewModel : INotifyPropertyChanged
{
    private readonly TutorialCoordinator _coordinator;
    private readonly ILogger<TutorialViewModel> _logger;
    private int _actionActive;
    private string _actionStatus = string.Empty;

    public TutorialViewModel(
        TutorialCoordinator coordinator,
        ILogger<TutorialViewModel>? logger = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? NullLogger<TutorialViewModel>.Instance;
        _coordinator.StateChanged += OnCoordinatorStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<TutorialNavigationRequestedEventArgs> NavigationRequested
    {
        add => _coordinator.NavigationRequested += value;
        remove => _coordinator.NavigationRequested -= value;
    }

    public bool IsActive => _coordinator.Current.IsActive;
    public bool IsBusy => Volatile.Read(ref _actionActive) != 0;
    public bool CanRunActions => !IsBusy;
    public string CurrentTitle => _coordinator.Current.Step?.Title ?? string.Empty;
    public string CurrentBody => _coordinator.Current.Step?.Body ?? string.Empty;
    public bool CanGoBack => _coordinator.Current.CanGoBack;
    public bool CanGoBackAction => CanGoBack && CanRunActions;
    public string ActionStatus
    {
        get => _actionStatus;
        private set => SetField(ref _actionStatus, value);
    }
    public string StepPosition => _coordinator.Current.IsActive
        ? $"{_coordinator.Current.CurrentIndex + 1} / {_coordinator.Current.StepCount}"
        : string.Empty;
    public string NextButtonText => _coordinator.Current.IsLastStep ? "Finish" : "Next";
    public string EvidenceStateText => _coordinator.Current.EvidenceState switch
    {
        TutorialStepEvidenceState.Waiting => "WAITING FOR LIVE FLIGHT",
        TutorialStepEvidenceState.Satisfied => "LIVE EVIDENCE CONFIRMED",
        _ => "NOT REQUIRED"
    };

    public string FeatureStateText => _coordinator.Current.FeatureState switch
    {
        TutorialFeatureState.Available => "AVAILABLE",
        TutorialFeatureState.Unavailable => "UNAVAILABLE RIGHT NOW",
        TutorialFeatureState.ComingLater => "COMING LATER",
        _ => "UNKNOWN"
    };

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync(
            "initialize",
            _coordinator.InitializeAsync,
            cancellationToken);

    public void RefreshLiveEvidence() =>
        _coordinator.RefreshLiveEvidence();

    public Task StartFirstJobAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync(
            "start first-job preview",
            token => _coordinator.StartAsync(
                AppTutorialCatalog.FirstJobId,
                cancellationToken: token),
            cancellationToken);

    public Task RestartIntroAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync(
            "restart intro",
            token => _coordinator.RestartAsync(AppTutorialCatalog.AppIntroId, token),
            cancellationToken);

    public Task PreviewBannerTowAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync(
            "preview banner tow",
            token => _coordinator.StartAsync(
                AppTutorialCatalog.BannerTowId,
                resume: false,
                token),
            cancellationToken);

    public Task PreviewCarrierTakeoffAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync(
            "preview carrier takeoff",
            token => _coordinator.StartAsync(
                AppTutorialCatalog.CarrierTakeoffId,
                resume: false,
                token),
            cancellationToken);

    public Task PreviewCarrierLandingAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync(
            "preview carrier landing",
            token => _coordinator.StartAsync(
                AppTutorialCatalog.CarrierLandingId,
                resume: false,
                token),
            cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync("advance", _coordinator.NextAsync, cancellationToken);

    public Task BackAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync("go back", _coordinator.BackAsync, cancellationToken);

    public Task SkipAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync("skip", _coordinator.SkipAsync, cancellationToken);

    private async Task RunActionAsync(
        string action,
        Func<CancellationToken, Task> actionAsync,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _actionActive, 1, 0) != 0)
        {
            ActionStatus =
                "Another tutorial action is already running. Wait for it to finish, then retry.";
            return;
        }

        NotifyActionStateChanged();
        ActionStatus = string.Empty;
        bool succeeded = false;

        try
        {
            await actionAsync(cancellationToken);
            succeeded = true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Tutorial action {TutorialAction} failed.",
                action);
            ActionStatus =
                "Tutorial progress could not be loaded or saved. The current tutorial state was kept. Check local storage access, then retry. If it happens again, create a diagnostic bundle in Settings.";
        }
        finally
        {
            Volatile.Write(ref _actionActive, 0);
            if (succeeded)
                ActionStatus = string.Empty;
            NotifyActionStateChanged();
        }
    }

    private void NotifyActionStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRunActions));
        OnPropertyChanged(nameof(CanGoBackAction));
    }

    private void OnCoordinatorStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoBackAction));
        OnPropertyChanged(nameof(StepPosition));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(FeatureStateText));
        OnPropertyChanged(nameof(EvidenceStateText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
