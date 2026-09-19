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
    PersistedActivePlayRecurringCostSettlementSummary? Settlement,
    EconomyLedgerTransaction Transaction,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter,
    ActivePlayBillingState CurrentBillingState)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;

    public decimal TotalCost =>
        -Transaction.CashChange;
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
        CancellationToken cancellationToken = default)
    {
        string idempotencyKey =
            ActivePlayRecurringCostSettlementEngine.BuildPersistedIdempotencyKey(
                ownershipId,
                activityReferenceId);

        EconomyLedgerTransaction? existing =
            await _ledgerStore
                .FindByIdempotencyKeyAsync(
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);

        if (existing is not null)
        {
            decimal existingBalance =
                await _ledgerStore
                    .ReadCashBalanceAsync(cancellationToken)
                    .ConfigureAwait(false);
            ActivePlayBillingState existingState =
                await _ledgerStore
                    .ReadActivePlayBillingStateAsync(
                        ownershipId,
                        cancellationToken)
                    .ConfigureAwait(false);

            return new PersistedActivePlayRecurringCostSettlementResult(
                Settlement: null,
                Transaction: existing,
                PostResult: LedgerPostResult.AlreadyPosted,
                CashBalanceAfter: existingBalance,
                CurrentBillingState: existingState);
        }

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
                settledAt);

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
            Settlement: settlement,
            Transaction: settlement.Transaction,
            PostResult: postResult,
            CashBalanceAfter: balance,
            CurrentBillingState: currentState);
    }
}
