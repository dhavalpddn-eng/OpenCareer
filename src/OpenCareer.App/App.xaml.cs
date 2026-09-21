using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using OpenCareer.App.Services;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Ai;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Dashboard;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Application.Tutorials;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Ai;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;
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
        services.AddSingleton(OpenAiNarrativeOptions.FromEnvironment());
        services.AddSingleton<IAiNarrativeProvider, OpenAiNarrativeProvider>();
        services.AddSingleton<IAiNarrativeService, AiNarrativeService>();

        services.AddSingleton<IDashboardSnapshotSource, UnavailableDashboardSnapshotSource>();
        services.AddSingleton(provider =>
            new OpenCareerDatabaseOptions(
                provider.GetRequiredService<OpenCareerDataPaths>().DatabaseFile));
        services.AddSingleton<IWorldSimulationStateStore, SqliteWorldSimulationStateStore>();
        services.AddSingleton<WorldSimulationPersistenceService>();
        services.AddSingleton<IJobBoardStateStore, SqliteJobBoardStateStore>();
        services.AddSingleton<JobBoardGenerationService>();
        services.AddSingleton<IJobContractStore, SqliteJobContractStore>();
        services.AddSingleton<IJobContractRecoverySource, SqliteJobContractRecoverySource>();
        services.AddSingleton<JobContractRecoveryService>();
        services.AddSingleton<JobContractLifecycleService>();
        services.AddSingleton<JobOfferAcceptanceService>();
        services.AddSingleton<IEconomyLedgerStore, SqliteEconomyLedgerStore>();
        services.AddSingleton<EconomySettlementService>();
        services.AddSingleton<SqliteLogbookStore>();
        services.AddSingleton<ILogbookSource>(provider =>
            provider.GetRequiredService<SqliteLogbookStore>());
        services.AddSingleton<ILogbookWriter>(provider =>
            provider.GetRequiredService<SqliteLogbookStore>());
        services.AddSingleton<LogbookCommitCoordinator>();
        services.AddSingleton<DashboardGuidanceEngine>();
        services.AddSingleton<AppDataBackupService>();
        services.AddSingleton<DiagnosticBundleService>();
        services.AddSingleton<ShellOpenService>();

        services.AddSingleton<FlightSessionCoordinator>();
        services.AddSingleton(FlightSessionCheckpointPolicy.Default);
        services.AddSingleton<IFlightSessionCheckpointStore>(provider =>
            new SqliteFlightSessionCheckpointStore(
                provider
                    .GetRequiredService<OpenCareerDataPaths>()
                    .DatabaseFile));
        services.AddSingleton<FlightSessionPersistenceService>();
        services.AddSingleton<FlightSessionCompletionService>();
        services.AddSingleton<FlightTelemetryEvidenceProcessor>();
        services.AddSingleton<FlightContinuityPolicy>();
        services.AddSingleton<FlightSessionRuntime>();

        services.AddSingleton<SimConnectConnection>();
        services.AddSingleton<ISimulatorConnection>(provider =>
            provider.GetRequiredService<SimConnectConnection>());
        services.AddSingleton<ISimulatorTelemetrySource>(provider =>
            provider.GetRequiredService<SimConnectConnection>());

        services.AddSingleton<ITutorialCatalog, AppTutorialCatalog>();
        services.AddSingleton<FlightSessionTutorialEvidenceSource>();
        services.AddSingleton<ITutorialStepEvidenceSource>(provider =>
            provider.GetRequiredService<FlightSessionTutorialEvidenceSource>());
        services.AddSingleton<ITutorialFeatureReadiness, CurrentTutorialFeatureReadiness>();
        services.AddSingleton<ITutorialProgressStore, JsonTutorialProgressStore>();
        services.AddSingleton<TutorialCoordinator>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<LogbookViewModel>();
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

        try
        {
            IReadOnlyList<PersistedJobContract> recoveredContracts =
                await _services
                    .GetRequiredService<JobContractRecoveryService>()
                    .RecoverAsync();

            if (recoveredContracts.Count > 0)
            {
                logger.LogInformation(
                    "Recovered {ContractCount} persisted job contracts requiring runtime reconciliation: {AcceptedCount} accepted, {InProgressCount} in progress, {CompletedCount} completed.",
                    recoveredContracts.Count,
                    recoveredContracts.Count(item => item.Contract.Status == ContractStatus.Accepted),
                    recoveredContracts.Count(item => item.Contract.Status == ContractStatus.InProgress),
                    recoveredContracts.Count(item => item.Contract.Status == ContractStatus.Completed));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Job-contract recovery failed. OpenCareer will continue without claiming recovered active or settlement-pending jobs.");
        }

        try
        {
            FlightSession? recovered =
                await _services
                    .GetRequiredService<FlightSessionPersistenceService>()
                    .RecoverAsync();

            if (recovered is not null)
            {
                logger.LogInformation(
                    "Recovered flight session {SessionId} in {Status}/{OperationState}.",
                    recovered.SessionId,
                    recovered.Status,
                    recovered.OperationState);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "FlightSession recovery failed. OpenCareer will continue without claiming a recovered active flight.");
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

            try
            {
                await _services
                    .GetRequiredService<FlightSessionPersistenceService>()
                    .FlushAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Final FlightSession checkpoint failed during shutdown.");
            }

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
