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

        string? installedPackagesPath = File
            .ReadLines(userConfigPath)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith(
                "InstalledPackagesPath",
                StringComparison.OrdinalIgnoreCase))
            .Select(ParseInstalledPackagesPath)
            .FirstOrDefault(static path => path is not null);

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
        int separator = line.IndexOfAny([' ', '=']);
        if (separator < 0 || separator == line.Length - 1)
            return null;

        string value = line[(separator + 1)..].Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
