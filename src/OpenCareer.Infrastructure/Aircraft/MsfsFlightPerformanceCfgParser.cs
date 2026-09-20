using System.Globalization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

internal sealed record MsfsFlightPerformanceDispatchFacts(
    double? FuelCapacityGallons,
    double? SingleFuelDensityPoundsPerGallon,
    double? MaximumFuelWeightPounds,
    AircraftConditionedPerformanceProfile? ConditionedPerformance)
{
    internal bool HasAny =>
        FuelCapacityGallons is not null
        || SingleFuelDensityPoundsPerGallon is not null
        || MaximumFuelWeightPounds is not null
        || ConditionedPerformance is not null;
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

        AircraftConditionedPerformanceProfile? conditioned =
            ParseConditionedPerformance(sections);

        return new(
            fuelCapacity,
            singleFuelDensity,
            maximumFuelWeight,
            conditioned);
    }

    private static AircraftConditionedPerformanceProfile? ParseConditionedPerformance(
        IReadOnlyDictionary<string, Dictionary<string, string>> sections)
    {
        sections.TryGetValue(
            "TAKEOFF_PERFORMANCE",
            out Dictionary<string, string>? takeoff);

        sections.TryGetValue(
            "LANDING_PERFORMANCE",
            out Dictionary<string, string>? landing);

        AircraftPerformanceGrid3D? takeoffGroundRoll = ParseGrid3D(
            takeoff,
            "takeoff_ground_roll_distance_table_by_weight_and_OAT_and_altitude",
            AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius);

        AircraftPerformanceGrid3D? takeoffTotal = ParseGrid3D(
            takeoff,
            "takeoff_total_distance_table_by_weight_and_OAT_and_altitude",
            AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius);

        AircraftPerformanceGrid3D? landingGroundRoll = ParseGrid3D(
            landing,
            "landing_ground_roll_distance_table_by_weight_and_OAT_and_altitude",
            AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius);

        AircraftPerformanceGrid3D? landingTotal = ParseGrid3D(
            landing,
            "landing_total_distance_table_by_weight_and_OAT_and_altitude",
            AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius);

        IReadOnlyList<AircraftCruisePerformanceProfile> cruiseProfiles =
            ParseCruiseProfiles(sections);

        var profile = new AircraftConditionedPerformanceProfile(
            takeoffGroundRoll,
            takeoffTotal,
            landingGroundRoll,
            landingTotal,
            cruiseProfiles);

        if (!profile.HasAny)
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

    private static IReadOnlyList<AircraftCruisePerformanceProfile> ParseCruiseProfiles(
        IReadOnlyDictionary<string, Dictionary<string, string>> sections)
    {
        var indexedSections = new List<(int Index, Dictionary<string, string> Values)>();

        foreach ((string sectionName, Dictionary<string, string> values) in sections)
        {
            const string prefix = "CRUISE_PERFORMANCE.";
            if (!sectionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string suffix = sectionName[prefix.Length..];

            if (!int.TryParse(
                    suffix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int index)
                || index is < 0 or > 99)
            {
                continue;
            }

            indexedSections.Add((index, values));
        }

        indexedSections.Sort(static (left, right) => left.Index.CompareTo(right.Index));

        for (int index = 0; index < indexedSections.Count; index++)
        {
            if (indexedSections[index].Index != index)
                return Array.Empty<AircraftCruisePerformanceProfile>();
        }

        var profiles = new List<AircraftCruisePerformanceProfile>();

        foreach ((int index, Dictionary<string, string> values) in indexedSections)
        {
            AircraftPerformanceGrid3D? tas = ParseGrid3D(
                values,
                "cruise_TAS_table_by_weight_and_ISA_dev_and_altitude",
                AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius);

            AircraftPerformanceGrid3D? fuelConsumption = ParseGrid3D(
                values,
                "cruise_fuel_consumption_table_by_weight_and_ISA_dev_and_altitude",
                AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius);

            if (tas is null && fuelConsumption is null)
                continue;

            int? fuelTypeIndex = ReadNonNegativeInt(values, "fuel_type_idx");
            if (values.ContainsKey("fuel_type_idx") && fuelTypeIndex is null)
                continue;

            var profile = new AircraftCruisePerformanceProfile(
                index,
                ReadOptionalText(values, "profile_name"),
                fuelTypeIndex ?? 0,
                ReadNonNegative(values, "Mach"),
                tas,
                fuelConsumption);

            try
            {
                profile.Validate();
                profiles.Add(profile);
            }
            catch (ArgumentException)
            {
                // One malformed cruise profile must not fabricate usable data.
            }
        }

        return profiles;
    }

    private static AircraftPerformanceGrid3D? ParseGrid3D(
        IReadOnlyDictionary<string, string>? section,
        string key,
        AircraftPerformanceTemperatureAxisKind temperatureAxisKind)
    {
        if (section is null || !section.TryGetValue(key, out string? raw))
            return null;

        string[] halves = raw.Split(
            "::",
            StringSplitOptions.TrimEntries);

        if (halves.Length != 2)
            return null;

        string[] axisBlocks = halves[0].Split(
            ':',
            StringSplitOptions.TrimEntries);

        if (axisBlocks.Length != 3)
            return null;

        double[]? weights = ParseNumberList(axisBlocks[0]);
        double[]? temperatures = ParseNumberList(axisBlocks[1]);
        double[]? altitudes = ParseNumberList(axisBlocks[2]);

        if (weights is null || temperatures is null || altitudes is null)
            return null;

        string[] valueGroups = halves[1].Split(
            ':',
            StringSplitOptions.TrimEntries);

        long expectedGroups = (long)weights.Length * temperatures.Length;
        if (valueGroups.LongLength != expectedGroups)
            return null;

        var values = new List<double>(
            checked(weights.Length * temperatures.Length * altitudes.Length));

        foreach (string group in valueGroups)
        {
            double[]? row = ParseNumberList(group);
            if (row is null || row.Length != altitudes.Length)
                return null;

            values.AddRange(row);
        }

        try
        {
            return new AircraftPerformanceGrid3D(
                weights,
                temperatures,
                altitudes,
                values,
                temperatureAxisKind);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static double[]? ParseNumberList(string raw)
    {
        string[] parts = raw.Split(
            ',',
            StringSplitOptions.TrimEntries
            | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return null;

        var values = new double[parts.Length];

        for (int index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(
                    parts[index],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value)
                || !double.IsFinite(value))
            {
                return null;
            }

            values[index] = value;
        }

        return values;
    }

    private static string? ReadOptionalText(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        if (section is null || !section.TryGetValue(key, out string? raw))
            return null;

        string value = raw.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? ReadNonNegativeInt(
        IReadOnlyDictionary<string, string>? section,
        string key)
    {
        if (section is null || !section.TryGetValue(key, out string? raw))
            return null;

        if (!int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
            || value < 0)
        {
            return null;
        }

        return value;
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
            string value = line[(separator + 1)..].Trim();

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
