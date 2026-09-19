using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.App.Services;

public sealed class DiagnosticBundleService(
    OpenCareerDataPaths paths,
    IAppSettingsService settings,
    ISimulatorConnection connection,
    ISimulatorTelemetrySource telemetrySource,
    ILogger<DiagnosticBundleService> logger)
{
    public async Task<SettingsActionResult> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();

        string fileName = $"opencareer-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip";
        string destination = Path.Combine(paths.DiagnosticsFolder, fileName);
        string temporary = destination + ".tmp";

        try
        {
            await using FileStream output = File.Create(temporary);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

            SimulatorConnectionSnapshot connectionSnapshot = connection.Current;
            AircraftTelemetrySnapshot? telemetry = telemetrySource.Latest;

            ZipArchiveEntry reportEntry = archive.CreateEntry("diagnostic-report.json");
            await using (Stream reportStream = reportEntry.Open())
            {
                await JsonSerializer.SerializeAsync(
                    reportStream,
                    new
                    {
                        schemaVersion = 1,
                        createdAt = DateTimeOffset.Now,
                        application = new
                        {
                            version = typeof(DiagnosticBundleService).Assembly.GetName().Version?.ToString() ?? "unknown",
                            runtime = RuntimeInformation.FrameworkDescription,
                            os = RuntimeInformation.OSDescription,
                            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString()
                        },
                        simulator = new
                        {
                            state = connectionSnapshot.State.ToString(),
                            issue = connectionSnapshot.Issue.ToString(),
                            name = connectionSnapshot.Simulator?.Name,
                            applicationVersion = connectionSnapshot.Simulator?.ApplicationVersion.ToString(),
                            simConnectVersion = connectionSnapshot.Simulator?.SimConnectVersion.ToString()
                        },
                        telemetry = telemetry is null ? null : new
                        {
                            telemetry.Timestamp,
                            telemetry.AltitudeMslFeet,
                            telemetry.AltitudeAglFeet,
                            telemetry.IndicatedAirspeedKnots,
                            telemetry.GroundSpeedKnots,
                            telemetry.VerticalSpeedFeetPerMinute,
                            telemetry.HeadingDegrees,
                            telemetry.OnGround,
                            telemetry.ParkingBrakeSet,
                            telemetry.EnginesRunning,
                            telemetry.FlapsPositionPercent,
                            telemetry.GearDown,
                            telemetry.Paused,
                            telemetry.SlewActive
                        },
                        preferences = settings.Current,
                        notes = new[]
                        {
                            "Coordinates are intentionally excluded from the diagnostic report.",
                            "OpenCareer AI/cloud services are not authoritative for career state.",
                            "Do not add API keys, credentials or secrets to diagnostic logging."
                        }
                    },
                    new JsonSerializerOptions { WriteIndented = true },
                    cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(paths.LogFile))
                await AddFileAsync(archive, paths.LogFile, "logs/opencareer.log", cancellationToken)
                    .ConfigureAwait(false);

            if (File.Exists(paths.SettingsFile))
                await AddFileAsync(archive, paths.SettingsFile, "settings.json", cancellationToken)
                    .ConfigureAwait(false);

            if (File.Exists(paths.TutorialPreferencesFile))
                await AddFileAsync(
                    archive,
                    paths.TutorialPreferencesFile,
                    "ui-preferences.json",
                    cancellationToken).ConfigureAwait(false);

            archive.Dispose();
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            await output.DisposeAsync().ConfigureAwait(false);

            File.Move(temporary, destination, overwrite: false);
            logger.LogInformation("Created diagnostic bundle at {Path}.", destination);

            return new SettingsActionResult(
                true,
                $"Diagnostic bundle created: {fileName}",
                destination);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(temporary);
            logger.LogError(ex, "Failed to create OpenCareer diagnostic bundle.");
            return new SettingsActionResult(false, $"Diagnostic export failed: {ex.Message}");
        }
    }

    private static async Task AddFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream destination = entry.Open();
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Preserve the original diagnostic-export error.
        }
    }
}
