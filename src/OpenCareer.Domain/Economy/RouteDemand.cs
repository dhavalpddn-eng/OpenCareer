namespace OpenCareer.Domain.Economy;

public enum CargoCommodityCategory
{
    GeneralFreight,
    ExpressParcel,
    Mail,
    Perishables,
    MedicalSupplies,
    AircraftAogParts,
    IndustrialParts,
    ElectronicsHighValue,
    HumanitarianSupplies,
    GovernmentMilitaryLogistics
}

public enum PassengerDemandPurpose
{
    General,
    Business,
    Leisure,
    VisitingFriendsAndFamily,
    MajorEvent,
    Seasonal,
    Evacuation,
    Recovery
}

public sealed record DemandPressure(
    double DemandIndex,
    double AvailableCapacityIndex,
    double Trend,
    double Urgency)
{
    public double PressureRatio =>
        DemandIndex
        / Math.Max(AvailableCapacityIndex, 0.1);

    public double Attractiveness
    {
        get
        {
            Validate();

            double pressureComponent =
                0.6 + 0.4 * Math.Sqrt(PressureRatio);
            double trendComponent =
                1 + 0.35 * Trend;
            double urgencyComponent =
                1 + 0.4 * Urgency;

            return Math.Clamp(
                pressureComponent
                * trendComponent
                * urgencyComponent,
                0.25,
                4.0);
        }
    }

    public void Validate()
    {
        if (!double.IsFinite(DemandIndex)
            || DemandIndex is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DemandIndex));
        }

        if (!double.IsFinite(AvailableCapacityIndex)
            || AvailableCapacityIndex is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AvailableCapacityIndex));
        }

        if (!double.IsFinite(Trend)
            || Trend is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Trend));
        }

        if (!double.IsFinite(Urgency)
            || Urgency is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Urgency));
        }
    }
}

public sealed record CargoCommodityDemand(
    CargoCommodityCategory Category,
    DemandPressure Pressure,
    double PayloadAvailabilityIndex = 1)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Category))
            throw new ArgumentOutOfRangeException(nameof(Category));

        ArgumentNullException.ThrowIfNull(Pressure);
        Pressure.Validate();

        if (!double.IsFinite(PayloadAvailabilityIndex)
            || PayloadAvailabilityIndex is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PayloadAvailabilityIndex));
        }
    }
}

public sealed record PassengerRouteDemand(
    PassengerDemandPurpose Purpose,
    DemandPressure Pressure)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Purpose))
            throw new ArgumentOutOfRangeException(nameof(Purpose));

        ArgumentNullException.ThrowIfNull(Pressure);
        Pressure.Validate();
    }
}

public sealed record RouteDemandProfile(
    string OriginIcao,
    string DestinationIcao,
    PassengerRouteDemand Passenger,
    IReadOnlyList<CargoCommodityDemand> Cargo,
    DateTimeOffset UpdatedAt)
{
    public double PassengerAttractiveness
    {
        get
        {
            Validate();
            return Passenger.Pressure.Attractiveness;
        }
    }

    public double CargoAttractiveness(
        CargoCommodityCategory? category = null)
    {
        Validate();

        IReadOnlyList<CargoCommodityDemand> candidates =
            category is null
                ? Cargo
                : Cargo
                    .Where(x => x.Category == category.Value)
                    .ToArray();

        if (candidates.Count == 0)
            return 0.25;

        double weighted =
            candidates.Sum(
                x =>
                    x.Pressure.Attractiveness
                    * Math.Max(
                        x.PayloadAvailabilityIndex,
                        0.05));

        double weights =
            candidates.Sum(
                x =>
                    Math.Max(
                        x.PayloadAvailabilityIndex,
                        0.05));

        return Math.Clamp(
            weighted / weights,
            0.25,
            4.0);
    }

    public void Validate()
    {
        ValidateIcao(
            OriginIcao,
            nameof(OriginIcao));
        ValidateIcao(
            DestinationIcao,
            nameof(DestinationIcao));

        if (string.Equals(
                OriginIcao,
                DestinationIcao,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Route demand requires distinct origin and destination airports.");
        }

        ArgumentNullException.ThrowIfNull(Passenger);
        ArgumentNullException.ThrowIfNull(Cargo);

        Passenger.Validate();

        if (Cargo
            .Select(x => x.Category)
            .Distinct()
            .Count() != Cargo.Count)
        {
            throw new ArgumentException(
                "Cargo commodity categories must be unique per route.",
                nameof(Cargo));
        }

        foreach (CargoCommodityDemand demand in Cargo)
            demand.Validate();
    }

    private static void ValidateIcao(
        string value,
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != 4
            || value.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A normalized four-letter ICAO identifier is required.",
                name);
        }
    }
}
