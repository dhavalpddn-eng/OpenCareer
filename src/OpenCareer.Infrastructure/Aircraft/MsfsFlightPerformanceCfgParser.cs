using System.Globalization;

namespace OpenCareer.Infrastructure.Aircraft;

internal sealed record MsfsFlightPerformanceDispatchFacts(
    double? FuelCapacityGallons,
    double? SingleFuelDensityPoundsPerGallon,
    double? MaximumFuelWeightPounds)
{
    internal bool HasAny =>
        FuelCapacityGallons is not null
        || SingleFuelDensityPoundsPerGallon is not null
        || MaximumFuelWeightPounds is not null;
}

internal static class MsfsFlightPerformanceCfgParser
{
    internal static MsfsFlightPerformanceDispatchFacts Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        Dictionary<string, Dictionary<string, string>> sections =
            ParseSections(content);

        sections.TryGetValue(
            "AIRCRAFT_LOADING",
            out Dictionary<string, string>? loading);

        sections.TryGetValue(
            "ENGINE_PERFORMANCE",
            out Dictionary<string, string>? enginePerformance);

        double? fuelCapacity =
            ReadNonNegative(loading, "fuel_capacity");

        double? singleFuelDensity =
            ReadSinglePositiveDensity(
                enginePerformance,
                "fuel_density_table");

        double? maximumFuelWeight =
            fuelCapacity is { } capacity
            && singleFuelDensity is { } density
                ? capacity * density
                : null;

        if (maximumFuelWeight is { } weight && !double.IsFinite(weight))
            maximumFuelWeight = null;

        return new(
            fuelCapacity,
            singleFuelDensity,
            maximumFuelWeight);
    }

    private static double? ReadNonNegative(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        if (section is null || !section.TryGetValue(key, out string? raw))
            return null;

        if (!double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
            || !double.IsFinite(value)
            || value < 0)
        {
            return null;
        }

        return value;
    }

    private static double? ReadSinglePositiveDensity(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        if (section is null || !section.TryGetValue(key, out string? raw))
            return null;

        string[] values = raw
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);

        if (values.Length != 1)
            return null;

        if (!double.TryParse(
                values[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double density)
            || !double.IsFinite(density)
            || density <= 0)
        {
            return null;
        }

        return density;
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
