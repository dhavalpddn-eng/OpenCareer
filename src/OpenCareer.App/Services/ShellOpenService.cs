using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OpenCareer.App.Services;

public sealed class ShellOpenService(ILogger<ShellOpenService> logger)
{
    public SettingsActionResult OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });

            return new SettingsActionResult(true, $"Opened {path}", path);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogWarning(ex, "Unable to open folder {Path}.", path);
            return new SettingsActionResult(false, $"Could not open folder: {ex.Message}");
        }
    }
}
