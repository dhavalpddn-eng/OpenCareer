using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Economy;

public sealed record ActivePlayRecurringCostSettlementResult(
    ActivePlayRecurringCostSettlementSummary Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class ActivePlayRecurringCostService(
    IEconomyLedgerStore ledgerStore)
{
    private readonly IEconomyLedgerStore _ledgerStore =
        ledgerStore ?? throw new ArgumentNullException(nameof(ledgerStore));

    public async Task<ActivePlayRecurringCostSettlementResult> SettleAsync(
        Guid settlementId,
        string ownershipId,
        string activityReferenceId,
        TimeSpan cycleProgressBefore,
        FlightTimeLedger flightTime,
        RecurringOwnershipCostCycle costs,
        DateTimeOffset settledAt,
        ActivePlayRecurringCostPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ActivePlayRecurringCostSettlementSummary settlement =
            ActivePlayRecurringCostSettlementEngine.Create(
                settlementId,
                ownershipId,
                activityReferenceId,
                cycleProgressBefore,
                flightTime,
                costs,
                settledAt,
                policy);

        LedgerPostResult postResult =
            await _ledgerStore
                .PostAsync(
                    settlement.Transaction,
                    cancellationToken)
                .ConfigureAwait(false);

        decimal balance =
            await _ledgerStore
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        return new ActivePlayRecurringCostSettlementResult(
            settlement,
            postResult,
            balance);
    }
}
