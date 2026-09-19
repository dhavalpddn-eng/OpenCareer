using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using OpenCareer.App.Services;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Dashboard;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Application.Tutorials;
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

        var dataPaths = new OpenCareerDataPaths();
        dataPaths.EnsureDirectories();
        var fileLogger = new OpenCareerFileLoggerProvider(dataPaths);

        services.AddSingleton(dataPaths);
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(fileLogger);
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();
        services.AddSingleton<IDashboardSnapshotSource, UnavailableDashboardSnapshotSource>();
        services.AddSingleton<DashboardGuidanceEngine>();
        services.AddSingleton<AppDataBackupService>();
        services.AddSingleton<DiagnosticBundleService>();
        services.AddSingleton<ShellOpenService>();

        services.AddSingleton<SimConnectConnection>();
        services.AddSingleton<ISimulatorConnection>(provider =>
            provider.GetRequiredService<SimConnectConnection>());
        services.AddSingleton<ISimulatorTelemetrySource>(provider =>
            provider.GetRequiredService<SimConnectConnection>());

        services.AddSingleton<ITutorialCatalog, AppTutorialCatalog>();
        services.AddSingleton<ITutorialFeatureReadiness, CurrentTutorialFeatureReadiness>();
        services.AddSingleton<ITutorialProgressStore, JsonTutorialProgressStore>();
        services.AddSingleton<TutorialCoordinator>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<TutorialViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var logger = _services.GetRequiredService<ILogger<App>>();

        try
        {
            await _services
                .GetRequiredService<IAppSettingsService>()
                .InitializeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenCareer settings initialization failed; using defaults.");
        }

        _window = _services.GetRequiredService<MainWindow>();
        _window.AppWindow.Closing += OnMainWindowClosing;
        _window.Activate();

        _services.GetRequiredService<ISimulatorConnection>().Start();
        logger.LogInformation("OpenCareer application launched.");
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
            logger.LogInformation("OpenCareer application shutting down.");
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

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogError(e.Exception, "Unhandled OpenCareer UI exception.");
    }
}
