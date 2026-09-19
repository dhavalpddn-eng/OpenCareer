namespace OpenCareer.Domain.Finance;

public sealed record AircraftLoanAgreement(
    Guid LoanId,
    string OwnershipId,
    string LenderId,
    decimal OriginalPrincipal,
    decimal AnnualRate,
    int TermCycles,
    decimal ScheduledPayment,
    DateTimeOffset OriginatedAt)
{
    public void Validate()
    {
        if (LoanId == Guid.Empty)
            throw new ArgumentException("Loan ID is required.", nameof(LoanId));

        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(LenderId);

        if (OriginalPrincipal <= 0m
            || AnnualRate is < 0m or > 0.5m
            || TermCycles is < 1 or > 240
            || ScheduledPayment <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(AircraftLoanAgreement));
        }

        if (RoundMoney(OriginalPrincipal) != OriginalPrincipal
            || RoundMoney(ScheduledPayment) != ScheduledPayment)
        {
            throw new ArgumentException("Loan money values must be expressed to whole cents.");
        }

        decimal firstCycleInterest =
            RoundMoney(OriginalPrincipal * AnnualRate / 12m);

        if (ScheduledPayment <= firstCycleInterest && AnnualRate > 0m)
        {
            throw new ArgumentException(
                "Scheduled payment must amortize principal rather than create negative amortization.");
        }
    }

    public static AircraftLoanAgreement FromApprovedDecision(
        Guid loanId,
        string ownershipId,
        string lenderId,
        LoanDecision decision,
        int termCycles,
        DateTimeOffset originatedAt)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!decision.Approved
            || decision.Reason != LoanDeclineReason.None
            || decision.RequestedPrincipal <= 0m
            || decision.MonthlyPayment <= 0m)
        {
            throw new InvalidOperationException("Only an approved financed purchase can originate an aircraft loan.");
        }

        var agreement = new AircraftLoanAgreement(
            loanId,
            ownershipId,
            lenderId,
            RoundMoney(decision.RequestedPrincipal),
            decision.AnnualRate,
            termCycles,
            RoundMoney(decision.MonthlyPayment),
            originatedAt);

        agreement.Validate();
        return agreement;
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record AircraftLoanCyclePayment(
    int CycleIndex,
    decimal BalanceBefore,
    decimal Interest,
    decimal Principal,
    decimal TotalPayment,
    decimal BalanceAfter,
    bool IsFinalPayment)
{
    public void Validate()
    {
        if (CycleIndex < 0
            || BalanceBefore < 0m
            || Interest < 0m
            || Principal < 0m
            || TotalPayment < 0m
            || BalanceAfter < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(AircraftLoanCyclePayment));
        }

        if (decimal.Round(Interest + Principal, 2, MidpointRounding.AwayFromZero) != TotalPayment)
            throw new ArgumentException("Loan payment principal and interest do not reconcile.");

        if (decimal.Round(BalanceBefore - Principal, 2, MidpointRounding.AwayFromZero) != BalanceAfter)
            throw new ArgumentException("Loan payment does not reconcile to remaining principal.");
    }
}

public static class AircraftLoanAmortization
{
    public static AircraftLoanCyclePayment QuoteCycle(
        AircraftLoanAgreement agreement,
        int cycleIndex)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        agreement.Validate();

        if (cycleIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cycleIndex));

        decimal balance = agreement.OriginalPrincipal;

        for (int cycle = 0; cycle < cycleIndex && balance > 0m; cycle++)
            balance = Calculate(agreement, cycle, balance).BalanceAfter;

        if (balance <= 0m || cycleIndex >= agreement.TermCycles)
        {
            return new AircraftLoanCyclePayment(
                cycleIndex,
                0m,
                0m,
                0m,
                0m,
                0m,
                IsFinalPayment: true);
        }

        return Calculate(agreement, cycleIndex, balance);
    }

    public static decimal RemainingPrincipalAfterCycles(
        AircraftLoanAgreement agreement,
        int completedCycles)
    {
        if (completedCycles < 0)
            throw new ArgumentOutOfRangeException(nameof(completedCycles));

        return QuoteCycle(agreement, completedCycles).BalanceBefore;
    }

    private static AircraftLoanCyclePayment Calculate(
        AircraftLoanAgreement agreement,
        int cycleIndex,
        decimal balanceBefore)
    {
        decimal interest =
            RoundMoney(balanceBefore * agreement.AnnualRate / 12m);

        decimal totalDue =
            RoundMoney(balanceBefore + interest);

        decimal payment =
            cycleIndex == agreement.TermCycles - 1
                ? totalDue
                : Math.Min(agreement.ScheduledPayment, totalDue);

        decimal principal =
            RoundMoney(payment - interest);

        if (principal < 0m)
            throw new InvalidOperationException("Loan payment cannot increase principal.");

        decimal balanceAfter =
            RoundMoney(Math.Max(0m, balanceBefore - principal));

        bool final =
            balanceAfter == 0m
            || cycleIndex == agreement.TermCycles - 1;

        if (final && balanceAfter != 0m)
        {
            principal = balanceBefore;
            payment = RoundMoney(principal + interest);
            balanceAfter = 0m;
        }

        var result = new AircraftLoanCyclePayment(
            cycleIndex,
            balanceBefore,
            interest,
            principal,
            payment,
            balanceAfter,
            final);
        result.Validate();
        return result;
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
