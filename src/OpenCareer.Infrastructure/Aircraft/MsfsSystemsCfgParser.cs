using System.Globalization;

namespace OpenCareer.Infrastructure.Aircraft;

internal sealed record MsfsPressurizationFacts(
    double? DesignCabinPressurePsi,
    double? MaximumPressureDifferentialPsi)
{
    internal bool ProvesPressurized =>
        DesignCabinPressurePsi is not null
        || MaximumPressureDifferentialPsi is not null;
}

internal static class MsfsSystemsCfgParser
{
    internal static MsfsPressurizationFacts Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        IReadOnlyDictionary<string, string>? pressurization =
            ParseSections(content).GetValueOrDefault("PRESSURIZATION");

        return new(
            ReadPositive(pressurization, "design_cabin_pressure"),
            ReadPositive(pressurization, "max_pressure_differential"));
    }

    private static double? ReadPositive(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        if (section is null
            || !section.TryGetValue(key, out string? raw)
            || !double.TryParse(
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
