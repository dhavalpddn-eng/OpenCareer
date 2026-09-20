using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

public sealed class MsfsAircraftCfgObservationSource(
    IEnumerable<string> packageRoots)
    : IAircraftRegistryObservationSource
{
    public const string ProviderId = "msfs-aircraft-cfg";

    private readonly string[] _packageRoots =
        packageRoots?
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? throw new ArgumentNullException(nameof(packageRoots));

    public static MsfsAircraftCfgObservationSource FromUserConfig(string userConfigPath) =>
        new(MsfsPackageRootLocator.FromUserConfig(userConfigPath));

    public Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AircraftCanonicalIdentity.TryGetMsfsTitle(canonicalAircraftId, out string title))
        {
            return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>(
                Array.Empty<AircraftRegistryObservation>());
        }

        MatchingVariation[] matches = FindMatches(title, cancellationToken).ToArray();

        // Without a VFS-resolved source path, multiple local definitions are ambiguous.
        // Fail closed rather than arbitrarily choosing an override.
        if (matches.Length != 1)
        {
            return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>(
                Array.Empty<AircraftRegistryObservation>());
        }

        MatchingVariation match = matches[0];
        AircraftCfgDocument document = match.Document;
        AircraftCfgVariation variation = match.Variation;

        var metadata = new AircraftReferenceMetadata(
            document.IcaoTypeDesignator,
            document.IcaoManufacturer,
            document.IcaoModel,
            document.EngineType,
            variation.PassengerCapacity);

        bool hasMetadata =
            metadata.IcaoTypeDesignator is not null
            || metadata.IcaoManufacturer is not null
            || metadata.IcaoModel is not null
            || metadata.EngineType is not null
            || metadata.PassengerCapacity is not null;

        var observation = new AircraftRegistryObservation(
            canonicalAircraftId,
            ProviderId,
            $"{match.Path}#{variation.SectionName}",
            AircraftDataConfidence.Reference,
            IsInstalled: false,
            MaximumRangeNauticalMiles: variation.MaximumRangeNauticalMiles,
            EngineCount: document.EngineCount,
            ReferenceMetadata: hasMetadata ? metadata : null);

        return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>([observation]);
    }

    private IEnumerable<MatchingVariation> FindMatches(
        string exactTitle,
        CancellationToken cancellationToken)
    {
        foreach (string root in _packageRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    root,
                    "aircraft.cfg",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MatchCasing = MatchCasing.CaseInsensitive,
                        ReturnSpecialDirectories = false
                    });
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string path in files.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                AircraftCfgDocument? document = TryRead(path);
                if (document is null)
                    continue;

                foreach (AircraftCfgVariation variation in document.Variations)
                {
                    if (string.Equals(variation.Title, exactTitle, StringComparison.Ordinal))
                        yield return new(Path.GetFullPath(path), document, variation);
                }
            }
        }
    }

    private static AircraftCfgDocument? TryRead(string path)
    {
        try
        {
            return AircraftCfgParser.Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record MatchingVariation(
        string Path,
        AircraftCfgDocument Document,
        AircraftCfgVariation Variation);
}
