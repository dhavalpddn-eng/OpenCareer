namespace OpenCareer.Domain.Aircraft;

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
    string? Source = null)
{
    public void Validate()
    {
        ValidateOptionalPositive(OperatingEmptyWeightPounds, nameof(OperatingEmptyWeightPounds));
        ValidateOptionalPositive(MaximumTakeoffWeightPounds, nameof(MaximumTakeoffWeightPounds));
        ValidateOptionalPositive(MaximumFuelWeightPounds, nameof(MaximumFuelWeightPounds));

        if (OperatingEmptyWeightPounds is { } empty
            && MaximumTakeoffWeightPounds is { } mtow
            && empty > mtow)
        {
            throw new ArgumentException(
                "Operating empty weight cannot exceed maximum takeoff weight.");
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

        AircraftPayloadRangePoint[] ordered = PayloadRangeEnvelope
            .OrderBy(static point => point.PayloadPounds)
            .ToArray();

        for (int i = 0; i < ordered.Length; i++)
        {
            AircraftPayloadRangePoint point = ordered[i]
                ?? throw new ArgumentException(
                    "Payload-range envelope cannot contain null points.",
                    nameof(PayloadRangeEnvelope));

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

    private static void ValidateOptionalPositive(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number <= 0))
            throw new ArgumentOutOfRangeException(name);
    }
}
