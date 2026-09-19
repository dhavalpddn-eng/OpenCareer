using OpenCareer.Domain.Flights;

namespace OpenCareer.Domain.Economy;

public sealed record RecurringOwnershipCostCycle(
    decimal LoanPrincipal,
    decimal LoanInterest,
    decimal Insurance,
    decimal Storage,
    decimal OtherFixedCosts = 0m)
{
    public decimal Total =>
        Money.Normalize(
            LoanPrincipal
            + LoanInterest
            + Insurance
            + Storage
            + OtherFixedCosts);

    public void Validate()
    {
        Money.Validate(LoanPrincipal, nameof(LoanPrincipal));
        Money.Validate(LoanInterest, nameof(LoanInterest));
        Money.Validate(Insurance, nameof(Insurance));
        Money.Validate(Storage, nameof(Storage));
        Money.Validate(OtherFixedCosts, nameof(OtherFixedCosts));
    }
}

public sealed record ActivePlayRecurringCostPolicy(
    TimeSpan BillingCycleCareerCreditTime)
{
    public static ActivePlayRecurringCostPolicy Default { get; } =
        new(TimeSpan.FromHours(30));

    public ActivePlayRecurringCostAssessment Assess(
        TimeSpan cycleProgressBefore,
        TimeSpan activeCareerCreditTime,
        RecurringOwnershipCostCycle costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        costs.Validate();
        Validate();

        if (cycleProgressBefore < TimeSpan.Zero
            || cycleProgressBefore >= BillingCycleCareerCreditTime)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleProgressBefore));
        }

        if (activeCareerCreditTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(activeCareerCreditTime));

        TimeSpan cycleProgressAfter =
            cycleProgressBefore + activeCareerCreditTime;

        if (cycleProgressAfter > BillingCycleCareerCreditTime)
        {
            throw new InvalidOperationException(
                "Active-play recurring cost settlement cannot cross a billing-cycle boundary. Split the activity at the boundary and quote the next cycle separately.");
        }

        decimal loanPrincipal = IncrementalAccrual(
            costs.LoanPrincipal,
            cycleProgressBefore,
            cycleProgressAfter);
        decimal loanInterest = IncrementalAccrual(
            costs.LoanInterest,
            cycleProgressBefore,
            cycleProgressAfter);
        decimal insurance = IncrementalAccrual(
            costs.Insurance,
            cycleProgressBefore,
            cycleProgressAfter);
        decimal storage = IncrementalAccrual(
            costs.Storage,
            cycleProgressBefore,
            cycleProgressAfter);
        decimal other = IncrementalAccrual(
            costs.OtherFixedCosts,
            cycleProgressBefore,
            cycleProgressAfter);

        return new ActivePlayRecurringCostAssessment(
            cycleProgressBefore,
            cycleProgressAfter,
            cycleProgressAfter == BillingCycleCareerCreditTime,
            loanPrincipal,
            loanInterest,
            insurance,
            storage,
            other);
    }

    public void Validate()
    {
        if (BillingCycleCareerCreditTime <= TimeSpan.Zero
            || BillingCycleCareerCreditTime > TimeSpan.FromHours(1_000))
        {
            throw new ArgumentOutOfRangeException(nameof(BillingCycleCareerCreditTime));
        }
    }

    private decimal IncrementalAccrual(
        decimal fullCycleAmount,
        TimeSpan before,
        TimeSpan after)
    {
        decimal accruedBefore = AccruedTo(fullCycleAmount, before);
        decimal accruedAfter = AccruedTo(fullCycleAmount, after);
        return Money.Normalize(accruedAfter - accruedBefore);
    }

    private decimal AccruedTo(
        decimal fullCycleAmount,
        TimeSpan progress)
    {
        if (fullCycleAmount == 0m || progress == TimeSpan.Zero)
            return 0m;

        decimal fraction =
            (decimal)progress.Ticks
            / BillingCycleCareerCreditTime.Ticks;

        return Money.Normalize(fullCycleAmount * fraction);
    }
}

public sealed record ActivePlayRecurringCostAssessment(
    TimeSpan CycleProgressBefore,
    TimeSpan CycleProgressAfter,
    bool CycleCompleted,
    decimal LoanPrincipal,
    decimal LoanInterest,
    decimal Insurance,
    decimal Storage,
    decimal OtherFixedCosts)
{
    public decimal Total =>
        Money.Normalize(
            LoanPrincipal
            + LoanInterest
            + Insurance
            + Storage
            + OtherFixedCosts);

    public void Validate()
    {
        if (CycleProgressBefore < TimeSpan.Zero
            || CycleProgressAfter < CycleProgressBefore)
        {
            throw new ArgumentException("Invalid active-play billing-cycle progress.");
        }

        Money.Validate(LoanPrincipal, nameof(LoanPrincipal));
        Money.Validate(LoanInterest, nameof(LoanInterest));
        Money.Validate(Insurance, nameof(Insurance));
        Money.Validate(Storage, nameof(Storage));
        Money.Validate(OtherFixedCosts, nameof(OtherFixedCosts));
    }
}

public sealed record ActivePlayRecurringCostSettlementSummary(
    string OwnershipId,
    string ActivityReferenceId,
    TimeSpan ActiveCareerCreditTime,
    ActivePlayRecurringCostAssessment Assessment,
    EconomyLedgerTransaction Transaction)
{
    public decimal TotalCost => Assessment.Total;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActivityReferenceId);

        if (ActiveCareerCreditTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ActiveCareerCreditTime));

        ArgumentNullException.ThrowIfNull(Assessment);
        Assessment.Validate();

        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.CashChange != -TotalCost)
        {
            throw new ArgumentException(
                "Recurring-cost settlement does not reconcile to the ledger cash change.");
        }
    }
}

public static class ActivePlayRecurringCostSettlementEngine
{
    public static ActivePlayRecurringCostSettlementSummary Create(
        Guid settlementId,
        string ownershipId,
        string activityReferenceId,
        TimeSpan cycleProgressBefore,
        FlightTimeLedger flightTime,
        RecurringOwnershipCostCycle costs,
        DateTimeOffset settledAt,
        ActivePlayRecurringCostPolicy? policy = null)
    {
        if (settlementId == Guid.Empty)
            throw new ArgumentException("Settlement ID is required.", nameof(settlementId));

        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityReferenceId);
        ArgumentNullException.ThrowIfNull(flightTime);
        ArgumentNullException.ThrowIfNull(costs);

        ActivePlayRecurringCostPolicy effectivePolicy =
            policy ?? ActivePlayRecurringCostPolicy.Default;

        ActivePlayRecurringCostAssessment assessment =
            effectivePolicy.Assess(
                cycleProgressBefore,
                flightTime.CareerCreditTime,
                costs);

        var postings = new List<LedgerPosting>();

        AddCost(
            postings,
            LedgerAccountCode.LoanPayable,
            assessment.LoanPrincipal,
            "Aircraft loan principal");
        AddCost(
            postings,
            LedgerAccountCode.InterestExpense,
            assessment.LoanInterest,
            "Aircraft loan interest");
        AddCost(
            postings,
            LedgerAccountCode.InsuranceExpense,
            assessment.Insurance,
            "Aircraft insurance");
        AddCost(
            postings,
            LedgerAccountCode.StorageExpense,
            assessment.Storage,
            "Aircraft storage");
        AddCost(
            postings,
            LedgerAccountCode.OtherOperatingExpense,
            assessment.OtherFixedCosts,
            "Other fixed ownership cost");

        var transaction = new EconomyLedgerTransaction(
            settlementId,
            $"ownership:{ownershipId}:activity:{activityReferenceId}:active-cost-v1",
            settledAt,
            $"Active-play ownership costs for {ownershipId}",
            "OwnershipActivePlayCosts",
            ownershipId,
            postings);

        transaction.Validate();

        var summary = new ActivePlayRecurringCostSettlementSummary(
            ownershipId,
            activityReferenceId,
            flightTime.CareerCreditTime,
            assessment,
            transaction);

        summary.Validate();
        return summary;
    }

    private static void AddCost(
        ICollection<LedgerPosting> postings,
        LedgerAccountCode account,
        decimal amount,
        string memo)
    {
        if (amount <= 0m)
            return;

        postings.Add(
            LedgerPosting.DebitTo(
                account,
                amount,
                memo));
        postings.Add(
            LedgerPosting.CreditTo(
                LedgerAccountCode.Cash,
                amount,
                memo));
    }
}
