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

    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private IReadOnlyDictionary<string, MatchingVariation[]>? _variationIndex;

    public static MsfsAircraftCfgObservationSource FromUserConfig(string userConfigPath) =>
        new(MsfsPackageRootLocator.FromUserConfig(userConfigPath));

    public async Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AircraftCanonicalIdentity.TryGetMsfsTitle(canonicalAircraftId, out string title))
        {
            return Array.Empty<AircraftRegistryObservation>();
        }

        IReadOnlyDictionary<string, MatchingVariation[]> index =
            await GetVariationIndexAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!index.TryGetValue(title, out MatchingVariation[]? matches))
            return Array.Empty<AircraftRegistryObservation>();

        // Without a VFS-resolved source path, multiple local definitions are ambiguous.
        // Fail closed rather than arbitrarily choosing an override.
        if (matches.Length != 1)
        {
            return Array.Empty<AircraftRegistryObservation>();
        }

        return await Task.Run<IReadOnlyList<AircraftRegistryObservation>>(
                () => CreateObservations(
                    canonicalAircraftId,
                    matches[0],
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<AircraftRegistryObservation> CreateObservations(
        string canonicalAircraftId,
        MatchingVariation match,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        MsfsFlightModelDispatchFacts? flightModel =
            TryReadFlightModelFacts(match.AircraftCfgPath);

        AircraftDispatchPerformanceProfile? dispatchPerformance =
            TryReadDispatchPerformance(
                match.AircraftCfgPath,
                flightModel);

        double? maximumPayload =
            ResolveMaximumPayload(flightModel);

        bool? pressurized =
            TryReadPressurizationCapability(match.AircraftCfgPath);

        AircraftAccess? access =
            AircraftCfgParser.InferAccess(variation);

        var observation = new AircraftRegistryObservation(
            canonicalAircraftId,
            ProviderId,
            match.ProviderRecordId,
            AircraftDataConfidence.Reference,
            IsInstalled: false,
            DisplayName: variation.Title,
            Access: access,
            MaximumPayloadPounds: maximumPayload,
            MaximumRangeNauticalMiles: variation.MaximumRangeNauticalMiles,
            CertifiedCeilingFeet: variation.CertifiedCeilingFeet,
            TypicalCruiseKnots: flightModel?.TypicalCruiseKnots,
            EngineCount: document.EngineCount,
            Pressurized: pressurized,
            RetractableGear: flightModel?.RetractableGear,
            ReferenceMetadata: hasMetadata ? metadata : null,
            DispatchPerformance: dispatchPerformance,
            PassengerCapacity: variation.PassengerCapacity,
            ObjectClass: document.ObjectClass);

        return [observation];
    }

    private async Task<IReadOnlyDictionary<string, MatchingVariation[]>> GetVariationIndexAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, MatchingVariation[]>? current =
            Volatile.Read(ref _variationIndex);

        if (current is not null)
            return current;

        await _indexGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            current = Volatile.Read(ref _variationIndex);
            if (current is not null)
                return current;

            IReadOnlyDictionary<string, MatchingVariation[]> built =
                await Task.Run(
                        () => BuildVariationIndex(cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);

            Volatile.Write(ref _variationIndex, built);
            return built;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private IReadOnlyDictionary<string, MatchingVariation[]> BuildVariationIndex(
        CancellationToken cancellationToken)
    {
        var matchesByTitle =
            new Dictionary<string, List<MatchingVariation>>(
                StringComparer.Ordinal);

        for (int rootIndex = 0; rootIndex < _packageRoots.Length; rootIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string root = _packageRoots[rootIndex];
            if (!Directory.Exists(root))
                continue;

            string[] files;
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
                        })
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
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

            foreach (string path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AircraftCfgDocument? document = TryRead(path);
                if (document is null)
                    continue;

                foreach (AircraftCfgVariation variation in document.Variations)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string title = variation.Title.Trim();
                    if (title.Length == 0)
                        continue;

                    string relativePath = Path.GetRelativePath(root, path)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');

                    if (!matchesByTitle.TryGetValue(
                            title,
                            out List<MatchingVariation>? matches))
                    {
                        matches = [];
                        matchesByTitle.Add(title, matches);
                    }

                    matches.Add(
                        new(
                            $"root-{rootIndex}:{relativePath}#{variation.SectionName}",
                            path,
                            document,
                            variation));
                }
            }
        }

        return matchesByTitle.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
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

    private static AircraftDispatchPerformanceProfile? TryReadDispatchPerformance(
        string aircraftCfgPath,
        MsfsFlightModelDispatchFacts? flightModel)
    {
        string? directory = Path.GetDirectoryName(aircraftCfgPath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        MsfsFlightPerformanceDispatchFacts? flightPerformance =
            TryReadFlightPerformance(
                FindUniqueSibling(directory, "flight_performance.cfg"));

        if (flightModel is null && flightPerformance is null)
            return null;

        var profile = new AircraftDispatchPerformanceProfile(
            OperatingEmptyWeightPounds: null,
            MaximumTakeoffWeightPounds:
                flightModel?.MaximumTakeoffWeightPounds,
            MaximumFuelWeightPounds:
                flightPerformance?.MaximumFuelWeightPounds,
            PayloadRangeEnvelope: null,
            Confidence: AircraftDataConfidence.Reference,
            Source: "MSFS 2024 local aircraft configuration",
            ConfiguredEmptyWeightPounds:
                flightModel?.ConfiguredEmptyWeightPounds,
            MaximumLandingWeightPounds:
                flightModel?.MaximumLandingWeightPounds,
            MaximumZeroFuelWeightPounds:
                flightModel?.MaximumZeroFuelWeightPounds,
            ConditionedPerformance:
                flightPerformance?.ConditionedPerformance,
            FuelCapacityGallons:
                flightPerformance?.FuelCapacityGallons,
            FuelDensities:
                flightPerformance?.FuelDensities);

        bool hasAny =
            profile.MaximumTakeoffWeightPounds is not null
            || profile.MaximumFuelWeightPounds is not null
            || profile.ConfiguredEmptyWeightPounds is not null
            || profile.MaximumLandingWeightPounds is not null
            || profile.MaximumZeroFuelWeightPounds is not null
            || profile.ConditionedPerformance is not null
            || profile.FuelCapacityGallons is not null
            || profile.FuelDensities is not null;

        if (!hasAny)
            return null;

        try
        {
            profile.Validate();
            return profile;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static MsfsFlightModelDispatchFacts? TryReadFlightModelFacts(
        string aircraftCfgPath)
    {
        string? directory = Path.GetDirectoryName(aircraftCfgPath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        return TryReadFlightModel(
            FindUniqueSibling(
                directory,
                "flight_model.cfg"));
    }

    private static MsfsFlightModelDispatchFacts? TryReadFlightModel(string? path)
    {
        if (path is null)
            return null;

        try
        {
            MsfsFlightModelDispatchFacts facts =
                MsfsFlightModelCfgParser.Parse(File.ReadAllText(path));

            return facts.HasAny ? facts : null;
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

    private static double? ResolveMaximumPayload(
        MsfsFlightModelDispatchFacts? flightModel)
    {
        if (flightModel?.ConfiguredEmptyWeightPounds is not { } empty
            || flightModel.MaximumZeroFuelWeightPounds is not { } zeroFuel
            || zeroFuel < empty)
        {
            return null;
        }

        return zeroFuel - empty;
    }

    private static MsfsFlightPerformanceDispatchFacts? TryReadFlightPerformance(
        string? path)
    {
        if (path is null)
            return null;

        try
        {
            MsfsFlightPerformanceDispatchFacts facts =
                MsfsFlightPerformanceCfgParser.Parse(File.ReadAllText(path));

            return facts.HasAny ? facts : null;
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

    private static bool? TryReadPressurizationCapability(
        string aircraftCfgPath)
    {
        string? directory = Path.GetDirectoryName(aircraftCfgPath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        string? path = FindUniqueSibling(directory, "systems.cfg");
        if (path is null)
            return null;

        try
        {
            MsfsPressurizationFacts facts =
                MsfsSystemsCfgParser.Parse(File.ReadAllText(path));

            return facts.ProvesPressurized ? true : null;
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

    private static string? FindUniqueSibling(
        string directory,
        string fileName)
    {
        try
        {
            string[] matches = Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetFileName(path),
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return matches.Length == 1 ? matches[0] : null;
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
        string ProviderRecordId,
        string AircraftCfgPath,
        AircraftCfgDocument Document,
        AircraftCfgVariation Variation);
}
