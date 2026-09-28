using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class JobContractRecoveryService
{
    private readonly IJobContractRecoverySource _recoverySource;
    private readonly IJobContractStore _contractStore;

    public JobContractRecoveryService(
        IJobContractRecoverySource recoverySource,
        IJobContractStore contractStore)
    {
        _recoverySource = recoverySource
            ?? throw new ArgumentNullException(nameof(recoverySource));
        _contractStore = contractStore
            ?? throw new ArgumentNullException(nameof(contractStore));
    }

    public async Task<IReadOnlyList<PersistedJobContract>> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<JobContractRecoveryCandidate> candidates =
            await _recoverySource
                .ReadRecoveryCandidatesAsync(cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Job-contract recovery source returned no candidate collection.");

        if (candidates.Count == 0)
            return Array.Empty<PersistedJobContract>();

        var seen = new HashSet<Guid>();
        var recovered =
            new List<PersistedJobContract>(candidates.Count);

        foreach (JobContractRecoveryCandidate candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            candidate.Validate();

            if (!seen.Add(candidate.ContractId))
            {
                throw new InvalidOperationException(
                    "Job-contract recovery source returned a duplicate contract identity.");
            }

            PersistedJobContract persisted =
                await _contractStore
                    .ReadJobContractAsync(
                        candidate.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Recoverable job contract could not be loaded from authoritative persistence.");

            persisted.Validate();

            if (persisted.Contract.ContractId != candidate.ContractId
                || persisted.Contract.Status != candidate.Status
                || persisted.Version != candidate.Version
                || ContractUpdatedAt(persisted.Contract).ToUnixTimeMilliseconds()
                    != candidate.UpdatedAt.ToUnixTimeMilliseconds())
            {
                throw new InvalidOperationException(
                    "Recoverable job-contract metadata does not match authoritative persistence.");
            }

            recovered.Add(persisted);
        }

        return recovered;
    }

    private static DateTimeOffset ContractUpdatedAt(
        JobContract contract) =>
        contract.CompletedAt
        ?? contract.StartedAt
        ?? contract.AcceptedAt
        ?? contract.OfferedAt;
}
