using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Planning;

public enum FuelWeightConversionStatus
{
    Converted = 0,
    InsufficientData = 1
}

public enum FuelWeightConversionReason
{
    FuelDensityDataUnavailable = 0,
    FuelTypeDensityUnavailable = 1
}

public sealed record RouteFuelWeightPlan(
    int FuelTypeIndex,
    double FuelDensityPoundsPerGallon,
    double RequiredFuelGallons,
    double RequiredFuelPounds,
    double? MaximumFuelGallons,
    double? MaximumFuelPounds)
{
    public void Validate()
    {
        if (FuelTypeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(FuelTypeIndex));

        if (!double.IsFinite(FuelDensityPoundsPerGallon)
            || FuelDensityPoundsPerGallon <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FuelDensityPoundsPerGallon));
        }

        ValidateNonNegative(RequiredFuelGallons, nameof(RequiredFuelGallons));
        ValidateNonNegative(RequiredFuelPounds, nameof(RequiredFuelPounds));

        if (MaximumFuelGallons is { } gallons)
            ValidateNonNegative(gallons, nameof(MaximumFuelGallons));

        if (MaximumFuelPounds is { } pounds)
            ValidateNonNegative(pounds, nameof(MaximumFuelPounds));
    }

    public OperationDispatchRequirements ApplyTo(
        OperationDispatchRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        requirements.Validate();
        Validate();

        return requirements with
        {
            PlannedFuelPounds = RequiredFuelPounds,
            PlannedFuelTypeIndex = FuelTypeIndex
        };
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record FuelWeightConversionResult
{
    private FuelWeightConversionResult(
        FuelWeightConversionStatus status,
        FuelWeightConversionReason? reason,
        RouteFuelWeightPlan? plan)
    {
        Status = status;
        Reason = reason;
        Plan = plan;
    }

    public FuelWeightConversionStatus Status { get; }
    public FuelWeightConversionReason? Reason { get; }
    public RouteFuelWeightPlan? Plan { get; }

    public static FuelWeightConversionResult Converted(RouteFuelWeightPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        return new(FuelWeightConversionStatus.Converted, null, plan);
    }

    public static FuelWeightConversionResult InsufficientData(
        FuelWeightConversionReason reason)
    {
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        return new(FuelWeightConversionStatus.InsufficientData, reason, null);
    }
}

/// <summary>
/// Converts the Slice 16 gallons requirement to pounds using only the fuel
/// density whose index exactly matches the selected Slice 15 cruise profile.
/// </summary>
public static class RouteFuelWeightBridge
{
    public static FuelWeightConversionResult Convert(
        RouteFuelEstimate estimate,
        AircraftDispatchPerformanceProfile dispatchPerformance)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(dispatchPerformance);

        estimate.Validate();
        dispatchPerformance.Validate();

        IReadOnlyList<AircraftFuelDensity>? densities =
            dispatchPerformance.FuelDensities;

        if (densities is null || densities.Count == 0)
        {
            return FuelWeightConversionResult.InsufficientData(
                FuelWeightConversionReason.FuelDensityDataUnavailable);
        }

        int fuelTypeIndex = estimate.CruisePerformance.FuelTypeIndex;
        AircraftFuelDensity? density = densities.SingleOrDefault(
            candidate => candidate.FuelTypeIndex == fuelTypeIndex);

        if (density is null)
        {
            return FuelWeightConversionResult.InsufficientData(
                FuelWeightConversionReason.FuelTypeDensityUnavailable);
        }

        double requiredFuelPounds =
            estimate.RequiredFuelGallons * density.PoundsPerGallon;

        if (!double.IsFinite(requiredFuelPounds))
            throw new ArgumentOutOfRangeException(nameof(estimate));

        double? maximumFuelGallons = dispatchPerformance.FuelCapacityGallons;
        double? maximumFuelPounds =
            dispatchPerformance.ResolveMaximumFuelWeightPounds(fuelTypeIndex);

        return FuelWeightConversionResult.Converted(
            new(
                fuelTypeIndex,
                density.PoundsPerGallon,
                estimate.RequiredFuelGallons,
                requiredFuelPounds,
                maximumFuelGallons,
                maximumFuelPounds));
    }
}
