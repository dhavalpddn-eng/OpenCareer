namespace OpenCareer.App.Services;

public sealed class OpenCareerDataPaths
{
    public OpenCareerDataPaths(string? rootOverride = null)
    {
        string root = rootOverride ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCareer");

        Root = Path.GetFullPath(root);
        SettingsFile = Path.Combine(Root, "settings.json");
        TutorialPreferencesFile = Path.Combine(Root, "ui-preferences.json");
        DatabaseFile = Path.Combine(Root, "opencareer.db");
        PendingDatabaseRestoreFile = Path.Combine(Root, "pending-database-restore.db");
        LogsFolder = Path.Combine(Root, "Logs");
        LogFile = Path.Combine(LogsFolder, "opencareer.log");
        BackupsFolder = Path.Combine(Root, "Backups");
        DiagnosticsFolder = Path.Combine(Root, "Diagnostics");
    }

    public string Root { get; }
    public string SettingsFile { get; }
    public string TutorialPreferencesFile { get; }
    public string DatabaseFile { get; }
    public string PendingDatabaseRestoreFile { get; }
    public string LogsFolder { get; }
    public string LogFile { get; }
    public string BackupsFolder { get; }
    public string DiagnosticsFolder { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(BackupsFolder);
        Directory.CreateDirectory(DiagnosticsFolder);
    }
}
