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

        MsfsFlightModelDispatchFacts? flightModel =
            TryReadFlightModelFacts(match.AircraftCfgPath);

        AircraftDispatchPerformanceProfile? dispatchPerformance =
            TryReadDispatchPerformance(
                match.AircraftCfgPath,
                flightModel);

        double? maximumPayload =
            ResolveMaximumPayload(flightModel);

        AircraftAccess? access =
            InferAccess(variation);

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
            TypicalCruiseKnots: flightModel?.TypicalCruiseKnots,
            EngineCount: document.EngineCount,
            ReferenceMetadata: hasMetadata ? metadata : null,
            DispatchPerformance: dispatchPerformance);

        return Task.FromResult<IReadOnlyList<AircraftRegistryObservation>>([observation]);
    }

    private IEnumerable<MatchingVariation> FindMatches(
        string exactTitle,
        CancellationToken cancellationToken)
    {
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
                    if (string.Equals(variation.Title, exactTitle, StringComparison.Ordinal))
                    {
                        string relativePath = Path.GetRelativePath(root, path)
                            .Replace(Path.DirectorySeparatorChar, '/')
                            .Replace(Path.AltDirectorySeparatorChar, '/');

                        yield return new(
                            $"root-{rootIndex}:{relativePath}#{variation.SectionName}",
                            path,
                            document,
                            variation);
                    }
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

    private static AircraftAccess? InferAccess(
        AircraftCfgVariation variation)
    {
        if (variation.IsUserSelectable == false
            || variation.IsAirTraffic == true)
        {
            return null;
        }

        if (variation.AtcParkingTypes.Any(
                static type =>
                    type is "MIL_COMBAT" or "MIL_CARGO"))
        {
            return AircraftAccess.Military;
        }

        if (variation.IsUserSelectable == true
            || variation.AtcParkingTypes.Any(
                static type =>
                    type is "ANY"
                        or "RAMP"
                        or "CARGO"
                        or "GATE"
                        or "DOCK"))
        {
            return AircraftAccess.Civilian;
        }

        return null;
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
