namespace OpenCareer.Domain.Aircraft;

public sealed record AircraftFuelDensity(
    int FuelTypeIndex,
    double PoundsPerGallon)
{
    public void Validate()
    {
        if (FuelTypeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(FuelTypeIndex));

        if (!double.IsFinite(PoundsPerGallon) || PoundsPerGallon <= 0)
            throw new ArgumentOutOfRangeException(nameof(PoundsPerGallon));
    }
}

/// <summary>
/// One authoritative point on an aircraft payload-range envelope.
/// Payload is mission payload. Range is the maximum supported range at that payload.
/// </summary>
public sealed record AircraftPayloadRangePoint(
    double PayloadPounds,
    double MaximumRangeNauticalMiles)
{
    public void Validate()
    {
        if (!double.IsFinite(PayloadPounds) || PayloadPounds < 0)
            throw new ArgumentOutOfRangeException(nameof(PayloadPounds));

        if (!double.IsFinite(MaximumRangeNauticalMiles) || MaximumRangeNauticalMiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumRangeNauticalMiles));
    }
}

/// <summary>
/// Optional weight/fuel and payload-range data used only when a provider can
/// substantiate it. Operating empty weight means ready-for-operation aircraft
/// weight excluding usable fuel and mission payload.
/// </summary>
public sealed record AircraftDispatchPerformanceProfile(
    double? OperatingEmptyWeightPounds,
    double? MaximumTakeoffWeightPounds,
    double? MaximumFuelWeightPounds,
    IReadOnlyList<AircraftPayloadRangePoint>? PayloadRangeEnvelope,
    AircraftDataConfidence Confidence,
    string? Source = null,
    double? ConfiguredEmptyWeightPounds = null,
    double? MaximumLandingWeightPounds = null,
    double? MaximumZeroFuelWeightPounds = null,
    AircraftConditionedPerformanceProfile? ConditionedPerformance = null,
    double? FuelCapacityGallons = null,
    IReadOnlyList<AircraftFuelDensity>? FuelDensities = null)
{
    public void Validate()
    {
        ValidateOptionalPositive(OperatingEmptyWeightPounds, nameof(OperatingEmptyWeightPounds));
        ValidateOptionalPositive(MaximumTakeoffWeightPounds, nameof(MaximumTakeoffWeightPounds));
        ValidateOptionalNonNegative(MaximumFuelWeightPounds, nameof(MaximumFuelWeightPounds));
        ValidateOptionalPositive(ConfiguredEmptyWeightPounds, nameof(ConfiguredEmptyWeightPounds));
        ValidateOptionalPositive(MaximumLandingWeightPounds, nameof(MaximumLandingWeightPounds));
        ValidateOptionalPositive(MaximumZeroFuelWeightPounds, nameof(MaximumZeroFuelWeightPounds));
        ValidateOptionalNonNegative(FuelCapacityGallons, nameof(FuelCapacityGallons));

        if (OperatingEmptyWeightPounds is { } empty
            && MaximumTakeoffWeightPounds is { } mtow
            && empty > mtow)
        {
            throw new ArgumentException(
                "Operating empty weight cannot exceed maximum takeoff weight.");
        }

        if (ConfiguredEmptyWeightPounds is { } configuredEmpty
            && MaximumTakeoffWeightPounds is { } configuredMtow
            && configuredEmpty > configuredMtow)
        {
            throw new ArgumentException(
                "Configured empty weight cannot exceed maximum takeoff weight.");
        }

        ConditionedPerformance?.Validate();

        if (FuelDensities is not null)
        {
            if (FuelDensities.Any(static density => density is null))
                throw new ArgumentException("Fuel densities cannot contain null entries.", nameof(FuelDensities));

            foreach (AircraftFuelDensity density in FuelDensities)
                density.Validate();

            if (FuelDensities
                .GroupBy(static density => density.FuelTypeIndex)
                .Any(static group => group.Count() > 1))
            {
                throw new ArgumentException(
                    "Fuel density indices must be unique.",
                    nameof(FuelDensities));
            }
        }

        if (!Enum.IsDefined(Confidence))
            throw new ArgumentOutOfRangeException(nameof(Confidence));

        if (Source is not null && string.IsNullOrWhiteSpace(Source))
            throw new ArgumentException("Dispatch performance source must be non-empty when supplied.", nameof(Source));

        if (PayloadRangeEnvelope is null)
            return;

        if (PayloadRangeEnvelope.Count < 2)
            throw new ArgumentException(
                "Payload-range envelope requires at least two points.",
                nameof(PayloadRangeEnvelope));

        if (PayloadRangeEnvelope.Any(static point => point is null))
        {
            throw new ArgumentException(
                "Payload-range envelope cannot contain null points.",
                nameof(PayloadRangeEnvelope));
        }

        AircraftPayloadRangePoint[] ordered = PayloadRangeEnvelope
            .OrderBy(static point => point.PayloadPounds)
            .ToArray();

        for (int i = 0; i < ordered.Length; i++)
        {
            AircraftPayloadRangePoint point = ordered[i];
            point.Validate();

            if (i == 0)
                continue;

            AircraftPayloadRangePoint previous = ordered[i - 1];

            if (point.PayloadPounds <= previous.PayloadPounds)
            {
                throw new ArgumentException(
                    "Payload-range payload values must be unique and strictly increasing.",
                    nameof(PayloadRangeEnvelope));
            }

            if (point.MaximumRangeNauticalMiles > previous.MaximumRangeNauticalMiles)
            {
                throw new ArgumentException(
                    "Payload-range maximum range cannot increase as payload increases.",
                    nameof(PayloadRangeEnvelope));
            }
        }
    }

    public double? ResolveFuelDensityPoundsPerGallon(int fuelTypeIndex)
    {
        if (fuelTypeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(fuelTypeIndex));

        Validate();

        return FuelDensities?
            .SingleOrDefault(density => density.FuelTypeIndex == fuelTypeIndex)?
            .PoundsPerGallon;
    }

    public double? ResolveMaximumFuelWeightPounds(int? fuelTypeIndex = null)
    {
        Validate();

        double? typeSpecificMaximum = null;

        if (fuelTypeIndex is { } index
            && FuelCapacityGallons is { } capacity
            && ResolveFuelDensityPoundsPerGallon(index) is { } density)
        {
            double calculated = capacity * density;
            if (double.IsFinite(calculated))
                typeSpecificMaximum = calculated;
        }

        return (MaximumFuelWeightPounds, typeSpecificMaximum) switch
        {
            ({ } knownMaximum, { } selectedMaximum) =>
                Math.Min(knownMaximum, selectedMaximum),
            ({ } knownMaximum, null) => knownMaximum,
            (null, { } selectedMaximum) => selectedMaximum,
            _ => null
        };
    }

    private static void ValidateOptionalPositive(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number <= 0))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateOptionalNonNegative(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
            throw new ArgumentOutOfRangeException(name);
    }
}
