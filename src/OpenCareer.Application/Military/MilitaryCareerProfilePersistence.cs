using OpenCareer.Domain.Military;

namespace OpenCareer.Application.Military;

public sealed record MilitaryCareerProfileStoreRecord(
    long Revision,
    MilitaryCareerState Career,
    DateTimeOffset SavedAt)
{
    public void Validate()
    {
        if (Revision < 1)
            throw new ArgumentOutOfRangeException(nameof(Revision));

        ArgumentNullException.ThrowIfNull(Career);
        Career.Validate();

        if (SavedAt == default)
            throw new ArgumentOutOfRangeException(nameof(SavedAt));
    }
}

public interface IMilitaryCareerProfileStore
{
    Task<MilitaryCareerProfileStoreRecord?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<MilitaryCareerProfileStoreRecord> SaveAsync(
        MilitaryCareerState career,
        long? expectedRevision,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default);
}

public sealed class MilitaryCareerProfileConcurrencyException
    : InvalidOperationException
{
    public MilitaryCareerProfileConcurrencyException(string message)
        : base(message)
    {
    }
}
