using System.Globalization;

namespace OpenCareer.Infrastructure.Aircraft;

internal sealed record MsfsFlightModelDispatchFacts(
    double? ConfiguredEmptyWeightPounds,
    double? MaximumTakeoffWeightPounds,
    double? MaximumLandingWeightPounds,
    double? MaximumZeroFuelWeightPounds,
    double? TypicalCruiseKnots,
    bool? RetractableGear)
{
    internal bool HasAny =>
        ConfiguredEmptyWeightPounds is not null
        || MaximumTakeoffWeightPounds is not null
        || MaximumLandingWeightPounds is not null
        || MaximumZeroFuelWeightPounds is not null
        || TypicalCruiseKnots is not null
        || RetractableGear is not null;
}

internal static class MsfsFlightModelCfgParser
{
    internal static MsfsFlightModelDispatchFacts Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        Dictionary<string, Dictionary<string, string>> sections =
            ParseSections(content);

        Dictionary<string, string>? weightAndBalance =
            sections.GetValueOrDefault("WEIGHT_AND_BALANCE");

        Dictionary<string, string>? referenceSpeeds =
            sections.GetValueOrDefault("REFERENCE SPEEDS");

        Dictionary<string, string>? contactPoints =
            sections.GetValueOrDefault("CONTACT_POINTS");

        double? maximumGrossWeight =
            ReadPositive(weightAndBalance, "max_gross_weight");

        double? maximumTakeoffWeight =
            ReadWithDocumentedFallback(
                weightAndBalance,
                "max_takeoff_weight",
                maximumGrossWeight);

        double? maximumLandingWeight =
            ReadWithDocumentedFallback(
                weightAndBalance,
                "max_landing_weight",
                maximumTakeoffWeight ?? maximumGrossWeight);

        double? maximumZeroFuelWeight =
            ReadWithDocumentedFallback(
                weightAndBalance,
                "max_zero_fuel_weight",
                maximumGrossWeight);

        return new(
            ReadPositive(weightAndBalance, "empty_weight"),
            maximumTakeoffWeight,
            maximumLandingWeight,
            maximumZeroFuelWeight,
            ReadPositive(referenceSpeeds, "cruise_speed"),
            ResolveRetractableWheelGear(contactPoints));
    }

    private static bool? ResolveRetractableWheelGear(
        IReadOnlyDictionary<string, string>? section)
    {
        if (section is null)
            return null;

        bool foundRetractableWheel = false;

        foreach ((string key, string raw) in section)
        {
            if (!key.StartsWith("point.", StringComparison.OrdinalIgnoreCase))
                continue;

            string? properties = ExtractContactPointProperties(raw);
            if (properties is null)
                return null;

            string[] values = properties
                .Split(',', StringSplitOptions.TrimEntries);

            if (values.Length == 0
                || !int.TryParse(
                    values[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int contactPointType))
            {
                return null;
            }

            // Official MSFS 2024 contact-point type 1 is a wheel. Other point
            // types do not establish the wheel-gear configuration shown by
            // the registry.
            if (contactPointType != 1)
                continue;

            if (values.Length <= 12
                || !TryParseNonNegative(values[11], out double extensionTime)
                || !TryParseNonNegative(values[12], out double retractionTime))
            {
                return null;
            }

            bool hasExtensionTime = extensionTime > 0;
            bool hasRetractionTime = retractionTime > 0;
            if (hasExtensionTime != hasRetractionTime)
                return null;

            foundRetractableWheel |= hasExtensionTime;
        }

        // Positive movement timing proves retractability. Zero timing does not
        // prove fixed gear from a direct package file because other VFS-merged
        // configuration may still contribute contact points.
        return foundRetractableWheel ? true : null;
    }

    private static string? ExtractContactPointProperties(string raw)
    {
        int propertyName = raw.IndexOf(
            "Properties",
            StringComparison.OrdinalIgnoreCase);

        if (propertyName < 0)
            return raw.Contains('#') ? null : raw;

        int separator = propertyName + "Properties".Length;
        while (separator < raw.Length && char.IsWhiteSpace(raw[separator]))
            separator++;

        if (separator >= raw.Length || raw[separator] != ':')
            return null;

        string properties = raw[(separator + 1)..];
        int nextHash = properties.IndexOf('#');
        if (nextHash >= 0)
            properties = properties[..nextHash];

        properties = properties.Trim();
        return properties.Length == 0 ? null : properties;
    }

    private static bool TryParseNonNegative(string raw, out double value) =>
        double.TryParse(
            raw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value)
        && double.IsFinite(value)
        && value >= 0;

    private static double? ReadWithDocumentedFallback(
        IReadOnlyDictionary<string, string>? section,
        string key,
        double? fallback)
    {
        if (section is null || !section.TryGetValue(key, out string? raw))
            return fallback;

        return ParsePositive(raw);
    }

    private static double? ReadPositive(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        if (section is null || !section.TryGetValue(key, out string? raw))
            return null;

        return ParsePositive(raw);
    }

    private static double? ParsePositive(string? raw)
    {
        if (!double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
            || !double.IsFinite(value)
            || value <= 0)
        {
            return null;
        }

        return value;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseSections(
        string content)
    {
        var sections =
            new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string>? current = null;

        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } rawLine)
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string sectionName = line[1..^1].Trim();
                if (sectionName.Length == 0)
                {
                    current = null;
                    continue;
                }

                if (!sections.TryGetValue(sectionName, out current))
                {
                    current = new(StringComparer.OrdinalIgnoreCase);
                    sections.Add(sectionName, current);
                }

                continue;
            }

            if (current is null)
                continue;

            int separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Trim('"');

            if (key.Length > 0)
                current[key] = value;
        }

        return sections;
    }

    private static string StripComment(string line)
    {
        bool quoted = false;

        for (int index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
                quoted = !quoted;
            else if (line[index] == ';' && !quoted)
                return line[..index];
        }

        return line;
    }
}
