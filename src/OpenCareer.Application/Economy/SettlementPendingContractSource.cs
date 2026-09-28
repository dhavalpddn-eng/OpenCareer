using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public sealed record SettlementPendingContract(
    PersistedJobContract PersistedContract,
    string SettlementIdempotencyKey);

public sealed class SettlementPendingContractSource
{
    private readonly IJobContractRuntimeSource _runtimeSource;
    private readonly IEconomyLedgerStore _ledgerStore;

    public SettlementPendingContractSource(
        IJobContractRuntimeSource runtimeSource,
        IEconomyLedgerStore ledgerStore)
    {
        _runtimeSource =
            runtimeSource
            ?? throw new ArgumentNullException(nameof(runtimeSource));

        _ledgerStore =
            ledgerStore
            ?? throw new ArgumentNullException(nameof(ledgerStore));
    }

    public async Task<IReadOnlyList<SettlementPendingContract>>
        ReadPendingAsync(
            CancellationToken cancellationToken = default)
    {
        if (!_runtimeSource.IsInitialized)
        {
            throw new InvalidOperationException(
                "Job-contract runtime state must be initialized before settlement-pending contracts can be read.");
        }

        PersistedJobContract[] completed =
            _runtimeSource.Current
                .Where(
                    item =>
                        item.Contract.Status
                        == ContractStatus.Completed)
                .OrderBy(
                    item =>
                        item.Contract.CompletedAt)
                .ThenBy(
                    item =>
                        item.Contract.ContractId)
                .ToArray();

        var pending =
            new List<SettlementPendingContract>(
                completed.Length);

        foreach (PersistedJobContract persisted in completed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            persisted.Validate();

            string idempotencyKey =
                ContractSettlementEngine
                    .GetIdempotencyKey(
                        persisted.Contract.ContractId);

            EconomyLedgerTransaction? existing =
                await _ledgerStore
                    .FindByIdempotencyKeyAsync(
                        idempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (existing is not null)
                continue;

            pending.Add(
                new SettlementPendingContract(
                    persisted,
                    idempotencyKey));
        }

        return pending;
    }
}
