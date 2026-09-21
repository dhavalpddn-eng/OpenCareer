namespace OpenCareer.Domain.Planning;

public enum DispatchWeatherAuthority
{
    Reference = 0,
    LocalSimulator = 1
}

/// <summary>
/// Runway-relative wind components already normalized into the same reference frame
/// as the runway. Positive headwind means headwind; negative headwind means tailwind.
/// Crosswind values are magnitudes and therefore non-negative.
/// </summary>
public sealed record RunwayWindObservation(
    string RunwayIdentifier,
    double? SustainedHeadwindKnots,
    double? SustainedCrosswindKnots,
    double? GustHeadwindKnots = null,
    double? GustCrosswindKnots = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RunwayIdentifier);

        ValidateFinite(SustainedHeadwindKnots, nameof(SustainedHeadwindKnots));
        ValidateNonNegative(SustainedCrosswindKnots, nameof(SustainedCrosswindKnots));
        ValidateFinite(GustHeadwindKnots, nameof(GustHeadwindKnots));
        ValidateNonNegative(GustCrosswindKnots, nameof(GustCrosswindKnots));
    }

    private static void ValidateFinite(double? value, string name)
    {
        if (value is { } number && !double.IsFinite(number))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateNonNegative(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>
/// Provider-neutral dispatch weather observation. The source is responsible for
/// converting raw weather and runway geometry into runway-relative wind components.
/// No runway-heading or magnetic/true-north assumptions are made in Domain logic.
/// </summary>
public sealed record AirportDispatchWeatherObservation(
    string Icao,
    string SourceId,
    DispatchWeatherAuthority Authority,
    DateTimeOffset ObservedAt,
    IReadOnlyList<RunwayWindObservation> RunwayWinds,
    double? DensityAltitudeFeet = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Icao);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceId);
        ArgumentNullException.ThrowIfNull(RunwayWinds);

        if (!Enum.IsDefined(Authority))
            throw new ArgumentOutOfRangeException(nameof(Authority));

        if (DensityAltitudeFeet is { } densityAltitude
            && !double.IsFinite(densityAltitude))
        {
            throw new ArgumentOutOfRangeException(nameof(DensityAltitudeFeet));
        }

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (RunwayWindObservation runwayWind in RunwayWinds)
        {
            ArgumentNullException.ThrowIfNull(runwayWind);
            runwayWind.Validate();

            if (!identifiers.Add(runwayWind.RunwayIdentifier))
            {
                throw new ArgumentException(
                    "Weather observation contains duplicate runway wind identifiers.",
                    nameof(RunwayWinds));
            }
        }
    }

    public RunwayWindObservation? FindRunway(string runwayIdentifier) =>
        RunwayWinds.FirstOrDefault(
            wind => string.Equals(
                wind.RunwayIdentifier,
                runwayIdentifier,
                StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Explicit physical weather limits. These are operation/aircraft planning inputs,
/// not pilot qualification rules and not hidden global defaults.
/// </summary>
public sealed record DispatchWeatherLimits(
    double MaximumCrosswindKnots,
    double MaximumTailwindKnots,
    double? MaximumDensityAltitudeFeet = null)
{
    public void Validate()
    {
        ValidateNonNegative(MaximumCrosswindKnots, nameof(MaximumCrosswindKnots));
        ValidateNonNegative(MaximumTailwindKnots, nameof(MaximumTailwindKnots));

        if (MaximumDensityAltitudeFeet is { } maximumDensityAltitude
            && !double.IsFinite(maximumDensityAltitude))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDensityAltitudeFeet));
        }
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
