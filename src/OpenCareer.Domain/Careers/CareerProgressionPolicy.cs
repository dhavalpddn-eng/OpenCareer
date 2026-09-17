namespace OpenCareer.Domain.Careers;

public sealed record CareerProgressionPolicy(
    double TargetOwnershipHoursLow,
    double TargetOwnershipHoursHigh,
    decimal TypicalUsedLightAircraftPrice,
    decimal MinimumDownPaymentRate,
    decimal MinimumOperatingReserve,
    decimal TargetNetSavingsPerFlightHour)
{
    public static CareerProgressionPolicy Default { get; } = new(
        TargetOwnershipHoursLow: 50,
        TargetOwnershipHoursHigh: 80,
        TypicalUsedLightAircraftPrice: 60_000m,
        MinimumDownPaymentRate: 0.10m,
        MinimumOperatingReserve: 2_000m,
        TargetNetSavingsPerFlightHour: 125m);

    public decimal MinimumAcquisitionCash =>
        decimal.Round(TypicalUsedLightAircraftPrice * MinimumDownPaymentRate + MinimumOperatingReserve, 2);

    public double ExpectedHoursToAcquisition =>
        TargetNetSavingsPerFlightHour <= 0 ? double.PositiveInfinity :
        (double)(MinimumAcquisitionCash / TargetNetSavingsPerFlightHour);

    public void Validate()
    {
        if (!double.IsFinite(TargetOwnershipHoursLow) || !double.IsFinite(TargetOwnershipHoursHigh) || TargetOwnershipHoursLow <= 0 || TargetOwnershipHoursHigh < TargetOwnershipHoursLow)
            throw new ArgumentOutOfRangeException(nameof(TargetOwnershipHoursLow));
        if (TypicalUsedLightAircraftPrice <= 0 || MinimumDownPaymentRate is <= 0 or >= 1 ||
            MinimumOperatingReserve < 0 || TargetNetSavingsPerFlightHour <= 0)
            throw new ArgumentOutOfRangeException(nameof(TypicalUsedLightAircraftPrice));
        if (ExpectedHoursToAcquisition < TargetOwnershipHoursLow || ExpectedHoursToAcquisition > TargetOwnershipHoursHigh)
            throw new InvalidOperationException("Progression tuning falls outside the 50-80 hour ownership target.");
    }
}
