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

public static class AircraftCanonicalIdentity
{
    public const string MsfsTitlePrefix = "msfs-title:";

    public static string FromMsfsTitle(string aircraftTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftTitle);
        string title = aircraftTitle.Trim();

        // The MSFS 2024 Basic variant displays as "Cessna 172 Skyhawk - Asobo
        // Studio Basic" in the picker but reports "C172SP Classic Passengers"
        // through SimConnect TITLE. Both observations were verified in the same
        // live session. Other variants, including Cargo, remain distinct.
        if (string.Equals(title, "Cessna 172 Skyhawk - Asobo Studio Basic",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(title, "C172SP Classic Passengers",
                StringComparison.OrdinalIgnoreCase))
        {
            title = "Cessna 172 Skyhawk";
        }

        return MsfsTitlePrefix + title;
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
