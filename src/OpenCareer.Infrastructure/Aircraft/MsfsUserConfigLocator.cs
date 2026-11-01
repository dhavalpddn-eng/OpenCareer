namespace OpenCareer.Infrastructure.Aircraft;

/// <summary>
/// Locates read-only MSFS 2024 UserCfg.opt files from the documented local
/// application roots. OpenCareer never edits the simulator configuration.
/// </summary>
public static class MsfsUserConfigLocator
{
    public const string OverrideEnvironmentVariable = "OPENCAREER_MSFS2024_USERCFG";

    public static IReadOnlyList<string> FindExisting(
        string? configuredUserConfigPath = null,
        string? roamingAppData = null,
        string? localAppData = null)
    {
        configuredUserConfigPath ??=
            Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);

        roamingAppData ??=
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        localAppData ??=
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredUserConfigPath))
            candidates.Add(configuredUserConfigPath);

        if (!string.IsNullOrWhiteSpace(roamingAppData))
        {
            candidates.Add(
                Path.Combine(
                    roamingAppData,
                    "Microsoft Flight Simulator 2024",
                    "UserCfg.opt"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(
                Path.Combine(
                    localAppData,
                    "Packages",
                    "Microsoft.Limitless_8wekyb3d8bbwe",
                    "LocalCache",
                    "UserCfg.opt"));
        }

        return candidates
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> FindPackageRoots(
        string? configuredUserConfigPath = null,
        string? roamingAppData = null,
        string? localAppData = null) =>
        FindExisting(
                configuredUserConfigPath,
                roamingAppData,
                localAppData)
            .SelectMany(MsfsPackageRootLocator.FromUserConfig)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
