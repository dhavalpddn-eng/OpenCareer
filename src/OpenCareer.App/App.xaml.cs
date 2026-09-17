using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect;

namespace OpenCareer.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly ServiceProvider _services;
    private MainWindow? _window;
    private bool _isShuttingDown;
    private bool _shutdownComplete;

    public App()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<SimConnectConnection>();
        services.AddSingleton<ISimulatorConnection>(provider =>
            provider.GetRequiredService<SimConnectConnection>());
        services.AddSingleton<ISimulatorTelemetrySource>(provider =>
            provider.GetRequiredService<SimConnectConnection>());
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = _services.GetRequiredService<MainWindow>();
        _window.AppWindow.Closing += OnMainWindowClosing;
        _window.Activate();
        _services.GetRequiredService<ISimulatorConnection>().Start();
    }

    private async void OnMainWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownComplete)
            return;

        args.Cancel = true;
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        _window?.StopStatusUpdates();
        var logger = _services.GetRequiredService<ILogger<App>>();
        try
        {
            await _services.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenCareer shutdown failed.");
        }
        finally
        {
            _shutdownComplete = true;
            _window?.Close();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogError(e.Exception, "Unhandled OpenCareer UI exception.");
    }
}
