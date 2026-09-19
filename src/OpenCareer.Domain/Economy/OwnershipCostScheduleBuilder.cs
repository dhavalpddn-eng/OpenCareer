using OpenCareer.Domain.Finance;

namespace OpenCareer.Domain.Economy;

public sealed record OwnershipFixedCostProfile(
    decimal InsurancePerCycle,
    decimal StoragePerCycle,
    decimal OtherFixedCostsPerCycle = 0m)
{
    public void Validate()
    {
        Money.Validate(InsurancePerCycle, nameof(InsurancePerCycle));
        Money.Validate(StoragePerCycle, nameof(StoragePerCycle));
        Money.Validate(OtherFixedCostsPerCycle, nameof(OtherFixedCostsPerCycle));
    }
}

public static class OwnershipCostScheduleBuilder
{
    public static IReadOnlyList<RecurringOwnershipCostCycle> Build(
        AircraftLoanAgreement? loan,
        OwnershipFixedCostProfile fixedCosts,
        int totalCycles)
    {
        ArgumentNullException.ThrowIfNull(fixedCosts);
        fixedCosts.Validate();

        if (totalCycles < 1 || totalCycles > 2_000)
            throw new ArgumentOutOfRangeException(nameof(totalCycles));

        loan?.Validate();

        var cycles = new RecurringOwnershipCostCycle[totalCycles];

        for (int cycleIndex = 0; cycleIndex < totalCycles; cycleIndex++)
        {
            AircraftLoanCyclePayment? loanPayment =
                loan is not null
                    ? AircraftLoanAmortization.QuoteCycle(
                        loan,
                        cycleIndex)
                    : null;

            cycles[cycleIndex] = new RecurringOwnershipCostCycle(
                LoanPrincipal: loanPayment?.Principal ?? 0m,
                LoanInterest: loanPayment?.Interest ?? 0m,
                Insurance: fixedCosts.InsurancePerCycle,
                Storage: fixedCosts.StoragePerCycle,
                OtherFixedCosts: fixedCosts.OtherFixedCostsPerCycle);
            cycles[cycleIndex].Validate();
        }

        return cycles;
    }
}
