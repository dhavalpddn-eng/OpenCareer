namespace OpenCareer.Domain.Aircraft;

public enum AircraftPerformanceTemperatureAxisKind
{
    OutsideAirTemperatureCelsius = 0,
    IsaDeviationCelsius = 1
}

/// <summary>
/// Rectangular three-dimensional MSFS performance table.
/// Values are stored with pressure altitude as the fastest-varying axis,
/// then temperature, then weight, matching the documented modern CFG nD layout.
/// Interpolation is bounded to the supplied envelope; extrapolation is never performed.
/// </summary>
public sealed class AircraftPerformanceGrid3D
{
    private readonly double[] _weightsPounds;
    private readonly double[] _temperaturesCelsius;
    private readonly double[] _pressureAltitudesFeet;
    private readonly double[] _values;

    public AircraftPerformanceGrid3D(
        IEnumerable<double> weightsPounds,
        IEnumerable<double> temperaturesCelsius,
        IEnumerable<double> pressureAltitudesFeet,
        IEnumerable<double> values,
        AircraftPerformanceTemperatureAxisKind temperatureAxisKind)
    {
        ArgumentNullException.ThrowIfNull(weightsPounds);
        ArgumentNullException.ThrowIfNull(temperaturesCelsius);
        ArgumentNullException.ThrowIfNull(pressureAltitudesFeet);
        ArgumentNullException.ThrowIfNull(values);

        _weightsPounds = weightsPounds.ToArray();
        _temperaturesCelsius = temperaturesCelsius.ToArray();
        _pressureAltitudesFeet = pressureAltitudesFeet.ToArray();
        _values = values.ToArray();
        TemperatureAxisKind = temperatureAxisKind;

        Validate();
    }

    public IReadOnlyList<double> WeightsPounds => _weightsPounds;
    public IReadOnlyList<double> TemperaturesCelsius => _temperaturesCelsius;
    public IReadOnlyList<double> PressureAltitudesFeet => _pressureAltitudesFeet;
    public IReadOnlyList<double> Values => _values;
    public AircraftPerformanceTemperatureAxisKind TemperatureAxisKind { get; }

    public void Validate()
    {
        if (!Enum.IsDefined(TemperatureAxisKind))
            throw new ArgumentOutOfRangeException(nameof(TemperatureAxisKind));

        ValidateAxis(_weightsPounds, nameof(WeightsPounds), requireNonNegative: true);
        ValidateAxis(_temperaturesCelsius, nameof(TemperaturesCelsius), requireNonNegative: false);
        ValidateAxis(_pressureAltitudesFeet, nameof(PressureAltitudesFeet), requireNonNegative: false);

        long expected =
            (long)_weightsPounds.Length
            * _temperaturesCelsius.Length
            * _pressureAltitudesFeet.Length;

        if (expected != _values.Length)
        {
            throw new ArgumentException(
                "Performance table value count does not match its axis dimensions.",
                nameof(Values));
        }

        if (_values.Any(static value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Values),
                "Performance table values must be finite and non-negative.");
        }
    }

    public double? Interpolate(
        double weightPounds,
        double temperatureCelsius,
        double pressureAltitudeFeet)
    {
        if (!double.IsFinite(weightPounds)
            || !double.IsFinite(temperatureCelsius)
            || !double.IsFinite(pressureAltitudeFeet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(weightPounds),
                "Performance lookup inputs must be finite.");
        }

        Bracket? weight = FindBracket(_weightsPounds, weightPounds);
        Bracket? temperature = FindBracket(_temperaturesCelsius, temperatureCelsius);
        Bracket? altitude = FindBracket(_pressureAltitudesFeet, pressureAltitudeFeet);

        if (weight is null || temperature is null || altitude is null)
            return null;

        double lowerWeightLowerTemperature = Lerp(
            ValueAt(weight.Value.Lower, temperature.Value.Lower, altitude.Value.Lower),
            ValueAt(weight.Value.Lower, temperature.Value.Lower, altitude.Value.Upper),
            altitude.Value.Fraction);

        double lowerWeightUpperTemperature = Lerp(
            ValueAt(weight.Value.Lower, temperature.Value.Upper, altitude.Value.Lower),
            ValueAt(weight.Value.Lower, temperature.Value.Upper, altitude.Value.Upper),
            altitude.Value.Fraction);

        double upperWeightLowerTemperature = Lerp(
            ValueAt(weight.Value.Upper, temperature.Value.Lower, altitude.Value.Lower),
            ValueAt(weight.Value.Upper, temperature.Value.Lower, altitude.Value.Upper),
            altitude.Value.Fraction);

        double upperWeightUpperTemperature = Lerp(
            ValueAt(weight.Value.Upper, temperature.Value.Upper, altitude.Value.Lower),
            ValueAt(weight.Value.Upper, temperature.Value.Upper, altitude.Value.Upper),
            altitude.Value.Fraction);

        double lowerWeight = Lerp(
            lowerWeightLowerTemperature,
            lowerWeightUpperTemperature,
            temperature.Value.Fraction);

        double upperWeight = Lerp(
            upperWeightLowerTemperature,
            upperWeightUpperTemperature,
            temperature.Value.Fraction);

        return Lerp(lowerWeight, upperWeight, weight.Value.Fraction);
    }

    public double? TryGetExact(
        double weightPounds,
        double temperatureCelsius,
        double pressureAltitudeFeet)
    {
        int weightIndex = Array.BinarySearch(_weightsPounds, weightPounds);
        int temperatureIndex = Array.BinarySearch(_temperaturesCelsius, temperatureCelsius);
        int altitudeIndex = Array.BinarySearch(_pressureAltitudesFeet, pressureAltitudeFeet);

        return weightIndex >= 0 && temperatureIndex >= 0 && altitudeIndex >= 0
            ? ValueAt(weightIndex, temperatureIndex, altitudeIndex)
            : null;
    }

    private double ValueAt(int weightIndex, int temperatureIndex, int altitudeIndex)
    {
        int index =
            ((weightIndex * _temperaturesCelsius.Length) + temperatureIndex)
            * _pressureAltitudesFeet.Length
            + altitudeIndex;

        return _values[index];
    }

    private static Bracket? FindBracket(double[] axis, double value)
    {
        if (value < axis[0] || value > axis[^1])
            return null;

        int exact = Array.BinarySearch(axis, value);
        if (exact >= 0)
            return new(exact, exact, 0);

        int upper = ~exact;
        int lower = upper - 1;
        double fraction = (value - axis[lower]) / (axis[upper] - axis[lower]);

        return new(lower, upper, fraction);
    }

    private static double Lerp(double lower, double upper, double fraction) =>
        lower + ((upper - lower) * fraction);

    private static void ValidateAxis(
        double[] axis,
        string name,
        bool requireNonNegative)
    {
        if (axis.Length == 0)
            throw new ArgumentException("Performance table axes cannot be empty.", name);

        for (int index = 0; index < axis.Length; index++)
        {
            double value = axis[index];

            if (!double.IsFinite(value) || (requireNonNegative && value < 0))
                throw new ArgumentOutOfRangeException(name);

            if (index > 0 && value <= axis[index - 1])
            {
                throw new ArgumentException(
                    "Performance table axes must be strictly increasing.",
                    name);
            }
        }
    }

    private readonly record struct Bracket(int Lower, int Upper, double Fraction);
}

public sealed record AircraftCruisePerformanceProfile(
    int Index,
    string? ProfileName,
    int FuelTypeIndex,
    double? TargetMach,
    AircraftPerformanceGrid3D? TrueAirspeedKnots,
    AircraftPerformanceGrid3D? FuelConsumptionGallonsPerHour)
{
    public void Validate()
    {
        if (Index is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(Index));

        if (ProfileName is not null && string.IsNullOrWhiteSpace(ProfileName))
            throw new ArgumentException("Cruise profile name must be non-empty when supplied.", nameof(ProfileName));

        if (FuelTypeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(FuelTypeIndex));

        if (TargetMach is { } mach && (!double.IsFinite(mach) || mach < 0))
            throw new ArgumentOutOfRangeException(nameof(TargetMach));

        ValidateCruiseGrid(TrueAirspeedKnots, nameof(TrueAirspeedKnots));
        ValidateCruiseGrid(FuelConsumptionGallonsPerHour, nameof(FuelConsumptionGallonsPerHour));
    }

    private static void ValidateCruiseGrid(
        AircraftPerformanceGrid3D? grid,
        string name)
    {
        if (grid is null)
            return;

        grid.Validate();

        if (grid.TemperatureAxisKind
            != AircraftPerformanceTemperatureAxisKind.IsaDeviationCelsius)
        {
            throw new ArgumentException(
                "Cruise performance tables must use ISA-deviation temperature axes.",
                name);
        }
    }
}

public sealed record AircraftConditionedPerformanceProfile(
    AircraftPerformanceGrid3D? TakeoffGroundRollDistanceFeet,
    AircraftPerformanceGrid3D? TakeoffTotalDistanceFeet,
    AircraftPerformanceGrid3D? LandingGroundRollDistanceFeet,
    AircraftPerformanceGrid3D? LandingTotalDistanceFeet,
    IReadOnlyList<AircraftCruisePerformanceProfile> CruiseProfiles)
{
    public bool HasAny =>
        TakeoffGroundRollDistanceFeet is not null
        || TakeoffTotalDistanceFeet is not null
        || LandingGroundRollDistanceFeet is not null
        || LandingTotalDistanceFeet is not null
        || CruiseProfiles.Count > 0;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CruiseProfiles);

        ValidateDistanceGrid(TakeoffGroundRollDistanceFeet, nameof(TakeoffGroundRollDistanceFeet));
        ValidateDistanceGrid(TakeoffTotalDistanceFeet, nameof(TakeoffTotalDistanceFeet));
        ValidateDistanceGrid(LandingGroundRollDistanceFeet, nameof(LandingGroundRollDistanceFeet));
        ValidateDistanceGrid(LandingTotalDistanceFeet, nameof(LandingTotalDistanceFeet));

        var indices = new HashSet<int>();

        foreach (AircraftCruisePerformanceProfile profile in CruiseProfiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
            profile.Validate();

            if (!indices.Add(profile.Index))
                throw new ArgumentException("Cruise performance profile indices must be unique.", nameof(CruiseProfiles));
        }
    }

    private static void ValidateDistanceGrid(
        AircraftPerformanceGrid3D? grid,
        string name)
    {
        if (grid is null)
            return;

        grid.Validate();

        if (grid.TemperatureAxisKind
            != AircraftPerformanceTemperatureAxisKind.OutsideAirTemperatureCelsius)
        {
            throw new ArgumentException(
                "Takeoff and landing distance tables must use outside-air-temperature axes.",
                name);
        }
    }
}
