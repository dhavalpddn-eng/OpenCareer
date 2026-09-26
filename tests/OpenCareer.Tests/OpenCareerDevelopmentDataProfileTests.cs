using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class OpenCareerDevelopmentDataProfileTests
{
    [Fact]
    public void DefaultLaunchUsesOnlyNormalDataRoot()
    {
        using var root = new TemporaryDirectory();

        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve([], root.Path);

        Assert.Equal(OpenCareerDataProfile.Normal, profile.Paths.Profile);
        Assert.False(profile.ResetRequested);
        Assert.Equal(
            System.IO.Path.Combine(root.Path, "OpenCareer", "opencareer.db"),
            profile.Paths.DatabaseFile);
        Assert.DoesNotContain("LiveTests", profile.Paths.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KJFKProfileUsesFixedSiblingRootAndNeverNormalDatabase()
    {
        using var root = new TemporaryDirectory();

        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);

        Assert.Equal(OpenCareerDataProfile.KjfkLiveTest, profile.Paths.Profile);
        Assert.True(profile.Paths.IsDevelopmentLiveTest);
        Assert.Equal(
            System.IO.Path.Combine(root.Path, "OpenCareer.LiveTests", "KJFK", "opencareer.db"),
            profile.Paths.DatabaseFile);
        Assert.NotEqual(
            System.IO.Path.Combine(root.Path, "OpenCareer", "opencareer.db"),
            profile.Paths.DatabaseFile);
    }

    [Fact]
    public async Task CleanResetRemovesAllStaleTestStateAndPreservesNormalData()
    {
        using var root = new TemporaryDirectory();
        var normal = new OpenCareerDataPaths(System.IO.Path.Combine(root.Path, "OpenCareer"));
        normal.EnsureDirectories();
        await File.WriteAllTextAsync(normal.SettingsFile, "normal-settings");
        await CreateStateDatabaseAsync(normal.DatabaseFile, "normal-contract");

        OpenCareerDataProfileSelection retained =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        retained.Prepare();
        await File.WriteAllTextAsync(retained.Paths.SettingsFile, "test-settings");
        await File.WriteAllTextAsync(retained.Paths.TutorialPreferencesFile, "test-tutorial");
        await File.WriteAllTextAsync(retained.Paths.PendingDatabaseRestoreFile, "stale-restore");
        await File.WriteAllTextAsync(retained.Paths.LogFile, "stale-log");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(retained.Paths.BackupsFolder, "stale.zip"),
            "stale-backup");
        await CreateStateDatabaseAsync(retained.Paths.DatabaseFile, "stale-contract");

        OpenCareerDataProfileSelection clean =
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path);
        clean.Prepare();

        Assert.True(Directory.Exists(clean.Paths.Root));
        Assert.True(File.Exists(clean.Paths.ProfileMarkerFile));
        Assert.False(File.Exists(clean.Paths.DatabaseFile));
        Assert.False(File.Exists(clean.Paths.SettingsFile));
        Assert.False(File.Exists(clean.Paths.TutorialPreferencesFile));
        Assert.False(File.Exists(clean.Paths.PendingDatabaseRestoreFile));
        Assert.False(File.Exists(clean.Paths.LogFile));
        Assert.Empty(Directory.EnumerateFiles(clean.Paths.BackupsFolder));
        Assert.Equal("normal-settings", await File.ReadAllTextAsync(normal.SettingsFile));
        Assert.Equal("normal-contract", await ReadStateAsync(normal.DatabaseFile));
    }

    [Fact]
    public async Task LaunchWithoutResetPreservesRestartRecoveryState()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection first =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        first.Prepare();
        await CreateStateDatabaseAsync(first.Paths.DatabaseFile, "recover-me");

        OpenCareerDataProfileSelection restarted =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        restarted.Prepare();

        Assert.Equal(first.Paths.Root, restarted.Paths.Root);
        Assert.Equal("recover-me", await ReadStateAsync(restarted.Paths.DatabaseFile));
    }

    [Fact]
    public async Task ProductionRecoveryStateIsRetainedUntilAnExplicitCleanReset()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection first =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        first.Prepare();

        Guid contractId = Guid.Parse("b6100000-0000-0000-0000-000000000001");
        Guid sessionId = Guid.Parse("b6100000-0000-0000-0000-000000000002");
        const string aircraftId = "msfs-title:Cessna 172 Skyhawk";
        const string reservationId = "contract:b6100000-0000-0000-0000-000000000001:aircraft-v1";
        DateTimeOffset now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        await SeedProductionRecoveryStateAsync(
            first.Paths.DatabaseFile,
            contractId,
            sessionId,
            aircraftId,
            reservationId,
            now);

        OpenCareerDataProfileSelection recoveryLaunch =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        recoveryLaunch.Prepare();

        await AssertProductionRecoveryStateAsync(
            recoveryLaunch.Paths.DatabaseFile,
            contractId,
            sessionId,
            aircraftId,
            reservationId,
            expectedPresent: true);

        OpenCareerDataProfileSelection cleanLaunch =
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path);
        cleanLaunch.Prepare();

        Assert.False(File.Exists(cleanLaunch.Paths.DatabaseFile));
        await AssertProductionRecoveryStateAsync(
            cleanLaunch.Paths.DatabaseFile,
            contractId,
            sessionId,
            aircraftId,
            reservationId,
            expectedPresent: false);
    }

    [Fact]
    public void ResetWithoutExactDevelopmentProfileFailsClosed()
    {
        using var root = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ResetArgument],
                root.Path));

        Assert.Throws<ArgumentException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument + "=normal"],
                root.Path));
    }

    [Fact]
    public void MissingOrCorruptDevelopmentMarkerRefusesReset()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        Directory.CreateDirectory(profile.Paths.Root);
        File.WriteAllText(profile.Paths.DatabaseFile, "do-not-delete-without-marker");

        Assert.Throws<InvalidDataException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path).Prepare());
        Assert.True(File.Exists(profile.Paths.DatabaseFile));

        File.WriteAllText(profile.Paths.ProfileMarkerFile, "not-an-opencareer-profile");
        Assert.Throws<InvalidDataException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path).Prepare());
        Assert.True(File.Exists(profile.Paths.DatabaseFile));
    }

    [Fact]
    public void LiveTestScriptDefaultsToCleanProfileAndSupportsRecoveryMode()
    {
        string script = File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "UiContracts", "run-kjfk-live-test.ps1"));

        Assert.Contains("--development-kjfk-live-test", script, StringComparison.Ordinal);
        Assert.Contains("--reset-development-kjfk-live-test", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$PreserveState", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $PreserveState)", script, StringComparison.Ordinal);
        Assert.Contains("OpenCareer.LiveTests\\KJFK", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalProfileNeverCreatesKjfkDiagnosticArtifacts()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection normal =
            OpenCareerDataProfileSelection.Resolve([], root.Path);
        var journal = new KjfkLiveTestDiagnosticJournal(
            normal.Paths,
            CreateLaunchMetadata());

        Assert.False(journal.Start());
        journal.Record(
            "simconnect",
            "Connected",
            new { state = "Connected" });
        journal.Complete("NormalShutdown");

        Assert.False(Directory.Exists(journal.RunDirectory));
        Assert.False(File.Exists(journal.ManifestFile));
        Assert.False(File.Exists(journal.EventsFile));
        Assert.False(File.Exists(journal.SummaryFile));
    }

    [Fact]
    public void KjfkDiagnosticJournalCapturesExactLaunchAndDeduplicatedBoundedState()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        profile.Prepare();

        KjfkLiveTestLaunchMetadata launch = CreateLaunchMetadata();
        var journal = new KjfkLiveTestDiagnosticJournal(
            profile.Paths,
            launch);

        Assert.True(journal.Start());
        journal.Record(
            "simconnect",
            "Connected:None",
            new { state = "Connected", issue = "None" });
        journal.Record(
            "simconnect",
            "Connected:None",
            new { state = "Connected", issue = "None" });
        journal.Record(
            "simconnect",
            "Reconnecting:ConnectionLost",
            new { state = "Reconnecting", issue = "ConnectionLost" });
        journal.RecordIssue(
            "airport-facility",
            "KALB:InvalidResponse",
            "SimConnect airport facility response for KALB was malformed.");
        journal.Complete("NormalShutdown");

        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(journal.ManifestFile));
        JsonElement manifestRoot = manifest.RootElement;
        Assert.Equal(
            launch.RunId,
            manifestRoot.GetProperty("runId").GetGuid());
        Assert.Equal(
            launch.SourceHead,
            manifestRoot.GetProperty("source").GetProperty("head").GetString());
        Assert.True(
            manifestRoot.GetProperty("source").GetProperty("dirty").GetBoolean());
        Assert.Equal(
            "KjfkLiveTest",
            manifestRoot.GetProperty("profile").GetProperty("name").GetString());
        Assert.True(
            manifestRoot.GetProperty("profile").GetProperty("normalDataExcluded").GetBoolean());
        Assert.Equal(
            launch.RunMode,
            manifestRoot.GetProperty("profile").GetProperty("runMode").GetString());
        Assert.Equal(
            launch.BuildConfiguration,
            manifestRoot.GetProperty("build").GetProperty("configuration").GetString());
        Assert.Equal(
            launch.BuildPlatform,
            manifestRoot.GetProperty("build").GetProperty("platform").GetString());
        Assert.Equal(
            launch.LaunchedAtUtc,
            manifestRoot.GetProperty("launchedAtUtc").GetDateTimeOffset());

        string[] events = File.ReadAllLines(journal.EventsFile);
        Assert.Equal(3, events.Length);
        Assert.Equal(
            new long[] { 1, 2, 3 },
            events.Select(line => JsonDocument.Parse(line).RootElement
                .GetProperty("sequence").GetInt64()).ToArray());

        using JsonDocument summary = JsonDocument.Parse(
            File.ReadAllText(journal.SummaryFile));
        Assert.Equal(
            "NormalShutdown",
            summary.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            3,
            summary.RootElement.GetProperty("recordedEventCount").GetInt32());
        Assert.Equal(
            0,
            summary.RootElement.GetProperty("droppedEventCount").GetInt32());
        Assert.Equal(
            "Reconnecting",
            summary.RootElement.GetProperty("latest")
                .GetProperty("simconnect")
                .GetProperty("state")
                .GetString());
        Assert.Single(
            summary.RootElement.GetProperty("recentIssues").EnumerateArray());

        string allArtifacts =
            File.ReadAllText(journal.ManifestFile)
            + File.ReadAllText(journal.EventsFile)
            + File.ReadAllText(journal.SummaryFile);
        Assert.DoesNotContain("opencareer.db", allArtifacts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings.json", allArtifacts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", allArtifacts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", allArtifacts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KjfkDiagnosticJournalBoundsStateHistoryButRetainsLatestState()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        profile.Prepare();

        var journal = new KjfkLiveTestDiagnosticJournal(
            profile.Paths,
            CreateLaunchMetadata());
        Assert.True(journal.Start());

        int total = KjfkLiveTestDiagnosticJournal.MaximumEventCount + 7;
        for (int index = 0; index < total; index++)
        {
            journal.Record(
                "jobs-readiness",
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new { selectedAircraftId = $"aircraft-{index}" });
        }

        journal.Complete("NormalShutdown");

        Assert.Equal(
            KjfkLiveTestDiagnosticJournal.MaximumEventCount,
            File.ReadAllLines(journal.EventsFile).Length);
        using JsonDocument summary = JsonDocument.Parse(
            File.ReadAllText(journal.SummaryFile));
        Assert.Equal(
            7,
            summary.RootElement.GetProperty("droppedEventCount").GetInt32());
        Assert.Equal(
            $"aircraft-{total - 1}",
            summary.RootElement.GetProperty("latest")
                .GetProperty("jobs-readiness")
                .GetProperty("selectedAircraftId")
                .GetString());
    }

    [Fact]
    public void KjfkDiagnosticJournalRefusesToOverwriteAnExistingRunDirectory()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        profile.Prepare();

        var journal = new KjfkLiveTestDiagnosticJournal(
            profile.Paths,
            CreateLaunchMetadata());
        Directory.CreateDirectory(journal.RunDirectory);
        string sentinel = System.IO.Path.Combine(journal.RunDirectory, "existing.txt");
        File.WriteAllText(sentinel, "preserve");

        Assert.False(journal.Start());
        Assert.Equal("preserve", File.ReadAllText(sentinel));
        Assert.False(File.Exists(journal.ManifestFile));
        Assert.False(File.Exists(journal.EventsFile));
        Assert.False(File.Exists(journal.SummaryFile));
    }

    [Fact]
    public void KjfkDiagnosticSummaryRetainsLatestStateBeforeGracefulCompletion()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        profile.Prepare();

        var journal = new KjfkLiveTestDiagnosticJournal(
            profile.Paths,
            CreateLaunchMetadata());
        Assert.True(journal.Start());
        journal.Record(
            "jobs-readiness",
            "c172-ready",
            new
            {
                selectedAircraftId = "msfs-title:Cessna 172 Skyhawk",
                canStart = true
            });

        using JsonDocument summary = JsonDocument.Parse(
            File.ReadAllText(journal.SummaryFile));
        Assert.Equal(
            "Running",
            summary.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            "msfs-title:Cessna 172 Skyhawk",
            summary.RootElement.GetProperty("latest")
                .GetProperty("jobs-readiness")
                .GetProperty("selectedAircraftId")
                .GetString());
    }

    [Fact]
    public void LiveTestDiagnosticsAreDevOnlyAndLauncherAlwaysFinalizesAllowlistedBundle()
    {
        string app = File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        string service = File.ReadAllText(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "UiContracts",
                "KjfkLiveTestDiagnosticsService.cs"));
        string script = File.ReadAllText(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "UiContracts",
                "run-kjfk-live-test.ps1"));

        Assert.Contains("if (dataPaths.IsDevelopmentLiveTest)", app, StringComparison.Ordinal);
        Assert.Contains("_liveTestDiagnostics.Start()", app, StringComparison.Ordinal);
        Assert.Contains("CompleteAsync(\"NormalShutdown\")", app, StringComparison.Ordinal);
        Assert.Contains("_flightSessions.SessionChanged", service, StringComparison.Ordinal);
        Assert.Contains("_jobs.PropertyChanged", service, StringComparison.Ordinal);
        Assert.Contains("FindByReservationIdAsync", service, StringComparison.Ordinal);
        Assert.Contains("airport-facility", service, StringComparison.Ordinal);

        Assert.Contains("rev-parse --verify HEAD", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPENCAREER_KJFK_SOURCE_HEAD", script, StringComparison.Ordinal);
        Assert.Contains("OPENCAREER_KJFK_RUN_ID", script, StringComparison.Ordinal);
        Assert.Contains("finally", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("launch-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("state-events.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("diagnostic-summary.json", script, StringComparison.Ordinal);
        Assert.Contains("run-result.json", script, StringComparison.Ordinal);
        Assert.Contains("opencareer.log", script, StringComparison.Ordinal);
        Assert.Contains("diagnosticSummaryFinalized", script, StringComparison.Ordinal);
        Assert.Contains("$processExitCode = 2", script, StringComparison.Ordinal);
        Assert.Contains("FileAttributes]::ReparsePoint", script, StringComparison.Ordinal);
        int markerInitialization = script.IndexOf(
            "Initialize-KjfkLiveTestProfileMarker -ProfileRoot $profileRoot",
            StringComparison.Ordinal);
        int logRootGuard = script.IndexOf(
            "Assert-NotReparsePoint -Path $logRootBeforeLaunch",
            StringComparison.Ordinal);
        int profileContentGuard = script.IndexOf(
            "Assert-NoReparsePointsUnderRoot -Root $profileRoot",
            StringComparison.Ordinal);
        int applicationLaunch = script.IndexOf(
            "& $dotnetCommand.Path @dotnetArgs",
            StringComparison.Ordinal);
        Assert.True(markerInitialization >= 0);
        Assert.True(logRootGuard > markerInitialization);
        Assert.True(profileContentGuard > logRootGuard);
        Assert.True(applicationLaunch > profileContentGuard);
        Assert.Contains("FileAttributes.ReparsePoint", File.ReadAllText(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "UiContracts",
                "KjfkLiveTestDiagnosticJournal.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("opencareer.db", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings.json", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveTestLauncherParsesWithWindowsPowerShell()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string script = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "UiContracts",
            "run-kjfk-live-test.ps1");
        PowerShellProcessResult result = await RunWindowsPowerShellAsync(
            "$tokens=$null; $errors=$null; " +
            "[System.Management.Automation.Language.Parser]::ParseFile(" +
            "$env:OPENCAREER_SCRIPT_TO_PARSE,[ref]$tokens,[ref]$errors) | Out-Null; " +
            "if ($errors.Count -ne 0) { $errors | Out-String | Write-Error; exit 1 }",
            new Dictionary<string, string>
            {
                ["OPENCAREER_SCRIPT_TO_PARSE"] = script
            });

        Assert.True(
            result.ExitCode == 0,
            $"Windows PowerShell parser rejected the launcher: {result.Error}");
    }

    [Fact]
    public async Task LiveTestLauncherFunctionsRejectRecursiveDirectoryJunction()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var root = new TemporaryDirectory();
        string profileRoot = System.IO.Path.Combine(root.Path, "profile");
        string logsRoot = System.IO.Path.Combine(profileRoot, "Logs");
        string junction = System.IO.Path.Combine(logsRoot, "nested");
        string externalRoot = System.IO.Path.Combine(root.Path, "external");
        string sentinel = System.IO.Path.Combine(externalRoot, "sentinel.txt");
        string launchMarker = System.IO.Path.Combine(root.Path, "dotnet-launched.txt");
        Directory.CreateDirectory(logsRoot);
        Directory.CreateDirectory(externalRoot);
        File.WriteAllText(sentinel, "preserve");

        string script = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "UiContracts",
            "run-kjfk-live-test.ps1");

        try
        {
            PowerShellProcessResult result = await RunWindowsPowerShellAsync(
                LoadLauncherFunctionsCommand(
                    """
                    $junction = Join-Path $env:OPENCAREER_TEST_LOGS_ROOT "nested"
                    try {
                        $created = New-Item -ItemType Junction -Path $junction -Target $env:OPENCAREER_TEST_EXTERNAL_ROOT
                        if (($created.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
                            throw "The test junction was not created as a reparse point."
                        }

                        $rejected = $false
                        try {
                            Assert-NoReparsePointsUnderRoot -Root $env:OPENCAREER_TEST_PROFILE_ROOT
                        }
                        catch {
                            if (-not $_.Exception.Message.StartsWith(
                                "KJFK live-test profile content must not be redirected:",
                                [System.StringComparison]::Ordinal)) {
                                throw
                            }

                            $rejected = $true
                        }

                        if (-not $rejected) {
                            [System.IO.File]::WriteAllText(
                                $env:OPENCAREER_TEST_LAUNCH_MARKER,
                                "launched")
                            throw "The recursive reparse guard allowed launcher execution."
                        }

                        if (-not [string]::Equals(
                            [System.IO.File]::ReadAllText($env:OPENCAREER_TEST_SENTINEL),
                            "preserve",
                            [System.StringComparison]::Ordinal)) {
                            throw "The redirected target was modified."
                        }
                    }
                    finally {
                        if (Test-Path -LiteralPath $junction) {
                            [System.IO.Directory]::Delete($junction)
                        }
                    }
                    """),
                new Dictionary<string, string>
                {
                    ["OPENCAREER_SCRIPT_TO_PARSE"] = script,
                    ["OPENCAREER_TEST_PROFILE_ROOT"] = profileRoot,
                    ["OPENCAREER_TEST_LOGS_ROOT"] = logsRoot,
                    ["OPENCAREER_TEST_EXTERNAL_ROOT"] = externalRoot,
                    ["OPENCAREER_TEST_SENTINEL"] = sentinel,
                    ["OPENCAREER_TEST_LAUNCH_MARKER"] = launchMarker
                });

            Assert.True(
                result.ExitCode == 0,
                $"The recursive reparse guard test failed. Output: {result.Output} Error: {result.Error}");
            Assert.False(File.Exists(launchMarker));
            Assert.Equal("preserve", File.ReadAllText(sentinel));
        }
        finally
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task LiveTestLauncherFinalizerCreatesSucceededAllowlistedBundle()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var root = new TemporaryDirectory();
        string profileRoot = System.IO.Path.Combine(root.Path, "profile");
        string runId = "d1a60000000000000000000000000001";
        string runRoot = System.IO.Path.Combine(
            profileRoot,
            "Diagnostics",
            "KjfkLiveTest",
            runId);
        string logsRoot = System.IO.Path.Combine(profileRoot, "Logs");
        Directory.CreateDirectory(runRoot);
        Directory.CreateDirectory(logsRoot);
        File.WriteAllText(
            System.IO.Path.Combine(profileRoot, ".opencareer-development-profile"),
            "OpenCareer DEVELOPMENT / TEST profile: KJFK v1");
        File.WriteAllText(
            System.IO.Path.Combine(runRoot, "launch-manifest.json"),
            JsonSerializer.Serialize(new
            {
                runId = Guid.ParseExact(runId, "N")
            }));
        File.WriteAllText(
            System.IO.Path.Combine(runRoot, "state-events.jsonl"),
            "{}" + Environment.NewLine);
        File.WriteAllText(
            System.IO.Path.Combine(runRoot, "diagnostic-summary.json"),
            JsonSerializer.Serialize(new
            {
                runId = Guid.ParseExact(runId, "N"),
                outcome = "NormalShutdown",
                completedAtUtc = new DateTimeOffset(
                    2026,
                    9,
                    26,
                    16,
                    0,
                    0,
                    TimeSpan.Zero)
            }));
        File.WriteAllText(
            System.IO.Path.Combine(logsRoot, "opencareer.log"),
            "current log");
        File.WriteAllText(
            System.IO.Path.Combine(logsRoot, "opencareer.log.1"),
            "previous log");
        File.WriteAllText(
            System.IO.Path.Combine(profileRoot, "opencareer.db"),
            "must not be bundled");
        File.WriteAllText(
            System.IO.Path.Combine(profileRoot, "settings.json"),
            "must not be bundled");
        File.WriteAllText(
            System.IO.Path.Combine(runRoot, "not-allowlisted.txt"),
            "must not be bundled");

        string script = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "UiContracts",
            "run-kjfk-live-test.ps1");
        PowerShellProcessResult result = await RunWindowsPowerShellAsync(
            LoadLauncherFunctionsCommand(
                """
                $profileMarkerContents = "OpenCareer DEVELOPMENT / TEST profile: KJFK v1"
                $arguments = @{
                    ProfileRoot = $env:OPENCAREER_TEST_PROFILE_ROOT
                    RunId = $env:OPENCAREER_TEST_RUN_ID
                    RunMode = "CleanReset"
                    LaunchedAtUtc = "2026-09-26T15:00:00.0000000+00:00"
                    SourceHead = "1faff4eacdfabfdb90bb7e8ad2afbba3c147544e"
                    SourceDirty = $true
                    BuildConfiguration = "Release"
                    BuildPlatform = "x64"
                    ExitCode = 0
                }
                $result = Complete-KjfkLiveTestRun @arguments
                if (-not [string]::Equals(
                    [string]$result.Status,
                    "Succeeded",
                    [System.StringComparison]::Ordinal)) {
                    throw "The finalizer did not report Succeeded."
                }
                """),
            new Dictionary<string, string>
            {
                ["OPENCAREER_SCRIPT_TO_PARSE"] = script,
                ["OPENCAREER_TEST_PROFILE_ROOT"] = profileRoot,
                ["OPENCAREER_TEST_RUN_ID"] = runId
            });

        Assert.True(
            result.ExitCode == 0,
            $"The launcher finalizer test failed. Output: {result.Output} Error: {result.Error}");

        string resultFile = System.IO.Path.Combine(runRoot, "run-result.json");
        using JsonDocument runResult = JsonDocument.Parse(File.ReadAllText(resultFile));
        Assert.Equal(
            "Succeeded",
            runResult.RootElement.GetProperty("status").GetString());
        Assert.True(
            runResult.RootElement.GetProperty("artifacts")
                .GetProperty("launchManifestMatchesRun")
                .GetBoolean());
        Assert.True(
            runResult.RootElement.GetProperty("artifacts")
                .GetProperty("diagnosticSummaryFinalized")
                .GetBoolean());

        string zipFile = System.IO.Path.Combine(
            profileRoot,
            "Diagnostics",
            $"OpenCareer-kjfk-live-test-{runId}.zip");
        using System.IO.Compression.ZipArchive archive =
            System.IO.Compression.ZipFile.OpenRead(zipFile);
        string[] entries = archive.Entries
            .Select(static entry => entry.FullName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "diagnostic-summary.json",
                "launch-manifest.json",
                "logs/opencareer.log",
                "logs/opencareer.log.1",
                "run-result.json",
                "state-events.jsonl"
            },
            entries);
    }

    private static string LoadLauncherFunctionsCommand(string command) =>
        """
        $ErrorActionPreference = "Stop"
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $env:OPENCAREER_SCRIPT_TO_PARSE,
            [ref]$tokens,
            [ref]$errors)
        if ($errors.Count -ne 0) {
            throw ($errors | Out-String)
        }
        $definitions = $ast.FindAll(
            { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] },
            $true) | ForEach-Object { $_.Extent.Text }
        . ([scriptblock]::Create(($definitions -join [Environment]::NewLine)))
        """ + Environment.NewLine + command;

    private static async Task<PowerShellProcessResult> RunWindowsPowerShellAsync(
        string command,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach ((string name, string value) in environment)
            start.Environment[name] = value;
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows PowerShell did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

        try
        {
            await process.WaitForExitAsync().WaitAsync(effectiveTimeout);
        }
        catch (TimeoutException exception)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(
                $"Windows PowerShell did not exit within {effectiveTimeout}.",
                exception);
        }

        return new(
            process.ExitCode,
            await output,
            await error);
    }

    private sealed record PowerShellProcessResult(
        int ExitCode,
        string Output,
        string Error);

    private static KjfkLiveTestLaunchMetadata CreateLaunchMetadata() =>
        new(
            Guid.Parse("d1a60000-0000-0000-0000-000000000001"),
            "1faff4eacdfabfdb90bb7e8ad2afbba3c147544e",
            true,
            new DateTimeOffset(2026, 9, 26, 15, 0, 0, TimeSpan.Zero),
            "CleanReset",
            "Release",
            "x64");

    private static async Task CreateStateDatabaseAsync(string path, string value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE persisted_test_state (value TEXT NOT NULL);" +
            "INSERT INTO persisted_test_state (value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadStateAsync(string path)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM persisted_test_state LIMIT 1;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SeedProductionRecoveryStateAsync(
        string databasePath,
        Guid contractId,
        Guid sessionId,
        string aircraftId,
        string reservationId,
        DateTimeOffset now)
    {
        var options = new OpenCareerDatabaseOptions(databasePath);
        var contracts = new SqliteJobContractStore(
            options,
            NullLogger<SqliteJobContractStore>.Instance);
        JobContract offered = CreateContract(contractId, now);
        await contracts.CreateJobContractAsync(offered);

        JobContract accepted = offered with
        {
            Status = ContractStatus.Accepted,
            AcceptedAt = now.AddMinutes(1)
        };
        accepted.Validate();
        await contracts.UpdateJobContractAsync(accepted, expectedVersion: 0);

        JobContract inProgress = accepted with
        {
            Status = ContractStatus.InProgress,
            StartedAt = now.AddMinutes(2)
        };
        inProgress.Validate();
        await contracts.UpdateJobContractAsync(inProgress, expectedVersion: 1);

        var reservations = new SqliteAircraftAvailabilityStore(databasePath);
        Assert.Equal(
            AircraftReservationAcquireResult.Acquired,
            await reservations.TryReserveAsync(aircraftId, reservationId));

        var checkpoints = new SqliteFlightSessionCheckpointStore(databasePath);
        await checkpoints.SaveAsync(
            FlightSession.Start(
                now.AddMinutes(2),
                contractId,
                sessionId));

        var boards = new SqliteJobBoardStateStore(
            options,
            NullLogger<SqliteJobBoardStateStore>.Instance);
        await boards.SaveAsync(JobBoardState.Empty("KJFK", now));
    }

    private static async Task AssertProductionRecoveryStateAsync(
        string databasePath,
        Guid contractId,
        Guid sessionId,
        string aircraftId,
        string reservationId,
        bool expectedPresent)
    {
        var options = new OpenCareerDatabaseOptions(databasePath);
        var contracts = new SqliteJobContractStore(
            options,
            NullLogger<SqliteJobContractStore>.Instance);
        var reservations = new SqliteAircraftAvailabilityStore(databasePath);
        var checkpoints = new SqliteFlightSessionCheckpointStore(databasePath);
        var boards = new SqliteJobBoardStateStore(
            options,
            NullLogger<SqliteJobBoardStateStore>.Instance);

        PersistedJobContract? contract = await contracts.ReadJobContractAsync(contractId);
        AircraftReservationOwnership? reservation =
            await reservations.FindByReservationIdAsync(reservationId);
        FlightSession? checkpoint = await checkpoints.LoadAsync();
        JobBoardState? board = await boards.GetAsync("KJFK");

        try
        {
            if (expectedPresent)
            {
                Assert.NotNull(contract);
                Assert.Equal(ContractStatus.InProgress, contract.Contract.Status);
                Assert.Equal(aircraftId, reservation?.CanonicalAircraftId);
                Assert.Equal(sessionId, checkpoint?.SessionId);
                Assert.NotNull(board);
            }
            else
            {
                Assert.Null(contract);
                Assert.Null(reservation);
                Assert.Null(checkpoint);
                Assert.Null(board);
            }
        }
        finally
        {
            // A profile launch is a process boundary in production. Clear only
            // this test database's idle shared pool before reset or teardown.
            using var pooledConnection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared
                }.ToString());
            SqliteConnection.ClearPool(pooledConnection);
        }
    }

    private static JobContract CreateContract(Guid contractId, DateTimeOffset offeredAt) =>
        new(
            ContractId: contractId,
            EmployerId: null,
            Kind: ContractKind.Other,
            ServiceTrack: ServiceTrack.IndependentContract,
            OriginIcao: "KJFK",
            DestinationIcao: "KJFK",
            Compensation: new ContractCompensation(
                CompensationModel.MissionFee,
                GrossCustomerRevenue: 0m,
                PilotCompensation: 0m,
                EmployerCoversFuel: true,
                EmployerCoversMaintenance: true,
                EmployerCoversAirportFees: true),
            OfferedAt: offeredAt,
            MustStartBy: offeredAt.AddHours(1),
            MustCompleteBy: offeredAt.AddHours(2),
            AircraftRequirements: new AircraftMissionRequirements(
                AircraftCapability.None,
                AircraftAccess.Civilian,
                MinimumPayloadPounds: 0,
                MinimumRangeNauticalMiles: 0,
                MinimumSeats: 0),
            ReputationReward: 0,
            ReputationPenalty: 0,
            MarketId: "development:kjfk-live-test");

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
