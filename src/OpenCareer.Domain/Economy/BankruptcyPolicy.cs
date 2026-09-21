namespace OpenCareer.Domain.Economy;

public enum FinancialRecoveryStage { Healthy, Warning, Restructuring, BankruptcyEligible }

public static class BankruptcyPolicy
{
    // Eligibility is not automatic liquidation. The application must offer employee-work recovery.
    // Only missed obligations from active play count; protected absence never increments this input.
    public static FinancialRecoveryStage Assess(int consecutiveActiveMisses, decimal overdueBalance,
        decimal realizableAssets, decimal totalDebt, bool recoveryOffered)
    {
        if (consecutiveActiveMisses < 0 || overdueBalance < 0 || realizableAssets < 0 || totalDebt < overdueBalance)
            throw new ArgumentOutOfRangeException(nameof(consecutiveActiveMisses));
        if (overdueBalance == 0) return FinancialRecoveryStage.Healthy;
        if (consecutiveActiveMisses < 2) return FinancialRecoveryStage.Warning;
        if (consecutiveActiveMisses >= 3 && totalDebt > realizableAssets && recoveryOffered)
            return FinancialRecoveryStage.BankruptcyEligible;
        return FinancialRecoveryStage.Restructuring;
    }
}
