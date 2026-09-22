using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public sealed record SettlementPendingContractRequest(
    SettlementPendingContract PendingContract,
    ContractSettlementCosts ActualCosts,
    DateTimeOffset SettledAt);

public sealed class SettlementPendingContractCoordinator
{
    private readonly EconomySettlementService _settlementService;

    public SettlementPendingContractCoordinator(
        EconomySettlementService settlementService)
    {
        _settlementService =
            settlementService
            ?? throw new ArgumentNullException(nameof(settlementService));
    }

    public async Task<EconomySettlementResult> SettleAsync(
        SettlementPendingContractRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(
            request.PendingContract);
        ArgumentNullException.ThrowIfNull(
            request.ActualCosts);

        PersistedJobContract persisted =
            request.PendingContract.PersistedContract
            ?? throw new InvalidOperationException(
                "Settlement-pending input is missing its persisted contract.");

        persisted.Validate();
        request.ActualCosts.Validate();

        JobContract contract =
            persisted.Contract;

        if (contract.Status
            != ContractStatus.Completed
            || contract.CompletedAt is null)
        {
            throw new InvalidOperationException(
                "Only a completed settlement-pending contract can be settled.");
        }

        string expectedIdempotencyKey =
            ContractSettlementEngine.GetIdempotencyKey(
                contract.ContractId);

        if (!string.Equals(
                request.PendingContract.SettlementIdempotencyKey,
                expectedIdempotencyKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Settlement-pending idempotency key does not match the contract.");
        }

        if (request.SettledAt
            < contract.CompletedAt.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Settlement cannot precede verified contract completion.");
        }

        EconomySettlementResult result =
            await _settlementService
                .SettleCompletedContractAsync(
                    contract.ContractId,
                    request.ActualCosts,
                    request.SettledAt,
                    cancellationToken)
                .ConfigureAwait(false);

        if (result.PersistedContract.Contract.ContractId
                != contract.ContractId
            || !string.Equals(
                result.Settlement.Transaction.IdempotencyKey,
                expectedIdempotencyKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Economy settlement authority returned a different contract settlement.");
        }

        return result;
    }
}
