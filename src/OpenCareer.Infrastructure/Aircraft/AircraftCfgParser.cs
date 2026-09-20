using System.Globalization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

internal sealed record AircraftCfgVariation(
    string SectionName,
    string Title,
    double? MaximumRangeNauticalMiles,
    int? PassengerCapacity);

internal sealed record AircraftCfgDocument(
    string? IcaoTypeDesignator,
    string? IcaoManufacturer,
    string? IcaoModel,
    AircraftEngineType? EngineType,
    int? EngineCount,
    IReadOnlyList<AircraftCfgVariation> Variations);

internal static class AircraftCfgParser
{
    internal static AircraftCfgDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var sections = ParseSections(content);
        sections.TryGetValue("GENERAL", out Dictionary<string, string>? general);

        string? typeDesignator = ReadOptionalText(general, "icao_type_designator");
        string? manufacturer = ReadOptionalText(general, "icao_manufacturer");
        string? model = ReadOptionalText(general, "icao_model");
        AircraftEngineType? engineType = ParseEngineType(ReadOptionalText(general, "icao_engine_type"));
        int? engineCount = ParseNonNegativeInt(ReadOptionalText(general, "icao_engine_count"));

        AircraftCfgVariation[] variations = sections
            .Where(static pair => pair.Key.StartsWith("FLTSIM.", StringComparison.OrdinalIgnoreCase))
            .Select(static pair => ParseVariation(pair.Key, pair.Value))
            .Where(static variation => variation is not null)
            .Cast<AircraftCfgVariation>()
            .OrderBy(static variation => variation.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            typeDesignator,
            manufacturer,
            model,
            engineType,
            engineCount,
            variations);
    }

    private static AircraftCfgVariation? ParseVariation(
        string sectionName,
        IReadOnlyDictionary<string, string> values)
    {
        string? title = ReadOptionalText(values, "title");
        if (title is null)
            return null;

        return new(
            sectionName,
            title,
            ParseNonNegativeDouble(ReadOptionalText(values, "ui_max_range")),
            ParseNonNegativeInt(ReadOptionalText(values, "capacity")));
    }

    private static Dictionary<string, Dictionary<string, string>> ParseSections(string content)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
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
            string value = Unquote(line[(separator + 1)..].Trim());

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

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Trim()
            : value;

    private static string? ReadOptionalText(
        IReadOnlyDictionary<string, string>? values,
        string key)
    {
        if (values is null
            || !values.TryGetValue(key, out string? value)
            || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static int? ParseNonNegativeInt(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            || parsed < 0)
        {
            return null;
        }

        return parsed;
    }

    private static double? ParseNonNegativeDouble(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            || !double.IsFinite(parsed)
            || parsed < 0)
        {
            return null;
        }

        return parsed;
    }

    private static AircraftEngineType? ParseEngineType(string? value)
    {
        if (value is null)
            return null;

        return value.Trim() switch
        {
            "" => AircraftEngineType.None,
            "Piston" => AircraftEngineType.Piston,
            "Turboprop/Turboshaft" => AircraftEngineType.TurbopropOrTurboshaft,
            "Jet" => AircraftEngineType.Jet,
            "Electric" => AircraftEngineType.Electric,
            "Rocket" => AircraftEngineType.Rocket,
            "None" => AircraftEngineType.None,
            _ => null
        };
    }
}
