using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Economy;

public interface IActivePlayBillingLedgerStore : IEconomyLedgerStore
{
    Task<ActivePlayBillingState> ReadActivePlayBillingStateAsync(
        string ownershipId,
        CancellationToken cancellationToken = default);

    Task<LedgerPostResult> PostActivePlayRecurringCostAsync(
        PersistedActivePlayRecurringCostSettlementSummary settlement,
        CancellationToken cancellationToken = default);
}

public sealed record ActivePlayRecurringCostSettlementResult(
    ActivePlayRecurringCostSettlementSummary Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed record PersistedActivePlayRecurringCostSettlementResult(
    PersistedActivePlayRecurringCostSettlementSummary Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter,
    ActivePlayBillingState CurrentBillingState)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class ActivePlayRecurringCostService(
    IActivePlayBillingLedgerStore ledgerStore)
{
    private readonly IActivePlayBillingLedgerStore _ledgerStore =
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

    public async Task<PersistedActivePlayRecurringCostSettlementResult> SettlePersistedAsync(
        Guid settlementId,
        string ownershipId,
        string activityReferenceId,
        FlightTimeLedger flightTime,
        IReadOnlyList<RecurringOwnershipCostCycle> costSchedule,
        DateTimeOffset settledAt,
        ActivePlayRecurringCostPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ActivePlayBillingState stateBefore =
            await _ledgerStore
                .ReadActivePlayBillingStateAsync(
                    ownershipId,
                    cancellationToken)
                .ConfigureAwait(false);

        PersistedActivePlayRecurringCostSettlementSummary settlement =
            ActivePlayRecurringCostSettlementEngine.CreatePersisted(
                settlementId,
                ownershipId,
                activityReferenceId,
                stateBefore,
                flightTime,
                costSchedule,
                settledAt,
                policy);

        LedgerPostResult postResult =
            await _ledgerStore
                .PostActivePlayRecurringCostAsync(
                    settlement,
                    cancellationToken)
                .ConfigureAwait(false);

        decimal balance =
            await _ledgerStore
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        ActivePlayBillingState currentState =
            await _ledgerStore
                .ReadActivePlayBillingStateAsync(
                    ownershipId,
                    cancellationToken)
                .ConfigureAwait(false);

        return new PersistedActivePlayRecurringCostSettlementResult(
            settlement,
            postResult,
            balance,
            currentState);
    }
}
