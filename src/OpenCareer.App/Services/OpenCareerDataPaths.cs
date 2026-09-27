namespace OpenCareer.App.Services;

public sealed class OpenCareerDataPaths
{
    public OpenCareerDataPaths(string? rootOverride = null)
        : this(
            rootOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCareer"),
            OpenCareerDataProfile.Normal,
            rootOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCareer"))
    {
    }

    internal OpenCareerDataPaths(
        string root,
        OpenCareerDataProfile profile,
        string normalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalRoot);

        Root = Path.GetFullPath(root);
        NormalRoot = Path.GetFullPath(normalRoot);
        Profile = profile;
        SettingsFile = Path.Combine(Root, "settings.json");
        TutorialPreferencesFile = Path.Combine(Root, "ui-preferences.json");
        DatabaseFile = Path.Combine(Root, "opencareer.db");
        PendingDatabaseRestoreFile = Path.Combine(Root, "pending-database-restore.db");
        LogsFolder = Path.Combine(Root, "Logs");
        LogFile = Path.Combine(LogsFolder, "opencareer.log");
        BackupsFolder = Path.Combine(Root, "Backups");
        DiagnosticsFolder = Path.Combine(Root, "Diagnostics");
        ProfileMarkerFile = Path.Combine(Root, ".opencareer-development-profile");
    }

    public string Root { get; }
    internal string NormalRoot { get; }
    public OpenCareerDataProfile Profile { get; }
    public bool IsDevelopmentLiveTest => Profile == OpenCareerDataProfile.KjfkLiveTest;
    public string SettingsFile { get; }
    public string TutorialPreferencesFile { get; }
    public string DatabaseFile { get; }
    public string PendingDatabaseRestoreFile { get; }
    public string LogsFolder { get; }
    public string LogFile { get; }
    public string BackupsFolder { get; }
    public string DiagnosticsFolder { get; }
    internal string ProfileMarkerFile { get; }

    public void EnsureDirectories()
    {
        if (IsDevelopmentLiveTest)
            OpenCareerDevelopmentDataProfile.EnsureMarker(this);

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(BackupsFolder);
        Directory.CreateDirectory(DiagnosticsFolder);
    }
}
