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
using OpenCareer.Application.Military;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Application.Tutorials;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Military;
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
        services.AddSingleton<ICommodityMarketSnapshotStore, SqliteCommodityMarketSnapshotStore>();
        services.AddSingleton<WorldSimulationPersistenceService>();
        services.AddSingleton<IJobBoardStateStore, SqliteJobBoardStateStore>();
        services.AddSingleton<IJobContractStore, SqliteJobContractStore>();
        services.AddSingleton<IJobContractRecoverySource, SqliteJobContractRecoverySource>();
        services.AddSingleton<JobContractRecoveryService>();
        services.AddSingleton<JobContractRuntimeState>();
        services.AddSingleton<IJobContractRuntimeSource>(provider =>
            provider.GetRequiredService<JobContractRuntimeState>());
        services.AddSingleton<JobContractLifecycleService>();
        services.AddSingleton<JobOfferAcceptanceService>();
        services.AddSingleton<IEconomyLedgerStore, SqliteEconomyLedgerStore>();

        services.AddSingleton<EconomySettlementService>();
        services.AddSingleton<SettlementPendingContractSource>();

        services.AddSingleton<SettlementPendingContractCoordinator>();

        services.AddSingleton<SqlitePlayerCareerProfileStore>();
        services.AddSingleton<IPlayerCareerProfileStore>(provider =>
            provider.GetRequiredService<SqlitePlayerCareerProfileStore>());
        services.AddSingleton<PlayerCareerRuntimeState>();
        services.AddSingleton<PlayerCareerOnboardingCoordinator>();
        services.AddSingleton<PlayerCareerLocationCoordinator>();
        services.AddSingleton<PlayerCareerQualificationCoordinator>();

        services.AddSingleton<PlayerCareerExperienceCoordinator>();
        services.AddSingleton<CareerLogbookExperienceCoordinator>();

        services.AddSingleton<SqliteLogbookStore>();
        services.AddSingleton<ILogbookSource>(provider =>
            provider.GetRequiredService<SqliteLogbookStore>());
        services.AddSingleton<ILogbookIdempotencySource>(provider =>
            provider.GetRequiredService<SqliteLogbookStore>());
        services.AddSingleton<ILogbookWriter>(provider =>
            provider.GetRequiredService<SqliteLogbookStore>());

        services.AddSingleton<SqliteConflictCampaignStore>();
        services.AddSingleton<SqliteMilitaryCareerProfileStore>();
        services.AddSingleton<IMilitaryCareerProfileStore>(provider =>
            provider.GetRequiredService<SqliteMilitaryCareerProfileStore>());
        services.AddSingleton<SqliteDatabaseSnapshotService>();
        services.AddSingleton<SqliteBackupArchiveRestoreService>();
        services.AddSingleton<AppDataRestoreService>();
        services.AddSingleton<IConflictCampaignStore>(provider =>
            provider.GetRequiredService<SqliteConflictCampaignStore>());
        services.AddSingleton<IConflictCampaignRecoverySource>(provider =>
            provider.GetRequiredService<SqliteConflictCampaignStore>());
        services.AddSingleton<ConflictCampaignRuntimeState>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IConflictTheaterCatalog, DefaultConflictTheaterCatalog>();
        services.AddSingleton<ConflictOperationsService>();
        services.AddSingleton<ConflictCampaignCoordinator>();
        services.AddSingleton<SqliteOperationConsequenceStore>();
        services.AddSingleton<IOperationConsequenceStore>(provider =>
            provider.GetRequiredService<SqliteOperationConsequenceStore>());
        services.AddSingleton<IOperationConsequenceHistorySource>(provider =>
            provider.GetRequiredService<SqliteOperationConsequenceStore>());
        services.AddSingleton<IOperationResolutionRegistry, InMemoryOperationResolutionRegistry>();
        services.AddSingleton<OperationResolver>();
        services.AddSingleton<IOperationResolver>(provider =>
            new IdempotentOperationResolver(
                provider.GetRequiredService<OperationResolver>(),
                provider.GetRequiredService<IOperationResolutionRegistry>()));
        services.AddSingleton<IMilitaryReputationConsequenceRegistry, InMemoryMilitaryReputationConsequenceRegistry>();
        services.AddSingleton<MilitaryReputationConsequence>();
        services.AddSingleton<OperationConsequenceOrchestrator>();
        services.AddSingleton<PersistedOperationConsequenceCoordinator>();
        services.AddSingleton<MilitaryDispatchService>();
        services.AddSingleton<MilitaryCampaignMissionService>();
        services.AddSingleton<MilitaryCampaignTransitionService>();

        services.AddSingleton<LogbookCommitCoordinator>();
        services.AddSingleton<SettledJobLogbookCoordinator>();
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

        services.AddSingleton<IFlightStateEvidenceSource>(provider =>
            provider.GetRequiredService<FlightSessionRuntime>());

        services.AddSingleton<SimConnectConnection>();
        services.AddSingleton<ISimulatorConnection>(provider =>
            provider.GetRequiredService<SimConnectConnection>());
        services.AddSingleton<ISimulatorTelemetrySource>(provider =>
            provider.GetRequiredService<SimConnectConnection>());

        services.AddOpenCareerPlanningServices();

        services.AddSingleton<JobAcceptanceFleetBridge>();

        services.AddSingleton<AcceptedJobDispatchBridge>();

        services.AddSingleton<AcceptedJobStartBridge>();

        services.AddSingleton<AcceptedJobFlightSessionBridge>();

        services.AddSingleton<JobFlightCompletionEvidenceTracker>();

        services.AddSingleton<JobFlightSessionCompletionBridge>();

        services.AddSingleton<CompletedJobContractBridge>();

        services.AddSingleton<CareerFlightReservationReleaseCoordinator>();

        services.AddSingleton<CareerFlightFinalizationCoordinator>();

        services.AddSingleton<CareerFlightTerminalWorkflowCoordinator>();

        services.AddSingleton<CareerJobPlayableLoopCoordinator>();

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
        services.AddSingleton<MilitaryGovernmentViewModel>();
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
            bool restored = await _services
                .GetRequiredService<AppDataRestoreService>()
                .ApplyPendingDatabaseRestoreAsync();

            if (restored)
            {
                logger.LogInformation(
                    "Applied pending OpenCareer database restore before application data stores were opened.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Pending OpenCareer database restore failed; existing local data was preserved.");
        }

        try
        {
            PlayerCareerProfileStoreRecord? recovered =
                await _services
                    .GetRequiredService<PlayerCareerRuntimeState>()
                    .InitializeAsync();

            if (recovered is not null)
            {
                logger.LogInformation(
                    "Recovered player career profile {CareerId} at revision {Revision}.",
                    recovered.Profile.CareerId,
                    recovered.Revision);
            }
            else
            {
                logger.LogInformation(
                    "No player career profile found; career onboarding is required.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Player career profile recovery failed; OpenCareer will continue without claiming an active career profile.");
        }

        try
        {
            IReadOnlyList<PersistedJobContract> recoveredContracts =
                await _services
                    .GetRequiredService<JobContractRuntimeState>()
                    .InitializeAsync();

            if (recoveredContracts.Count > 0)
            {
                logger.LogInformation(
                    "Recovered {ContractCount} persisted job contracts requiring runtime reconciliation: {AcceptedCount} accepted, {InProgressCount} in progress, {CompletedCount} completed.",
                    recoveredContracts.Count,
                    recoveredContracts.Count(item => item.Contract.Status == ContractStatus.Accepted),
                    recoveredContracts.Count(item => item.Contract.Status == ContractStatus.InProgress),
                    recoveredContracts.Count(item => item.Contract.Status == ContractStatus.Completed));
            }
            else
            {
                logger.LogInformation(
                    "No persisted job contracts require runtime reconciliation.");
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

        try
        {
            ConflictCampaignStoreRecord? recovered = await _services
                .GetRequiredService<ConflictCampaignRuntimeState>()
                .InitializeAsync();

            if (recovered is not null)
            {
                logger.LogInformation(
                    "Recovered military conflict campaign {CampaignId} at revision {Revision}.",
                    recovered.Checkpoint.CampaignId,
                    recovered.Revision);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Military conflict campaign recovery failed; the rest of OpenCareer will continue.");
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
