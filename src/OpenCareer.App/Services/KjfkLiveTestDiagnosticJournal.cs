using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenCareer.App.Services;

public sealed record KjfkLiveTestLaunchMetadata(
    Guid RunId,
    string SourceHead,
    bool? SourceDirty,
    DateTimeOffset LaunchedAtUtc,
    string RunMode,
    string BuildConfiguration,
    string BuildPlatform)
{
    public static KjfkLiveTestLaunchMetadata FromEnvironment()
    {
        Guid runId = Guid.TryParse(
            Environment.GetEnvironmentVariable("OPENCAREER_KJFK_RUN_ID"),
            out Guid parsedRunId)
            && parsedRunId != Guid.Empty
                ? parsedRunId
                : Guid.NewGuid();

        DateTimeOffset launchedAt = DateTimeOffset.TryParse(
            Environment.GetEnvironmentVariable("OPENCAREER_KJFK_LAUNCHED_AT_UTC"),
            out DateTimeOffset parsedLaunch)
                ? parsedLaunch.ToUniversalTime()
                : DateTimeOffset.UtcNow;

        bool? dirty = bool.TryParse(
            Environment.GetEnvironmentVariable("OPENCAREER_KJFK_SOURCE_DIRTY"),
            out bool parsedDirty)
                ? parsedDirty
                : null;

        return new(
            runId,
            ReadEnvironment("OPENCAREER_KJFK_SOURCE_HEAD", "unavailable"),
            dirty,
            launchedAt,
            ReadEnvironment("OPENCAREER_KJFK_RUN_MODE", "DirectDevelopmentLaunch"),
            ReadEnvironment("OPENCAREER_KJFK_BUILD_CONFIGURATION", "unknown"),
            ReadEnvironment("OPENCAREER_KJFK_BUILD_PLATFORM", "unknown"));
    }

    private static string ReadEnvironment(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}

/// <summary>
/// A bounded, crash-tolerant state-change journal for the isolated KJFK live-test profile.
/// It is inert for every normal OpenCareer profile.
/// </summary>
public sealed class KjfkLiveTestDiagnosticJournal
{
    public const int MaximumEventCount = 512;
    public const int MaximumRecentIssueCount = 20;

    private static readonly JsonSerializerOptions CompactJson = new();
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true
    };

    private readonly OpenCareerDataPaths _paths;
    private readonly KjfkLiveTestLaunchMetadata _launch;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _signatures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _latest =
        new(StringComparer.Ordinal);
    private readonly Queue<object> _recentIssues = new();

    private bool _started;
    private bool _completed;
    private int _eventCount;
    private int _droppedEventCount;
    private long _sequence;
    private DateTimeOffset _lastUpdatedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private string _outcome = "Running";
    private string? _failure;

    public KjfkLiveTestDiagnosticJournal(
        OpenCareerDataPaths paths,
        KjfkLiveTestLaunchMetadata launch,
        TimeProvider? timeProvider = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _timeProvider = timeProvider ?? TimeProvider.System;

        string runName = launch.RunId.ToString("N");
        RunDirectory = Path.Combine(
            paths.DiagnosticsFolder,
            "KjfkLiveTest",
            runName);
        ManifestFile = Path.Combine(RunDirectory, "launch-manifest.json");
        EventsFile = Path.Combine(RunDirectory, "state-events.jsonl");
        SummaryFile = Path.Combine(RunDirectory, "diagnostic-summary.json");
    }

    public bool IsEnabled => _paths.IsDevelopmentLiveTest;
    public string RunDirectory { get; }
    public string ManifestFile { get; }
    public string EventsFile { get; }
    public string SummaryFile { get; }

    public bool Start()
    {
        if (!IsEnabled)
            return false;

        lock (_gate)
        {
            if (_started)
                return true;

            try
            {
                _paths.EnsureDirectories();
                PrepareSafeRunDirectory();
                File.WriteAllText(EventsFile, string.Empty);

                Assembly assembly = typeof(KjfkLiveTestDiagnosticJournal).Assembly;
                string informationalVersion = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString()
                    ?? "unknown";

                WriteJsonAtomic(
                    ManifestFile,
                    new
                    {
                        schemaVersion = 1,
                        runId = _launch.RunId,
                        source = new
                        {
                            head = _launch.SourceHead,
                            dirty = _launch.SourceDirty
                        },
                        profile = new
                        {
                            name = OpenCareerDataProfile.KjfkLiveTest.ToString(),
                            dataRoot = _paths.Root,
                            normalDataExcluded = true,
                            runMode = _launch.RunMode
                        },
                        build = new
                        {
                            configuration = _launch.BuildConfiguration,
                            platform = _launch.BuildPlatform,
                            informationalVersion,
                            moduleVersionId = assembly.ManifestModule.ModuleVersionId
                        },
                        runtime = new
                        {
                            framework = RuntimeInformation.FrameworkDescription,
                            os = RuntimeInformation.OSDescription,
                            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString()
                        },
                        launchedAtUtc = _launch.LaunchedAtUtc
                    });

                _started = true;
                _lastUpdatedAtUtc = _timeProvider.GetUtcNow();
                WriteSummaryLocked();
                return true;
            }
            catch
            {
                // Diagnostics must never prevent a development flight from running.
                _started = false;
                return false;
            }
        }
    }

    public void Record(
        string category,
        string signature,
        object? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(signature);

        lock (_gate)
        {
            if (!_started || _completed)
                return;

            if (_signatures.TryGetValue(category, out string? previous)
                && string.Equals(previous, signature, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                JsonElement retained = JsonSerializer.SerializeToElement(
                    data,
                    CompactJson);
                _signatures[category] = signature;
                _latest[category] = retained;
                _lastUpdatedAtUtc = _timeProvider.GetUtcNow();

                if (_eventCount < MaximumEventCount)
                {
                    long sequence = ++_sequence;
                    string line = JsonSerializer.Serialize(
                        new
                        {
                            schemaVersion = 1,
                            sequence,
                            observedAtUtc = _lastUpdatedAtUtc,
                            category,
                            data = retained
                        },
                        CompactJson);

                    File.AppendAllText(
                        EventsFile,
                        line + Environment.NewLine);
                    _eventCount++;
                }
                else
                {
                    _droppedEventCount++;
                }

                WriteSummaryLocked();
            }
            catch
            {
                // Live-test diagnostics are observational and cannot affect gameplay.
            }
        }
    }

    public void RecordIssue(
        string category,
        string signature,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        lock (_gate)
        {
            if (!_started || _completed)
                return;

            try
            {
                while (_recentIssues.Count >= MaximumRecentIssueCount)
                    _recentIssues.Dequeue();

                _recentIssues.Enqueue(
                    new
                    {
                        observedAtUtc = _timeProvider.GetUtcNow(),
                        category,
                        detail = Truncate(detail, 600)
                    });
            }
            catch
            {
                return;
            }
        }

        Record(
            category,
            signature,
            new { detail = Truncate(detail, 600) });
    }

    public void Complete(
        string outcome,
        string? failure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        lock (_gate)
        {
            if (!_started || _completed)
                return;

            try
            {
                _completed = true;
                _outcome = outcome.Trim();
                _failure = string.IsNullOrWhiteSpace(failure)
                    ? null
                    : Truncate(failure, 600);
                _completedAtUtc = _timeProvider.GetUtcNow();
                _lastUpdatedAtUtc = _completedAtUtc.Value;
                WriteSummaryLocked();
            }
            catch
            {
                // The launcher still retains the isolated application log on failure.
            }
        }
    }

    private void WriteSummaryLocked()
    {
        WriteJsonAtomic(
            SummaryFile,
            new
            {
                schemaVersion = 1,
                runId = _launch.RunId,
                sourceHead = _launch.SourceHead,
                profile = OpenCareerDataProfile.KjfkLiveTest.ToString(),
                startedAtUtc = _launch.LaunchedAtUtc,
                lastUpdatedAtUtc = _lastUpdatedAtUtc,
                completedAtUtc = _completedAtUtc,
                outcome = _outcome,
                failure = _failure,
                recordedEventCount = _eventCount,
                droppedEventCount = _droppedEventCount,
                latest = _latest,
                recentIssues = _recentIssues.ToArray(),
                exclusions = new[]
                {
                    "No coordinates or raw telemetry stream.",
                    "No database, settings, credentials, or normal-profile data."
                }
            });
    }

    private static void WriteJsonAtomic(string destination, object value)
    {
        string temporary =
            destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(value, IndentedJson));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch
            {
                // Preserve the original diagnostic write result.
            }
        }
    }

    private void PrepareSafeRunDirectory()
    {
        string root = Path.GetFullPath(_paths.Root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string runDirectory = Path.GetFullPath(RunDirectory);
        if (!runDirectory.StartsWith(
                root + Path.DirectorySeparatorChar,
                PathComparison))
        {
            throw new InvalidOperationException(
                "KJFK diagnostic run directory is outside the isolated profile.");
        }

        string runsRoot = Path.GetDirectoryName(runDirectory)
            ?? throw new InvalidOperationException(
                "KJFK diagnostic run directory has no parent.");

        PrepareSafeDirectory(_paths.DiagnosticsFolder);
        PrepareSafeDirectory(runsRoot);

        RejectReparsePoint(runDirectory);
        if (Directory.Exists(runDirectory)
            && Directory.EnumerateFileSystemEntries(runDirectory).Any())
        {
            throw new InvalidDataException(
                "KJFK diagnostic run directory already contains data.");
        }

        PrepareSafeDirectory(runDirectory);
    }

    private static void PrepareSafeDirectory(string path)
    {
        RejectReparsePoint(path);
        Directory.CreateDirectory(path);
        RejectReparsePoint(path);
    }

    private static void RejectReparsePoint(string path)
    {
        if (Directory.Exists(path)
            && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "KJFK diagnostic output refused a redirected directory.");
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : value[..maximumLength];
}
