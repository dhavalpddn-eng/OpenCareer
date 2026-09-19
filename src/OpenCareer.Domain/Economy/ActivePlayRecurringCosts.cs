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

public sealed record ActivePlayBillingState(
    string OwnershipId,
    int CycleIndex,
    TimeSpan CycleProgress,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static ActivePlayBillingState Start(string ownershipId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        return new(
            ownershipId,
            CycleIndex: 0,
            CycleProgress: TimeSpan.Zero,
            Version: 0,
            UpdatedAt: DateTimeOffset.UnixEpoch);
    }

    public void Validate(ActivePlayRecurringCostPolicy? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ActivePlayRecurringCostPolicy effectivePolicy =
            policy ?? ActivePlayRecurringCostPolicy.Default;
        effectivePolicy.Validate();

        if (CycleIndex < 0
            || CycleProgress < TimeSpan.Zero
            || CycleProgress >= effectivePolicy.BillingCycleCareerCreditTime
            || Version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ActivePlayBillingState));
        }
    }
}

public sealed record ActivePlayRecurringCostAccrual(
    decimal LoanPrincipal,
    decimal LoanInterest,
    decimal Insurance,
    decimal Storage,
    decimal OtherFixedCosts,
    int CompletedCycles)
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
        if (CompletedCycles < 0)
            throw new ArgumentOutOfRangeException(nameof(CompletedCycles));
    }
}

public sealed record PersistedActivePlayRecurringCostSettlementSummary(
    string OwnershipId,
    string ActivityReferenceId,
    TimeSpan ActiveCareerCreditTime,
    ActivePlayBillingState StateBefore,
    ActivePlayBillingState StateAfter,
    ActivePlayRecurringCostAccrual Accrual,
    EconomyLedgerTransaction Transaction)
{
    public decimal TotalCost => Accrual.Total;

    public void Validate(ActivePlayRecurringCostPolicy? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ActivityReferenceId);
        if (ActiveCareerCreditTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ActiveCareerCreditTime));

        ArgumentNullException.ThrowIfNull(StateBefore);
        ArgumentNullException.ThrowIfNull(StateAfter);
        StateBefore.Validate(policy);
        StateAfter.Validate(policy);

        if (!string.Equals(StateBefore.OwnershipId, OwnershipId, StringComparison.Ordinal)
            || !string.Equals(StateAfter.OwnershipId, OwnershipId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Billing state belongs to a different ownership.");
        }

        long expectedVersion =
            ActiveCareerCreditTime > TimeSpan.Zero
                ? checked(StateBefore.Version + 1)
                : StateBefore.Version;
        if (StateAfter.Version != expectedVersion)
            throw new ArgumentException("Billing state version transition is invalid.");

        ArgumentNullException.ThrowIfNull(Accrual);
        Accrual.Validate();

        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();
        if (Transaction.CashChange != -TotalCost)
        {
            throw new ArgumentException(
                "Persisted recurring-cost settlement does not reconcile to the ledger cash change.");
        }
    }
}

public static class ActivePlayRecurringCostSettlementEngine
{
    public static string BuildPersistedIdempotencyKey(
        string ownershipId,
        string activityReferenceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityReferenceId);
        return $"ownership:{ownershipId}:activity:{activityReferenceId}:active-cost-v2";
    }

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

    public static PersistedActivePlayRecurringCostSettlementSummary CreatePersisted(
        Guid settlementId,
        string ownershipId,
        string activityReferenceId,
        ActivePlayBillingState stateBefore,
        FlightTimeLedger flightTime,
        IReadOnlyList<RecurringOwnershipCostCycle> costSchedule,
        DateTimeOffset settledAt,
        ActivePlayRecurringCostPolicy? policy = null)
    {
        if (settlementId == Guid.Empty)
            throw new ArgumentException("Settlement ID is required.", nameof(settlementId));

        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityReferenceId);
        ArgumentNullException.ThrowIfNull(stateBefore);
        ArgumentNullException.ThrowIfNull(flightTime);
        ArgumentNullException.ThrowIfNull(costSchedule);

        ActivePlayRecurringCostPolicy effectivePolicy =
            policy ?? ActivePlayRecurringCostPolicy.Default;
        effectivePolicy.Validate();
        stateBefore.Validate(effectivePolicy);

        if (!string.Equals(stateBefore.OwnershipId, ownershipId, StringComparison.Ordinal))
            throw new ArgumentException("Billing state belongs to a different ownership.", nameof(stateBefore));

        foreach (RecurringOwnershipCostCycle cycle in costSchedule)
        {
            ArgumentNullException.ThrowIfNull(cycle);
            cycle.Validate();
        }

        TimeSpan remaining = flightTime.CareerCreditTime;
        if (remaining < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(flightTime));

        int cycleIndex = stateBefore.CycleIndex;
        TimeSpan cycleProgress = stateBefore.CycleProgress;
        decimal loanPrincipal = 0m;
        decimal loanInterest = 0m;
        decimal insurance = 0m;
        decimal storage = 0m;
        decimal other = 0m;
        int completedCycles = 0;

        while (remaining > TimeSpan.Zero)
        {
            if (cycleIndex >= costSchedule.Count)
            {
                throw new InvalidOperationException(
                    "Recurring ownership cost schedule does not cover the active-play billing period.");
            }

            TimeSpan available =
                effectivePolicy.BillingCycleCareerCreditTime - cycleProgress;
            TimeSpan slice = remaining <= available
                ? remaining
                : available;

            ActivePlayRecurringCostAssessment assessment =
                effectivePolicy.Assess(
                    cycleProgress,
                    slice,
                    costSchedule[cycleIndex]);

            loanPrincipal = Money.Normalize(loanPrincipal + assessment.LoanPrincipal);
            loanInterest = Money.Normalize(loanInterest + assessment.LoanInterest);
            insurance = Money.Normalize(insurance + assessment.Insurance);
            storage = Money.Normalize(storage + assessment.Storage);
            other = Money.Normalize(other + assessment.OtherFixedCosts);

            remaining -= slice;
            if (assessment.CycleCompleted)
            {
                cycleIndex = checked(cycleIndex + 1);
                cycleProgress = TimeSpan.Zero;
                completedCycles = checked(completedCycles + 1);
            }
            else
            {
                cycleProgress = assessment.CycleProgressAfter;
            }
        }

        var accrual = new ActivePlayRecurringCostAccrual(
            loanPrincipal,
            loanInterest,
            insurance,
            storage,
            other,
            completedCycles);
        accrual.Validate();

        ActivePlayBillingState stateAfter =
            flightTime.CareerCreditTime > TimeSpan.Zero
                ? stateBefore with
                {
                    CycleIndex = cycleIndex,
                    CycleProgress = cycleProgress,
                    Version = checked(stateBefore.Version + 1),
                    UpdatedAt = settledAt
                }
                : stateBefore;
        stateAfter.Validate(effectivePolicy);

        var postings = new List<LedgerPosting>();
        AddCost(postings, LedgerAccountCode.LoanPayable, accrual.LoanPrincipal, "Aircraft loan principal");
        AddCost(postings, LedgerAccountCode.InterestExpense, accrual.LoanInterest, "Aircraft loan interest");
        AddCost(postings, LedgerAccountCode.InsuranceExpense, accrual.Insurance, "Aircraft insurance");
        AddCost(postings, LedgerAccountCode.StorageExpense, accrual.Storage, "Aircraft storage");
        AddCost(postings, LedgerAccountCode.OtherOperatingExpense, accrual.OtherFixedCosts, "Other fixed ownership cost");

        var transaction = new EconomyLedgerTransaction(
            settlementId,
            BuildPersistedIdempotencyKey(ownershipId, activityReferenceId),
            settledAt,
            $"Active-play ownership costs for {ownershipId}",
            "OwnershipActivePlayCosts",
            ownershipId,
            postings);
        transaction.Validate();

        var result = new PersistedActivePlayRecurringCostSettlementSummary(
            ownershipId,
            activityReferenceId,
            flightTime.CareerCreditTime,
            stateBefore,
            stateAfter,
            accrual,
            transaction);
        result.Validate(effectivePolicy);
        return result;
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
