namespace OpenCareer.Domain.Aircraft;

public enum AircraftEngineType
{
    Piston = 1,
    TurbopropOrTurboshaft = 2,
    Jet = 3,
    Electric = 4,
    Rocket = 5,
    None = 6
}

public enum AircraftEngineMultiplicity
{
    None = 0,
    Single = 1,
    Multi = 2
}

public sealed record AircraftPowerplantClassification(
    AircraftEngineType EngineType,
    int EngineCount,
    AircraftEngineMultiplicity Multiplicity)
{
    public static AircraftPowerplantClassification? FromEvidence(
        AircraftEngineType? engineType,
        int? engineCount)
    {
        if (engineType is null || engineCount is null)
            return null;

        if (!Enum.IsDefined(engineType.Value) || engineCount < 0)
            return null;

        AircraftEngineMultiplicity? multiplicity =
            (engineType.Value, engineCount.Value) switch
            {
                (AircraftEngineType.None, 0) => AircraftEngineMultiplicity.None,
                (AircraftEngineType.None, _) => null,
                (_, 0) => null,
                (_, 1) => AircraftEngineMultiplicity.Single,
                _ => AircraftEngineMultiplicity.Multi
            };

        if (multiplicity is null)
            return null;

        var classification = new AircraftPowerplantClassification(
            engineType.Value,
            engineCount.Value,
            multiplicity.Value);
        classification.Validate();
        return classification;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(EngineType)
            || !Enum.IsDefined(Multiplicity)
            || EngineCount < 0
            || EngineType == AircraftEngineType.None
                && (EngineCount != 0
                    || Multiplicity != AircraftEngineMultiplicity.None)
            || EngineType != AircraftEngineType.None
                && (EngineCount == 0
                    || Multiplicity == AircraftEngineMultiplicity.None)
            || EngineCount == 1
                && Multiplicity != AircraftEngineMultiplicity.Single
            || EngineCount > 1
                && Multiplicity != AircraftEngineMultiplicity.Multi)
        {
            throw new ArgumentException(
                "Aircraft powerplant classification is inconsistent with its engine evidence.");
        }
    }
}

public sealed record AircraftReferenceMetadata(
    string? IcaoTypeDesignator = null,
    string? IcaoManufacturer = null,
    string? IcaoModel = null,
    AircraftEngineType? EngineType = null,
    int? PassengerCapacity = null)
{
    public void Validate()
    {
        ValidateOptionalText(IcaoTypeDesignator, nameof(IcaoTypeDesignator));
        ValidateOptionalText(IcaoManufacturer, nameof(IcaoManufacturer));
        ValidateOptionalText(IcaoModel, nameof(IcaoModel));

        if (EngineType is { } engineType && !Enum.IsDefined(engineType))
            throw new ArgumentOutOfRangeException(nameof(EngineType));

        if (PassengerCapacity is < 0)
            throw new ArgumentOutOfRangeException(nameof(PassengerCapacity));
    }

    private static void ValidateOptionalText(string? value, string name)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Reference metadata text must be non-empty when supplied.", name);
    }
}

/// <summary>
/// Stable cross-variation aircraft type identity derived only from an explicit
/// ICAO type designator. It does not merge canonical installed-aircraft records
/// or infer a type from display names.
/// </summary>
public sealed record AircraftTypeIdentity(
    string CanonicalTypeId,
    string IcaoTypeDesignator)
{
    public const string CanonicalTypePrefix = "icao-type:";

    public static AircraftTypeIdentity? FromReferenceMetadata(
        AircraftReferenceMetadata? metadata)
    {
        if (metadata is null)
            return null;

        metadata.Validate();

        if (string.IsNullOrWhiteSpace(metadata.IcaoTypeDesignator))
            return null;

        string designator = metadata.IcaoTypeDesignator
            .Trim()
            .ToUpperInvariant();
        var identity = new AircraftTypeIdentity(
            CanonicalTypePrefix + designator,
            designator);

        identity.Validate();
        return identity;
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(IcaoTypeDesignator);

        string designator = IcaoTypeDesignator.Trim().ToUpperInvariant();
        if (!string.Equals(IcaoTypeDesignator, designator, StringComparison.Ordinal)
            || !string.Equals(
                CanonicalTypeId,
                CanonicalTypePrefix + designator,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Aircraft type identity must use its normalized ICAO type designator.");
        }

    }
}

public static class AircraftCanonicalIdentity
{
    public const string MsfsTitlePrefix = "msfs-title:";
    public const string Cessna172SkyhawkTitle = "Cessna 172 Skyhawk";
    public const string Cessna172SkyhawkAircraftId =
        MsfsTitlePrefix + Cessna172SkyhawkTitle;

    public static string FromMsfsTitle(string aircraftTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftTitle);
        return MsfsTitlePrefix + aircraftTitle.Trim();
    }

    public static bool TryGetMsfsTitle(string canonicalAircraftId, out string aircraftTitle)
    {
        aircraftTitle = string.Empty;

        if (string.IsNullOrWhiteSpace(canonicalAircraftId)
            || !canonicalAircraftId.StartsWith(MsfsTitlePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string value = canonicalAircraftId[MsfsTitlePrefix.Length..];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        aircraftTitle = value;
        return true;
    }
}
