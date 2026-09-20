namespace OpenCareer.Infrastructure.Aircraft;

public static class MsfsPackageRootLocator
{
    private static readonly string[] PackageFolderNames =
    [
        "Community",
        "Community2024",
        "Official2020",
        "Official2024",
        "StreamedPackages"
    ];

    public static IReadOnlyList<string> FromUserConfig(string userConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userConfigPath);

        if (!File.Exists(userConfigPath))
            return Array.Empty<string>();

        string? installedPackagesPath;
        try
        {
            installedPackagesPath = File
                .ReadLines(userConfigPath)
                .Select(static line => line.Trim())
                .Where(static line => line.StartsWith(
                    "InstalledPackagesPath",
                    StringComparison.OrdinalIgnoreCase))
                .Select(ParseInstalledPackagesPath)
                .FirstOrDefault(static path => path is not null);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        if (installedPackagesPath is null || !Directory.Exists(installedPackagesPath))
            return Array.Empty<string>();

        return PackageFolderNames
            .Select(name => Path.Combine(installedPackagesPath, name))
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ParseInstalledPackagesPath(string line)
    {
        const string key = "InstalledPackagesPath";

        if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            return null;

        string value = line[key.Length..].TrimStart();
        if (value.StartsWith('='))
            value = value[1..].TrimStart();

        value = value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
