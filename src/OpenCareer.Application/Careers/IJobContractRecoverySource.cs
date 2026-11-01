using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed record JobContractRecoveryCandidate(
    Guid ContractId,
    ContractStatus Status,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(ContractId));
        }

        if (Status is not
            (ContractStatus.Accepted
            or ContractStatus.InProgress
            or ContractStatus.Completed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Status),
                "Only accepted, in-progress, or completed contracts require recovery reconciliation.");
        }

        if (Version < 1)
            throw new ArgumentOutOfRangeException(nameof(Version));

        if (UpdatedAt == default)
            throw new ArgumentOutOfRangeException(nameof(UpdatedAt));
    }
}

public interface IJobContractRecoverySource
{
    Task<IReadOnlyList<JobContractRecoveryCandidate>>
        ReadRecoveryCandidatesAsync(
            CancellationToken cancellationToken = default);
}
