namespace OpenCareer.App.Services;

public enum OpenCareerDataProfile
{
    Normal = 0,
    KjfkLiveTest = 1
}

public sealed record OpenCareerDataProfileSelection(
    OpenCareerDataPaths Paths,
    bool ResetRequested)
{
    public const string ProfileArgument = "--development-kjfk-live-test";
    public const string ResetArgument = "--reset-development-kjfk-live-test";

    public static OpenCareerDataProfileSelection Resolve(
        IEnumerable<string> arguments,
        string? localApplicationDataOverride = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool developmentProfile = false;
        bool resetRequested = false;

        foreach (string argument in arguments)
        {
            if (string.Equals(argument, ProfileArgument, StringComparison.OrdinalIgnoreCase))
            {
                developmentProfile = true;
                continue;
            }

            if (string.Equals(argument, ResetArgument, StringComparison.OrdinalIgnoreCase))
            {
                resetRequested = true;
                continue;
            }

            if (argument.StartsWith(ProfileArgument, StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith(ResetArgument, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Unsupported development live-test argument: {argument}",
                    nameof(arguments));
            }
        }

        if (resetRequested && !developmentProfile)
        {
            throw new InvalidOperationException(
                "The KJFK development data reset requires the explicit development live-test profile.");
        }

        string localApplicationData = localApplicationDataOverride
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        localApplicationData = Path.GetFullPath(localApplicationData);

        var paths = developmentProfile
            ? new OpenCareerDataPaths(
                Path.Combine(localApplicationData, "OpenCareer.LiveTests", "KJFK"),
                OpenCareerDataProfile.KjfkLiveTest,
                Path.Combine(localApplicationData, "OpenCareer"))
            : new OpenCareerDataPaths(
                Path.Combine(localApplicationData, "OpenCareer"),
                OpenCareerDataProfile.Normal,
                Path.Combine(localApplicationData, "OpenCareer"));

        return new(paths, resetRequested);
    }

    public void Prepare()
    {
        if (ResetRequested)
            OpenCareerDevelopmentDataProfile.Reset(Paths);

        Paths.EnsureDirectories();
    }
}

internal static class OpenCareerDevelopmentDataProfile
{
    private const string MarkerContents = "OpenCareer DEVELOPMENT / TEST profile: KJFK v1";

    internal static void EnsureMarker(OpenCareerDataPaths paths)
    {
        RequireDevelopmentProfile(paths);

        if (Directory.Exists(paths.Root))
        {
            if (File.Exists(paths.ProfileMarkerFile))
            {
                ValidateMarker(paths);
                return;
            }

            if (Directory.EnumerateFileSystemEntries(paths.Root).Any())
            {
                throw new InvalidDataException(
                    "The KJFK development data root is not empty and has no valid OpenCareer development marker.");
            }
        }

        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ProfileMarkerFile, MarkerContents);
    }

    internal static void Reset(OpenCareerDataPaths paths)
    {
        RequireDevelopmentProfile(paths);

        if (!Directory.Exists(paths.Root))
            return;

        ValidateMarker(paths);
        Directory.Delete(paths.Root, recursive: true);
    }

    private static void RequireDevelopmentProfile(OpenCareerDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.IsDevelopmentLiveTest)
        {
            throw new InvalidOperationException(
                "Only the fixed KJFK DEVELOPMENT / TEST profile can be reset.");
        }

        string normalParent = Path.GetDirectoryName(paths.NormalRoot)
            ?? throw new InvalidOperationException("Normal data root has no parent directory.");
        string expected = Path.GetFullPath(
            Path.Combine(normalParent, "OpenCareer.LiveTests", "KJFK"));
        if (!string.Equals(paths.Root, expected, PathComparison)
            || IsSameOrUnder(paths.Root, paths.NormalRoot)
            || IsSameOrUnder(paths.NormalRoot, paths.Root))
        {
            throw new InvalidOperationException(
                "KJFK development data reset target failed the fixed-root safety check.");
        }

        RejectReparsePoint(Path.GetDirectoryName(paths.Root)!);
        RejectReparsePoint(paths.Root);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsSameOrUnder(string candidate, string root)
    {
        string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedCandidate, normalizedRoot, PathComparison)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                PathComparison);
    }

    private static void RejectReparsePoint(string path)
    {
        if (Directory.Exists(path)
            && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "KJFK development data reset refused a redirected profile path.");
        }
    }

    private static void ValidateMarker(OpenCareerDataPaths paths)
    {
        if (File.Exists(paths.ProfileMarkerFile)
            && File.GetAttributes(paths.ProfileMarkerFile).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "KJFK development data reset refused a redirected profile marker.");
        }

        if (!File.Exists(paths.ProfileMarkerFile)
            || !string.Equals(
                File.ReadAllText(paths.ProfileMarkerFile),
                MarkerContents,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The KJFK development data marker is missing or invalid; reset was refused.");
        }
    }
}
