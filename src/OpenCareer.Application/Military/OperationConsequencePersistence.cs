namespace OpenCareer.Application.Military;

using OpenCareer.Domain.Military;

public sealed record OperationConsequenceStoreRecord(
    OperationResolutionKey ResolutionKey,
    OperationConsequenceResult Result,
    DateTimeOffset SavedAt)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ResolutionKey.OperationId))
            throw new ArgumentException("Resolution operation ID is required.", nameof(ResolutionKey));

        if (ResolutionKey.MissionId == Guid.Empty)
            throw new ArgumentException("Resolution mission ID is required.", nameof(ResolutionKey));

        ArgumentNullException.ThrowIfNull(Result);
        Result.Validate();

        if (SavedAt == default)
            throw new ArgumentOutOfRangeException(nameof(SavedAt));

        OperationResolutionKey expectedKey =
            OperationResolutionKey.Create(
                Result.Outcome.OperationId,
                Result.Outcome.MissionId);

        if (ResolutionKey != expectedKey)
        {
            throw new ArgumentException(
                "Stored resolution key does not match the operation outcome.",
                nameof(ResolutionKey));
        }

        if (SavedAt < Result.Outcome.CompletedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SavedAt),
                "Persistence time cannot precede operation completion.");
        }
    }
}

public interface IOperationConsequenceStore
{
    Task<OperationConsequenceStoreRecord?> LoadAsync(
        OperationResolutionKey key,
        CancellationToken cancellationToken = default);

    Task<OperationConsequenceStoreRecord> SaveAsync(
        OperationConsequenceResult result,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default);
}

public sealed class OperationConsequenceAlreadyExistsException
    : InvalidOperationException
{
    public OperationConsequenceAlreadyExistsException(
        OperationResolutionKey resolutionKey)
        : base($"Operation consequence '{resolutionKey}' is already persisted.")
    {
        ResolutionKey = resolutionKey;
    }

    public OperationResolutionKey ResolutionKey { get; }
}
