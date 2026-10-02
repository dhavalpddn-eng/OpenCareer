using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

/// <summary>
/// Reads installed aircraft titles from the user's configured MSFS 2024 package roots.
/// This is installation evidence only; richer capability data remains the responsibility
/// of the existing aircraft-registry enrichment sources.
/// </summary>
public sealed class MsfsPackageInstalledAircraftDiscoverySource
    : IInstalledAircraftDiscoverySource
{
    public const string ProviderId = "msfs-package-cfg";

    private readonly string[] _packageRoots;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private InstalledAircraftDiscoverySnapshot _current =
        InstalledAircraftDiscoverySnapshot.Unavailable;
    private int _initialized;

    public MsfsPackageInstalledAircraftDiscoverySource(
        IEnumerable<string> packageRoots)
    {
        _packageRoots =
            packageRoots?
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? throw new ArgumentNullException(nameof(packageRoots));
    }

    public InstalledAircraftDiscoverySnapshot Current =>
        Volatile.Read(ref _current);

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized) == 1)
            return;

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_initialized == 1)
                return;

            InstalledAircraftDiscoverySnapshot snapshot =
                await Task.Run(
                        () => Scan(cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);

            Volatile.Write(
                ref _current,
                snapshot);

            Volatile.Write(
                ref _initialized,
                1);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private InstalledAircraftDiscoverySnapshot Scan(
        CancellationToken cancellationToken)
    {
        if (_packageRoots.Length == 0)
            return InstalledAircraftDiscoverySnapshot.Unavailable;

        var observations =
            new List<AircraftRegistryObservation>();

        bool anyReadableRoot = false;

        for (int rootIndex = 0;
            rootIndex < _packageRoots.Length;
            rootIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string root =
                _packageRoots[rootIndex];

            if (!Directory.Exists(root))
                continue;

            anyReadableRoot = true;

            string[] aircraftCfgFiles;

            try
            {
                aircraftCfgFiles =
                    Directory
                        .EnumerateFiles(
                            root,
                            "aircraft.cfg",
                            new EnumerationOptions
                            {
                                RecurseSubdirectories = true,
                                IgnoreInaccessible = true,
                                MatchCasing = MatchCasing.CaseInsensitive,
                                ReturnSpecialDirectories = false
                            })
                        .OrderBy(
                            static path =>
                                path,
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string path in aircraftCfgFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!HasInstalledPackageEvidence(
                        root,
                        path))
                {
                    continue;
                }

                AircraftCfgDocument? document =
                    TryRead(path);

                if (document is null)
                    continue;

                string relativePath =
                    Path
                        .GetRelativePath(
                            root,
                            path)
                        .Replace(
                            Path.DirectorySeparatorChar,
                            '/')
                        .Replace(
                            Path.AltDirectorySeparatorChar,
                            '/');

                foreach (AircraftCfgVariation variation
                    in document.Variations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (variation.IsUserSelectable == false
                        || variation.IsAirTraffic == true)
                    {
                        continue;
                    }

                    string title =
                        variation.Title.Trim();

                    if (title.Length == 0)
                        continue;

                    observations.Add(
                        new(
                            AircraftCanonicalIdentity
                                .FromMsfsTitle(title),
                            ProviderId,
                            $"root-{rootIndex}:{relativePath}#{variation.SectionName}",
                            AircraftDataConfidence.Reference,
                            IsInstalled:
                                true,
                            DisplayName:
                                title,
                            Access:
                                AircraftCfgParser.InferAccess(
                                    variation)));
                }
            }
        }

        if (!anyReadableRoot)
            return InstalledAircraftDiscoverySnapshot.Unavailable;

        AircraftRegistryObservation[] stable =
            observations
                .GroupBy(
                    static observation =>
                        observation.CanonicalAircraftId,
                    StringComparer.OrdinalIgnoreCase)
                .Where(
                    static group =>
                        group.Count() == 1)
                .Select(
                    static group =>
                        group.Single())
                .OrderBy(
                    static observation =>
                        observation.CanonicalAircraftId,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new(
            InstalledAircraftDiscoveryAvailability.Available,
            stable);
    }

    private static bool HasInstalledPackageEvidence(
        string packageRoot,
        string aircraftCfgPath)
    {
        string relative =
            Path.GetRelativePath(
                packageRoot,
                aircraftCfgPath);

        string? packageName =
            relative
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(packageName))
            return false;

        string packageDirectory =
            Path.Combine(
                packageRoot,
                packageName);

        return File.Exists(
                Path.Combine(
                    packageDirectory,
                    "manifest.json"))
            && File.Exists(
                Path.Combine(
                    packageDirectory,
                    "layout.json"));
    }

    private static AircraftCfgDocument? TryRead(
        string path)
    {
        try
        {
            return AircraftCfgParser.Parse(
                File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
