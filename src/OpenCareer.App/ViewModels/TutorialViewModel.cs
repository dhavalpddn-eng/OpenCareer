using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Tutorials;

namespace OpenCareer.App.ViewModels;

public sealed class TutorialViewModel : INotifyPropertyChanged
{
    private readonly TutorialCoordinator _coordinator;

    public TutorialViewModel(TutorialCoordinator coordinator)
    {
        _coordinator = coordinator;
        _coordinator.StateChanged += OnCoordinatorStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<TutorialNavigationRequestedEventArgs> NavigationRequested
    {
        add => _coordinator.NavigationRequested += value;
        remove => _coordinator.NavigationRequested -= value;
    }

    public bool IsActive => _coordinator.Current.IsActive;
    public string CurrentTitle => _coordinator.Current.Step?.Title ?? string.Empty;
    public string CurrentBody => _coordinator.Current.Step?.Body ?? string.Empty;
    public bool CanGoBack => _coordinator.Current.CanGoBack;
    public string StepPosition => _coordinator.Current.IsActive
        ? $"{_coordinator.Current.CurrentIndex + 1} / {_coordinator.Current.StepCount}"
        : string.Empty;
    public string NextButtonText => _coordinator.Current.IsLastStep ? "Finish" : "Next";
    public string FeatureStateText => _coordinator.Current.FeatureState switch
    {
        TutorialFeatureState.Available => "AVAILABLE",
        TutorialFeatureState.Unavailable => "UNAVAILABLE RIGHT NOW",
        TutorialFeatureState.ComingLater => "COMING LATER",
        _ => "UNKNOWN"
    };

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _coordinator.InitializeAsync(cancellationToken);

    public Task StartFirstJobAsync(CancellationToken cancellationToken = default) =>
        _coordinator.StartAsync(AppTutorialCatalog.FirstJobId, cancellationToken: cancellationToken);

    public Task RestartIntroAsync(CancellationToken cancellationToken = default) =>
        _coordinator.RestartAsync(AppTutorialCatalog.AppIntroId, cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken = default) =>
        _coordinator.NextAsync(cancellationToken);

    public Task BackAsync(CancellationToken cancellationToken = default) =>
        _coordinator.BackAsync(cancellationToken);

    public Task SkipAsync(CancellationToken cancellationToken = default) =>
        _coordinator.SkipAsync(cancellationToken);

    private void OnCoordinatorStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentBody));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(StepPosition));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(FeatureStateText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
