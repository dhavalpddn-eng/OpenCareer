namespace OpenCareer.Domain.Planning;

public sealed record RouteFuelReservePolicy(
    double ContingencyPercentOfTripFuel,
    double FinalReserveMinutesAtCruiseConsumption,
    double AdditionalReserveFuelGallons = 0)
{
    public void Validate()
    {
        if (!double.IsFinite(ContingencyPercentOfTripFuel)
            || ContingencyPercentOfTripFuel < 0
            || ContingencyPercentOfTripFuel > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ContingencyPercentOfTripFuel));
        }

        if (!double.IsFinite(FinalReserveMinutesAtCruiseConsumption)
            || FinalReserveMinutesAtCruiseConsumption < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FinalReserveMinutesAtCruiseConsumption));
        }

        if (!double.IsFinite(AdditionalReserveFuelGallons)
            || AdditionalReserveFuelGallons < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AdditionalReserveFuelGallons));
        }
    }
}

/// <summary>
/// Explicit airborne route inputs. Cruise distance is separate from total route
/// distance so cruise TAS/GPH are never silently applied to climb or descent.
/// Climb/descent fuel and time must be supplied by the caller from a separate
/// model or authoritative source.
/// </summary>
public sealed record RouteFuelPlanningRequest(
    double TotalRouteDistanceNauticalMiles,
    double CruiseSegmentDistanceNauticalMiles,
    double ClimbTimeMinutes,
    double DescentTimeMinutes,
    double ClimbFuelGallons,
    double DescentFuelGallons,
    RouteFuelReservePolicy ReservePolicy)
{
    public void Validate()
    {
        ValidateNonNegative(
            TotalRouteDistanceNauticalMiles,
            nameof(TotalRouteDistanceNauticalMiles));

        if (TotalRouteDistanceNauticalMiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(TotalRouteDistanceNauticalMiles));

        ValidateNonNegative(
            CruiseSegmentDistanceNauticalMiles,
            nameof(CruiseSegmentDistanceNauticalMiles));

        if (CruiseSegmentDistanceNauticalMiles <= 0
            || CruiseSegmentDistanceNauticalMiles > TotalRouteDistanceNauticalMiles)
        {
            throw new ArgumentOutOfRangeException(nameof(CruiseSegmentDistanceNauticalMiles));
        }

        ValidateNonNegative(ClimbTimeMinutes, nameof(ClimbTimeMinutes));
        ValidateNonNegative(DescentTimeMinutes, nameof(DescentTimeMinutes));
        ValidateNonNegative(ClimbFuelGallons, nameof(ClimbFuelGallons));
        ValidateNonNegative(DescentFuelGallons, nameof(DescentFuelGallons));

        ArgumentNullException.ThrowIfNull(ReservePolicy);
        ReservePolicy.Validate();
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record RouteFuelEstimate(
    CruisePerformancePlan CruisePerformance,
    double TotalRouteDistanceNauticalMiles,
    double CruiseSegmentDistanceNauticalMiles,
    double NonCruiseDistanceNauticalMiles,
    double ClimbTimeMinutes,
    double CruiseTimeMinutes,
    double DescentTimeMinutes,
    double EstimatedAirborneTimeMinutes,
    double ClimbFuelGallons,
    double CruiseFuelGallons,
    double DescentFuelGallons,
    double TripFuelGallons,
    double ContingencyFuelGallons,
    double FinalReserveFuelGallons,
    double AdditionalReserveFuelGallons,
    double RequiredFuelGallons)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CruisePerformance);
        CruisePerformance.Validate();

        ValidateNonNegative(TotalRouteDistanceNauticalMiles, nameof(TotalRouteDistanceNauticalMiles));
        ValidateNonNegative(CruiseSegmentDistanceNauticalMiles, nameof(CruiseSegmentDistanceNauticalMiles));
        ValidateNonNegative(NonCruiseDistanceNauticalMiles, nameof(NonCruiseDistanceNauticalMiles));
        ValidateNonNegative(ClimbTimeMinutes, nameof(ClimbTimeMinutes));
        ValidateNonNegative(CruiseTimeMinutes, nameof(CruiseTimeMinutes));
        ValidateNonNegative(DescentTimeMinutes, nameof(DescentTimeMinutes));
        ValidateNonNegative(EstimatedAirborneTimeMinutes, nameof(EstimatedAirborneTimeMinutes));
        ValidateNonNegative(ClimbFuelGallons, nameof(ClimbFuelGallons));
        ValidateNonNegative(CruiseFuelGallons, nameof(CruiseFuelGallons));
        ValidateNonNegative(DescentFuelGallons, nameof(DescentFuelGallons));
        ValidateNonNegative(TripFuelGallons, nameof(TripFuelGallons));
        ValidateNonNegative(ContingencyFuelGallons, nameof(ContingencyFuelGallons));
        ValidateNonNegative(FinalReserveFuelGallons, nameof(FinalReserveFuelGallons));
        ValidateNonNegative(AdditionalReserveFuelGallons, nameof(AdditionalReserveFuelGallons));
        ValidateNonNegative(RequiredFuelGallons, nameof(RequiredFuelGallons));
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

public enum RouteFuelPlanningStatus
{
    Planned = 0,
    InsufficientData = 1
}

public enum RouteFuelPlanningReason
{
    CruisePerformanceUnavailable = 0
}

public sealed record RouteFuelPlanningResult
{
    private RouteFuelPlanningResult(
        RouteFuelPlanningStatus status,
        RouteFuelPlanningReason? reason,
        CruisePerformancePlanningReason? cruisePerformanceReason,
        RouteFuelEstimate? estimate)
    {
        Status = status;
        Reason = reason;
        CruisePerformanceReason = cruisePerformanceReason;
        Estimate = estimate;
    }

    public RouteFuelPlanningStatus Status { get; }
    public RouteFuelPlanningReason? Reason { get; }
    public CruisePerformancePlanningReason? CruisePerformanceReason { get; }
    public RouteFuelEstimate? Estimate { get; }

    public static RouteFuelPlanningResult Planned(RouteFuelEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        estimate.Validate();

        return new(
            RouteFuelPlanningStatus.Planned,
            null,
            null,
            estimate);
    }

    public static RouteFuelPlanningResult InsufficientData(
        RouteFuelPlanningReason reason,
        CruisePerformancePlanningReason? cruisePerformanceReason = null)
    {
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        if (cruisePerformanceReason is { } cruiseReason
            && !Enum.IsDefined(cruiseReason))
        {
            throw new ArgumentOutOfRangeException(nameof(cruisePerformanceReason));
        }

        return new(
            RouteFuelPlanningStatus.InsufficientData,
            reason,
            cruisePerformanceReason,
            null);
    }
}

/// <summary>
/// Deterministic arithmetic over an already-resolved Slice 15 cruise plan.
/// This estimator does not derive climb/descent performance, fuel density,
/// alternates, regulatory minima, or fuel-capacity/weight authority.
/// </summary>
public static class RouteFuelEstimator
{
    public static RouteFuelEstimate Estimate(
        CruisePerformancePlan cruisePerformance,
        RouteFuelPlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(cruisePerformance);
        ArgumentNullException.ThrowIfNull(request);

        cruisePerformance.Validate();
        request.Validate();

        double cruiseTimeHours =
            request.CruiseSegmentDistanceNauticalMiles
            / cruisePerformance.TrueAirspeedKnots;

        double cruiseTimeMinutes = cruiseTimeHours * 60d;
        double cruiseFuelGallons =
            cruiseTimeHours * cruisePerformance.FuelConsumptionGallonsPerHour;

        double tripFuelGallons =
            request.ClimbFuelGallons
            + cruiseFuelGallons
            + request.DescentFuelGallons;

        double contingencyFuelGallons =
            tripFuelGallons
            * (request.ReservePolicy.ContingencyPercentOfTripFuel / 100d);

        double finalReserveFuelGallons =
            cruisePerformance.FuelConsumptionGallonsPerHour
            * (request.ReservePolicy.FinalReserveMinutesAtCruiseConsumption / 60d);

        double requiredFuelGallons =
            tripFuelGallons
            + contingencyFuelGallons
            + finalReserveFuelGallons
            + request.ReservePolicy.AdditionalReserveFuelGallons;

        double nonCruiseDistance =
            request.TotalRouteDistanceNauticalMiles
            - request.CruiseSegmentDistanceNauticalMiles;

        double estimatedAirborneMinutes =
            request.ClimbTimeMinutes
            + cruiseTimeMinutes
            + request.DescentTimeMinutes;

        ValidateFinite(
            cruiseTimeMinutes,
            cruiseFuelGallons,
            tripFuelGallons,
            contingencyFuelGallons,
            finalReserveFuelGallons,
            requiredFuelGallons,
            estimatedAirborneMinutes);

        var estimate = new RouteFuelEstimate(
            cruisePerformance,
            request.TotalRouteDistanceNauticalMiles,
            request.CruiseSegmentDistanceNauticalMiles,
            nonCruiseDistance,
            request.ClimbTimeMinutes,
            cruiseTimeMinutes,
            request.DescentTimeMinutes,
            estimatedAirborneMinutes,
            request.ClimbFuelGallons,
            cruiseFuelGallons,
            request.DescentFuelGallons,
            tripFuelGallons,
            contingencyFuelGallons,
            finalReserveFuelGallons,
            request.ReservePolicy.AdditionalReserveFuelGallons,
            requiredFuelGallons);

        estimate.Validate();
        return estimate;
    }

    private static void ValidateFinite(params double[] values)
    {
        if (values.Any(static value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(values),
                "Route/fuel calculation produced a non-finite or negative value.");
        }
    }
}
