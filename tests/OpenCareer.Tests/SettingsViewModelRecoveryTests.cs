using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SettingsViewModelRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"opencareer-settings-recovery-{Guid.NewGuid():N}");

    [Fact]
    public void NoSessionReportsAvailableSqliteRecoveryWithoutStaleMilestoneCopy()
    {
        TestContext context = Create();

        Assert.Contains("SQLite-backed", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("previous-valid fallback", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("No flight session is currently loaded", context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain("MBL-07", context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    [Fact]
    public async Task FailedRecoveryWarnsBeforeAnotherFlightStarts()
    {
        var sessions = new FlightSessionCoordinator();
        var persistence =
            new FlightSessionPersistenceService(
                sessions,
                new FailingCheckpointStore());

        await Assert.ThrowsAsync<IOException>(
            () => persistence.RecoverAsync());

        TestContext context = Create(
            sessions: sessions,
            flightPersistence: persistence);

        Assert.Contains("RECOVERY ATTENTION", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("did not complete", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("did not clear", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("Close and reopen OpenCareer", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("diagnostic bundle", context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain(
            "Synthetic checkpoint read failure",
            context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain(
            "No flight session is currently loaded",
            context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    [Fact]
    public void NormalCareerClearlyDisablesCleanTestReset()
    {
        TestContext context = Create();

        Assert.Contains("NORMAL CAREER", context.ViewModel.DataProfileStatus);
        Assert.False(context.ViewModel.CanScheduleDevelopmentProfileReset);

        context.ViewModel.ScheduleDevelopmentProfileReset();

        Assert.Contains("normal career profile", context.ViewModel.ActionStatus);
        Assert.False(File.Exists(
            Path.Combine(_root, ".opencareer-reset-on-next-launch")));
    }

    [Fact]
    public void FailedCleanTestResetSchedulingRemainsRetryableWithoutTechnicalDetails()
    {
        OpenCareerDataProfileSelection selection =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                _root);
        selection.Prepare();
        File.WriteAllText(
            selection.Paths.ProfileMarkerFile,
            "Synthetic invalid marker that must not reach player-facing text.");
        TestContext context = Create(paths: selection.Paths);

        context.ViewModel.ScheduleDevelopmentProfileReset();

        Assert.True(context.ViewModel.CanScheduleDevelopmentProfileReset);
        Assert.Contains("was not scheduled", context.ViewModel.ActionStatus);
        Assert.Contains("then retry", context.ViewModel.ActionStatus);
        Assert.Contains("diagnostic bundle", context.ViewModel.ActionStatus);
        Assert.DoesNotContain("marker is missing or invalid", context.ViewModel.ActionStatus);
        Assert.DoesNotContain("Synthetic invalid marker", context.ViewModel.ActionStatus);
        Assert.False(File.Exists(selection.Paths.DevelopmentResetRequestFile));

        Directory.Delete(selection.Paths.Root, recursive: true);
        selection.Prepare();
        context.ViewModel.ScheduleDevelopmentProfileReset();

        Assert.False(context.ViewModel.CanScheduleDevelopmentProfileReset);
        Assert.Contains("reset scheduled", context.ViewModel.ActionStatus);
        Assert.True(File.Exists(selection.Paths.DevelopmentResetRequestFile));
    }

    [Fact]
    public async Task CleanTestResetSchedulingRejectsConcurrentSettingsActionThenRemainsRetryable()
    {
        OpenCareerDataProfileSelection selection =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                _root);
        selection.Prepare();
        TestContext context = Create(paths: selection.Paths);
        var resetStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReset = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Settings.NextResetStarted = resetStarted;
        context.Settings.NextResetRelease = releaseReset.Task;

        Task reset = context.ViewModel.ResetPreferencesAsync();
        await resetStarted.Task;

        Assert.False(context.ViewModel.CanScheduleDevelopmentProfileReset);
        context.ViewModel.ScheduleDevelopmentProfileReset();

        Assert.Contains(
            "already running",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(selection.Paths.DevelopmentResetRequestFile));

        releaseReset.SetResult();
        await reset;

        Assert.True(context.ViewModel.CanScheduleDevelopmentProfileReset);
        context.ViewModel.ScheduleDevelopmentProfileReset();

        Assert.False(context.ViewModel.CanScheduleDevelopmentProfileReset);
        Assert.True(File.Exists(selection.Paths.DevelopmentResetRequestFile));
    }

    [Fact]
    public async Task BackupExcludesPendingDevelopmentProfileResetRequest()
    {
        OpenCareerDataProfileSelection selection =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                _root);
        selection.Prepare();
        TestContext context = Create(paths: selection.Paths);
        context.ViewModel.ScheduleDevelopmentProfileReset();

        await context.ViewModel.CreateBackupAsync();

        string backupPath = Assert.Single(
            Directory.EnumerateFiles(selection.Paths.BackupsFolder, "*.zip"));
        using ZipArchive archive = ZipFile.OpenRead(backupPath);
        Assert.DoesNotContain(
            archive.Entries,
            entry => string.Equals(
                entry.FullName,
                Path.GetFileName(selection.Paths.DevelopmentResetRequestFile),
                StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(selection.Paths.DevelopmentResetRequestFile));
    }

    [Fact]
    public void RestoreInventoryIncludesOnlyTopLevelOpenCareerBackupArchives()
    {
        var paths = new OpenCareerDataPaths(_root);
        paths.EnsureDirectories();
        string expected = "opencareer-backup-20261001-120000-000.zip";
        File.WriteAllText(Path.Combine(paths.BackupsFolder, expected), "archive");
        File.WriteAllText(Path.Combine(paths.BackupsFolder, "unrelated.zip"), "archive");
        string nested = Path.Combine(paths.BackupsFolder, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(
            Path.Combine(nested, "opencareer-backup-20261001-130000-000.zip"),
            "archive");
        TestContext context = Create(paths: paths);

        context.ViewModel.RefreshBackupArchives();

        Assert.Equal(new[] { expected }, context.ViewModel.BackupArchiveOptions);
        Assert.Null(context.ViewModel.SelectedBackupArchive);
        Assert.False(context.ViewModel.CanStageSelectedBackup);
        Assert.Contains("Select", context.ViewModel.BackupRestoreStatus);
    }

    [Fact]
    public async Task SelectedRestoreCreatesCurrentSafetyBackupAndCanBeCancelled()
    {
        var paths = new OpenCareerDataPaths(_root);
        paths.EnsureDirectories();
        await CreateValidDatabaseAsync(paths.DatabaseFile);
        TestContext context = Create(paths: paths);

        await context.ViewModel.CreateBackupAsync();
        string generated = Assert.Single(
            Directory.EnumerateFiles(paths.BackupsFolder, "*.zip"));
        string selectedPath = Path.Combine(
            paths.BackupsFolder,
            "opencareer-backup-20261001-120000-000.zip");
        File.Move(generated, selectedPath);
        context.ViewModel.RefreshBackupArchives();
        context.ViewModel.SelectedBackupIndex = 0;

        await context.ViewModel.StageSelectedBackupAsync();

        Assert.True(context.ViewModel.HasPendingDatabaseRestore);
        Assert.True(File.Exists(paths.PendingDatabaseRestoreFile));
        Assert.Equal(
            2,
            Directory.EnumerateFiles(paths.BackupsFolder, "*.zip").Count());
        Assert.Contains("safety backup created", context.ViewModel.ActionStatus);
        Assert.Contains("Restart OpenCareer", context.ViewModel.ActionStatus);
        Assert.True(context.ViewModel.CanCancelPendingDatabaseRestore);

        await context.ViewModel.CancelPendingDatabaseRestoreAsync();

        Assert.False(context.ViewModel.HasPendingDatabaseRestore);
        Assert.False(File.Exists(paths.PendingDatabaseRestoreFile));
        Assert.Contains("cancelled", context.ViewModel.ActionStatus);
        Assert.Contains("remain active", context.ViewModel.ActionStatus);
    }

    [Fact]
    public async Task InvalidSelectedRestoreKeepsSafetyBackupAndHidesArchiveDetails()
    {
        var paths = new OpenCareerDataPaths(_root);
        paths.EnsureDirectories();
        string selectedPath = Path.Combine(
            paths.BackupsFolder,
            "opencareer-backup-20261001-120000-000.zip");
        await File.WriteAllTextAsync(
            selectedPath,
            "Synthetic corrupt archive detail must not reach player-facing text.");
        TestContext context = Create(paths: paths);
        context.ViewModel.SelectedBackupIndex = 0;

        await context.ViewModel.StageSelectedBackupAsync();

        Assert.False(context.ViewModel.HasPendingDatabaseRestore);
        Assert.False(File.Exists(paths.PendingDatabaseRestoreFile));
        Assert.Equal(
            2,
            Directory.EnumerateFiles(paths.BackupsFolder, "*.zip").Count());
        Assert.Contains("safety backup created", context.ViewModel.ActionStatus);
        Assert.Contains("no restore was staged", context.ViewModel.ActionStatus);
        Assert.DoesNotContain("Synthetic corrupt", context.ViewModel.ActionStatus);
        Assert.True(context.ViewModel.CanStageSelectedBackup);
    }

    [Fact]
    public async Task FailedStartupRestoreStaysVisibleWithOrWithoutPendingStage()
    {
        var paths = new OpenCareerDataPaths(_root);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(
            paths.PendingDatabaseRestoreFile,
            "Synthetic invalid database detail must not reach player-facing text.");
        TestContext context = Create(paths: paths);

        await Assert.ThrowsAnyAsync<Exception>(
            () => context.Restore.ApplyPendingDatabaseRestoreAsync());
        context.ViewModel.RefreshBackupArchives();

        Assert.True(context.Restore.PendingDatabaseRestoreApplyFailed);
        Assert.True(context.ViewModel.DatabaseRestoreApplyFailed);
        Assert.True(context.ViewModel.HasPendingDatabaseRestore);
        Assert.True(context.ViewModel.CanCancelPendingDatabaseRestore);
        Assert.Contains("RESTORE ATTENTION", context.ViewModel.BackupRestoreStatus);
        Assert.Contains("did not complete", context.ViewModel.BackupRestoreStatus);
        Assert.Contains("stop automatic retry", context.ViewModel.BackupRestoreStatus);
        Assert.Contains("diagnostic bundle", context.ViewModel.BackupRestoreStatus);
        Assert.DoesNotContain("Synthetic invalid", context.ViewModel.BackupRestoreStatus);
        Assert.DoesNotContain("not a database", context.ViewModel.BackupRestoreStatus);

        await context.ViewModel.CancelPendingDatabaseRestoreAsync();

        Assert.False(context.Restore.PendingDatabaseRestoreApplyFailed);
        Assert.False(context.ViewModel.DatabaseRestoreApplyFailed);
        Assert.False(context.ViewModel.HasPendingDatabaseRestore);
        Assert.False(File.Exists(paths.PendingDatabaseRestoreFile));
        Assert.DoesNotContain("RESTORE ATTENTION", context.ViewModel.BackupRestoreStatus);

        await File.WriteAllTextAsync(
            paths.PendingDatabaseRestoreFile,
            "Synthetic second invalid database detail.");
        await Assert.ThrowsAnyAsync<Exception>(
            () => context.Restore.ApplyPendingDatabaseRestoreAsync());
        File.Delete(paths.PendingDatabaseRestoreFile);
        context.ViewModel.RefreshBackupArchives();

        Assert.True(context.ViewModel.DatabaseRestoreApplyFailed);
        Assert.False(context.ViewModel.HasPendingDatabaseRestore);
        Assert.False(context.ViewModel.CanCancelPendingDatabaseRestore);
        Assert.Contains("RESTORE ATTENTION", context.ViewModel.BackupRestoreStatus);
        Assert.Contains("no staged restore remains", context.ViewModel.BackupRestoreStatus);
        Assert.Contains("Review current career data", context.ViewModel.BackupRestoreStatus);
        Assert.DoesNotContain("Synthetic second", context.ViewModel.BackupRestoreStatus);
    }

    [Fact]
    public async Task FailedDiagnosticExportIsContainedWithoutTechnicalDetails()
    {
        Directory.CreateDirectory(_root);
        string blockedRoot = Path.Combine(_root, "blocked-by-file");
        await File.WriteAllTextAsync(
            blockedRoot,
            "This file intentionally prevents creation of the data directory.");
        TestContext context = Create(
            paths: new OpenCareerDataPaths(blockedRoot));

        await context.ViewModel.ExportDiagnosticsAsync();

        Assert.False(context.ViewModel.IsBusy);
        Assert.Contains(
            "could not be created",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "retry",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Open Logs Folder",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            blockedRoot,
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "blocked-by-file",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedBackupIsContainedWithoutTechnicalDetails()
    {
        Directory.CreateDirectory(_root);
        string blockedRoot = Path.Combine(_root, "blocked-backup-root");
        await File.WriteAllTextAsync(
            blockedRoot,
            "This file intentionally prevents creation of the data directory.");
        TestContext context = Create(
            paths: new OpenCareerDataPaths(blockedRoot));

        await context.ViewModel.CreateBackupAsync();

        Assert.False(context.ViewModel.IsBusy);
        Assert.Contains(
            "could not be created",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "retry",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Open Logs Folder",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            blockedRoot,
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "blocked-backup-root",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedPreferenceSaveIsContainedWithoutTechnicalDetails()
    {
        TestContext context = Create();
        context.Settings.UpdateFailure = new InvalidOperationException(
            "Synthetic settings path must not reach player-facing text.");

        await context.ViewModel.SetReduceMotionAsync(true);

        Assert.Contains(
            "could not be saved",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "retry",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "diagnostic bundle",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Open Logs Folder",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Synthetic settings path",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.False(context.Settings.Current.ReduceMotion);
    }

    [Fact]
    public async Task FailedPreferenceResetIsContainedAndRemainsRetryable()
    {
        TestContext context = Create();
        await context.ViewModel.SetReduceMotionAsync(true);
        context.Settings.ResetFailure = new UnauthorizedAccessException(
            "Synthetic reset path must not reach player-facing text.");

        await context.ViewModel.ResetPreferencesAsync();

        Assert.False(context.ViewModel.IsBusy);
        Assert.Contains(
            "could not be reset",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "retry",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "diagnostic bundle",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Open Logs Folder",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Synthetic reset path",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);
        Assert.True(context.Settings.Current.ReduceMotion);

        context.Settings.ResetFailure = null;
        await context.ViewModel.ResetPreferencesAsync();

        Assert.False(context.ViewModel.IsBusy);
        Assert.Contains(
            "reset to defaults",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(context.Settings.Current.ReduceMotion);
    }

    [Fact]
    public async Task InvalidPreferenceFileKeepsRecoveryAvailableAndEditsLockedUntilReset()
    {
        var settings = new FakeSettings
        {
            RequiresReset = true
        };
        TestContext context = Create(settings: settings);

        Assert.False(context.ViewModel.CanEditPreferences);
        Assert.True(context.ViewModel.CanRunSettingsActions);
        Assert.Contains(
            "PREFERENCES RECOVERY REQUIRED",
            context.ViewModel.PreferenceStorageStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "Reset preferences to defaults",
            context.ViewModel.PreferenceStorageStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "career data is not changed",
            context.ViewModel.PreferenceStorageStatus,
            StringComparison.OrdinalIgnoreCase);

        await context.ViewModel.SetReduceMotionAsync(true);

        Assert.Equal(0, settings.WriteCount);
        Assert.Contains(
            "original preference file was preserved",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);

        settings.ResetFailure = new IOException(
            "Synthetic settings path must not reach player-facing text.");
        await context.ViewModel.ResetPreferencesAsync();

        Assert.True(settings.RequiresReset);
        Assert.False(context.ViewModel.CanEditPreferences);
        Assert.Equal(0, settings.WriteCount);
        Assert.Contains(
            "remain locked",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "preserved preference file",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Synthetic settings path",
            context.ViewModel.ActionStatus,
            StringComparison.Ordinal);

        settings.ResetFailure = null;
        await context.ViewModel.ResetPreferencesAsync();

        Assert.False(settings.RequiresReset);
        Assert.True(context.ViewModel.CanEditPreferences);
        Assert.Equal(1, settings.WriteCount);
        Assert.Contains(
            "reset to defaults",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Preferences are available from local storage.",
            context.ViewModel.PreferenceStorageStatus);
    }

    [Fact]
    public async Task PreferencePersistenceCancellationStillPropagates()
    {
        TestContext context = Create();
        context.Settings.UpdateFailure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => context.ViewModel.SetReduceMotionAsync(true));
        Assert.Equal("Ready.", context.ViewModel.ActionStatus);

        context.Settings.UpdateFailure = null;
        await context.ViewModel.SetReduceMotionAsync(true);
        context.Settings.ResetFailure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => context.ViewModel.ResetPreferencesAsync());
        Assert.False(context.ViewModel.IsBusy);
        Assert.Equal("Settings saved.", context.ViewModel.ActionStatus);
        Assert.True(context.Settings.Current.ReduceMotion);
    }

    [Fact]
    public async Task OverlappingPreferenceChangesPreserveBothAuthoritativeUpdates()
    {
        TestContext context = Create();
        var updateStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Settings.NextUpdateStarted = updateStarted;
        context.Settings.NextUpdateRelease = releaseUpdate.Task;

        Task reduceMotion = context.ViewModel.SetReduceMotionAsync(true);
        await updateStarted.Task;

        Task allowOnlineServices =
            context.ViewModel.SetAllowOptionalOnlineServicesAsync(true);
        Assert.False(allowOnlineServices.IsCompleted);

        releaseUpdate.SetResult();
        await Task.WhenAll(reduceMotion, allowOnlineServices);

        Assert.True(context.Settings.Current.ReduceMotion);
        Assert.True(context.Settings.Current.AllowOptionalOnlineServices);
        Assert.Equal(2, context.Settings.WriteCount);
    }

    [Fact]
    public async Task BackupWaitsForStartedPreferenceWriteAndRejectsLaterPreferenceChanges()
    {
        TestContext context = Create();
        var updateStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Settings.NextUpdateStarted = updateStarted;
        context.Settings.NextUpdateRelease = releaseUpdate.Task;

        Task reduceMotion = context.ViewModel.SetReduceMotionAsync(true);
        await updateStarted.Task;

        Task backup = context.ViewModel.CreateBackupAsync();

        Assert.True(context.ViewModel.IsBusy);
        Assert.False(backup.IsCompleted);

        Task rejectedPreference =
            context.ViewModel.SetAllowOptionalOnlineServicesAsync(true);

        Assert.True(rejectedPreference.IsCompletedSuccessfully);
        Assert.Contains(
            "already running",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, context.Settings.WriteCount);

        releaseUpdate.SetResult();
        await Task.WhenAll(reduceMotion, backup);

        Assert.False(context.ViewModel.IsBusy);
        Assert.True(context.Settings.Current.ReduceMotion);
        Assert.False(context.Settings.Current.AllowOptionalOnlineServices);
        Assert.Equal(1, context.Settings.WriteCount);
        Assert.Contains(
            "Backup created",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverlappingSettingsActionsRejectSecondAttemptUntilFirstFinishes()
    {
        TestContext context = Create();
        var resetStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReset = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Settings.NextResetStarted = resetStarted;
        context.Settings.NextResetRelease = releaseReset.Task;

        Task firstReset = context.ViewModel.ResetPreferencesAsync();
        await resetStarted.Task;

        Assert.True(context.ViewModel.IsBusy);
        Assert.False(context.ViewModel.CanRunSettingsActions);

        Task duplicateReset = context.ViewModel.ResetPreferencesAsync();
        Task rejectedPreference = context.ViewModel.SetReduceMotionAsync(true);

        Assert.True(duplicateReset.IsCompletedSuccessfully);
        Assert.True(rejectedPreference.IsCompletedSuccessfully);
        Assert.Contains(
            "already running",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, context.Settings.WriteCount);

        releaseReset.SetResult();
        await firstReset;

        Assert.False(context.ViewModel.IsBusy);
        Assert.True(context.ViewModel.CanRunSettingsActions);
        Assert.Equal(1, context.Settings.WriteCount);
        Assert.False(context.Settings.Current.ReduceMotion);
        Assert.Contains(
            "reset to defaults",
            context.ViewModel.ActionStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BindingStatusUsesReadOnlyDiscoverySourceWithoutClaimingUnbound()
    {
        TestContext context = Create(
            new FakeBindingSource(
                InputBindingDiscoverySnapshot.Unavailable(
                    "Official runtime profile assignments are not exposed.")));

        Assert.Contains("UNAVAILABLE", context.ViewModel.InputBindingStatus);
        Assert.Contains("not exposed", context.ViewModel.InputBindingStatus);
        Assert.DoesNotContain("UNBOUND", context.ViewModel.InputBindingStatus);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    [Fact]
    public void ActiveSessionRefreshesStatusWithoutPersistenceWrites()
    {
        TestContext context = Create();
        int changes = 0;
        context.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.CareerRecoveryStatus))
                changes++;
        };

        context.Sessions.Restore(FlightSession.Start(Now));

        Assert.Contains("active flight session", context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("restart checkpoints", context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(1, changes);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Suspended, "suspended flight session", "not completed")]
    [InlineData(FlightSessionStatus.Interrupted, "interrupted terminal session", "not completed")]
    public void NonCompletedRecoveryStatesAreNotDescribedAsCompleted(
        FlightSessionStatus status,
        string expectedState,
        string expectedQualification)
    {
        TestContext context = Create();

        context.Sessions.Restore(Session(status));

        Assert.Contains(expectedState, context.ViewModel.CareerRecoveryStatus);
        Assert.Contains(expectedQualification, context.ViewModel.CareerRecoveryStatus);
        Assert.DoesNotContain(
            "A completed terminal session",
            context.ViewModel.CareerRecoveryStatus);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Completed, "completed terminal session")]
    [InlineData(FlightSessionStatus.Cancelled, "cancelled terminal session")]
    public void TerminalSessionRemainsPendingUntilCleanup(
        FlightSessionStatus status,
        string expectedState)
    {
        TestContext context = Create();

        context.Sessions.Restore(Session(status));

        Assert.Contains(expectedState, context.ViewModel.CareerRecoveryStatus);
        Assert.Contains("cleanup", context.ViewModel.CareerRecoveryStatus);

        context.Sessions.ClearTerminalSession();

        Assert.Contains(
            "No flight session is currently loaded",
            context.ViewModel.CareerRecoveryStatus);
        Assert.Equal(0, context.Settings.WriteCount);
    }

    private TestContext Create(
        IInputBindingDiscoverySource? inputBindings = null,
        FlightSessionCoordinator? sessions = null,
        FlightSessionPersistenceService? flightPersistence = null,
        OpenCareerDataPaths? paths = null,
        FakeSettings? settings = null)
    {
        paths ??= new OpenCareerDataPaths(_root);
        settings ??= new FakeSettings();
        var connection = new FakeConnection();
        var telemetry = new FakeTelemetrySource();
        sessions ??= new FlightSessionCoordinator();
        flightPersistence ??=
            new FlightSessionPersistenceService(
                sessions,
                new EmptyCheckpointStore());
        var diagnostics = new DiagnosticBundleService(
            paths,
            settings,
            connection,
            telemetry,
            NullLogger<DiagnosticBundleService>.Instance);
        var databaseOptions = new OpenCareerDatabaseOptions(paths.DatabaseFile);
        var backup = new AppDataBackupService(
            paths,
            new SqliteDatabaseSnapshotService(databaseOptions),
            NullLogger<AppDataBackupService>.Instance);
        var restoreArchive = new SqliteBackupArchiveRestoreService(
            databaseOptions,
            new SqliteDatabaseSnapshotService(databaseOptions),
            NullLogger<SqliteBackupArchiveRestoreService>.Instance);
        var restore = new AppDataRestoreService(
            paths,
            restoreArchive,
            NullLogger<AppDataRestoreService>.Instance);
        var shell = new ShellOpenService(
            NullLogger<ShellOpenService>.Instance);
        var viewModel = new SettingsViewModel(
            settings,
            connection,
            telemetry,
            diagnostics,
            backup,
            restore,
            shell,
            paths,
            sessions,
            flightPersistence,
            inputBindings);

        return new TestContext(
            viewModel,
            settings,
            sessions,
            restore);
    }

    private static async Task CreateValidDatabaseAsync(string databasePath)
    {
        await using (var connection = new SqliteConnection(
                         $"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE restore_probe (id INTEGER PRIMARY KEY, value TEXT NOT NULL);"
                + " INSERT INTO restore_probe (value) VALUES ('valid');";
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
    }

    private static readonly DateTimeOffset Now =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlightSession Session(FlightSessionStatus status) =>
        FlightSession.Start(Now) with
        {
            Status = status,
            OperationState = status switch
            {
                FlightSessionStatus.Suspended => FlightOperationState.Accepted,
                FlightSessionStatus.Interrupted => FlightOperationState.Failed,
                FlightSessionStatus.Completed => FlightOperationState.Complete,
                FlightSessionStatus.Cancelled => FlightOperationState.Cancelled,
                _ => FlightOperationState.Accepted
            }
        };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record TestContext(
        SettingsViewModel ViewModel,
        FakeSettings Settings,
        FlightSessionCoordinator Sessions,
        AppDataRestoreService Restore);

    private sealed class FakeBindingSource(
        InputBindingDiscoverySnapshot snapshot) : IInputBindingDiscoverySource
    {
        public InputBindingDiscoverySnapshot Read(
            IReadOnlyList<InputBindingActionIdentity> actions) => snapshot;
    }

    private sealed class FakeSettings :
        IAppSettingsService,
        IAppSettingsRecoveryState
    {
        public AppPreferences Current { get; private set; } = AppPreferences.Default;
        public bool RequiresReset { get; set; }
        public int WriteCount { get; private set; }
        public Exception? UpdateFailure { get; set; }
        public Exception? ResetFailure { get; set; }
        public TaskCompletionSource? NextUpdateStarted { get; set; }
        public Task? NextUpdateRelease { get; set; }
        public TaskCompletionSource? NextResetStarted { get; set; }
        public Task? NextResetRelease { get; set; }

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task UpdateAsync(
            AppPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            if (UpdateFailure is not null)
                throw UpdateFailure;

            TaskCompletionSource? updateStarted = NextUpdateStarted;
            Task? updateRelease = NextUpdateRelease;
            NextUpdateStarted = null;
            NextUpdateRelease = null;
            updateStarted?.TrySetResult();
            if (updateRelease is not null)
                await updateRelease.WaitAsync(cancellationToken);

            WriteCount++;
            Current = preferences;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public async Task ResetAsync(CancellationToken cancellationToken = default)
        {
            if (ResetFailure is not null)
                throw ResetFailure;

            TaskCompletionSource? resetStarted = NextResetStarted;
            Task? resetRelease = NextResetRelease;
            NextResetStarted = null;
            NextResetRelease = null;
            resetStarted?.TrySetResult();
            if (resetRelease is not null)
                await resetRelease.WaitAsync(cancellationToken);

            WriteCount++;
            Current = AppPreferences.Default;
            RequiresReset = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; } =
            new(SimulatorConnectionState.Disconnected);

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTelemetrySource : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest => null;
    }

    private sealed class EmptyCheckpointStore : IFlightSessionCheckpointStore
    {
        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FlightSession?>(null);

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FailingCheckpointStore : IFlightSessionCheckpointStore
    {
        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic checkpoint read failure.");

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

}
