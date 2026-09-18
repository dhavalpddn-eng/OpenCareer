using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public enum LedgerPostResult
{
    Posted,
    AlreadyPosted
}

public interface IEconomyLedgerStore
{
    Task<LedgerPostResult> PostAsync(
        EconomyLedgerTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<decimal> ReadCashBalanceAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EconomyLedgerTransaction>> ReadRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
