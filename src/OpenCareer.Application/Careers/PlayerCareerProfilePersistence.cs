using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed record PlayerCareerProfileStoreRecord(
    long Revision,
    PlayerCareerProfile Profile,
    DateTimeOffset SavedAt)
{
    public void Validate()
    {
        if (Revision < 1)
            throw new ArgumentOutOfRangeException(nameof(Revision));

        ArgumentNullException.ThrowIfNull(Profile);
        Profile.Validate();

        if (SavedAt == default || SavedAt < Profile.CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(SavedAt));
    }
}

public interface IPlayerCareerProfileStore
{
    Task<PlayerCareerProfileStoreRecord?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<PlayerCareerProfileStoreRecord> SaveAsync(
        PlayerCareerProfile profile,
        long? expectedRevision,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default);
}

public sealed class PlayerCareerProfileConcurrencyException
    : InvalidOperationException
{
    public PlayerCareerProfileConcurrencyException(string message)
        : base(message)
    {
    }
}
