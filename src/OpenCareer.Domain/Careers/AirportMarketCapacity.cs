namespace OpenCareer.Domain.Careers;

public enum AirportMarketScale
{
    Local,
    Small,
    Regional,
    Large,
    MajorHub,
    MegaHub
}

/// <summary>
/// Career-market capacity is separate from physical runway feasibility. It can be calibrated from
/// real passenger/cargo/operations data plus curated airport specialties without pretending SimConnect
/// exposes airline, cargo-hub or military-economic labels.
/// </summary>
public sealed record AirportMarketCapacity(
    AirportMarketScale Scale,
    int MatureVisibleOfferCapacity,
    double PassengerActivityIndex,
    double CargoActivityIndex,
    double GeneralAviationActivityIndex)
{
    public static AirportMarketCapacity ForScale(AirportMarketScale scale) => scale switch
    {
        AirportMarketScale.Local => new(scale, 10, 0.10, 0.10, 0.65),
        AirportMarketScale.Small => new(scale, 16, 0.25, 0.20, 0.70),
        AirportMarketScale.Regional => new(scale, 20, 0.45, 0.35, 0.65),
        AirportMarketScale.Large => new(scale, 35, 0.70, 0.65, 0.45),
        AirportMarketScale.MajorHub => new(scale, 45, 0.90, 0.80, 0.30),
        AirportMarketScale.MegaHub => new(scale, 60, 1.00, 0.95, 0.25),
        _ => throw new ArgumentOutOfRangeException(nameof(scale))
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Scale))
            throw new ArgumentOutOfRangeException(nameof(Scale));
        if (MatureVisibleOfferCapacity is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(MatureVisibleOfferCapacity));
        ValidateUnit(PassengerActivityIndex, nameof(PassengerActivityIndex));
        ValidateUnit(CargoActivityIndex, nameof(CargoActivityIndex));
        ValidateUnit(GeneralAviationActivityIndex, nameof(GeneralAviationActivityIndex));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}
