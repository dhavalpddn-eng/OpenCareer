using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class SettingsPage : Page
{
    private TutorialViewModel? _tutorial;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _tutorial = e.Parameter as TutorialViewModel;
    }

    private async void RestartIntro_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.RestartIntroAsync();
    }

    private async void StartFirstJob_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.StartFirstJobAsync();
    }

    private async void PreviewBannerTow_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.PreviewBannerTowAsync();
    }

    private async void PreviewCarrierTakeoff_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.PreviewCarrierTakeoffAsync();
    }

    private async void PreviewCarrierLanding_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.PreviewCarrierLandingAsync();
    }
}
