using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum JobContractSaveResult
{
    Created,
    Updated,
    AlreadySaved
}

public sealed record PersistedJobContract(
    JobContract Contract,
    long Version)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Contract);
        Contract.Validate();

        if (Version < 0)
            throw new ArgumentOutOfRangeException(nameof(Version));
    }
}

public interface IJobContractStore
{
    Task<PersistedJobContract?> ReadJobContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);

    Task<JobContractSaveResult> CreateJobContractAsync(
        JobContract contract,
        CancellationToken cancellationToken = default);

    Task<JobContractSaveResult> UpdateJobContractAsync(
        JobContract contract,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
