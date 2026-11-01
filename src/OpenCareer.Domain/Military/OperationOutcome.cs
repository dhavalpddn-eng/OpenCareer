namespace OpenCareer.Domain.Military;

public enum OperationOutcomeStatus
{
    Success,
    PartialSuccess,
    Failure,
    Aborted
}

public sealed record OperationOutcome(
    Guid MissionId,
    string OperationId,
    OperationOutcomeStatus Status,
    DateTimeOffset CompletedAt)
{
    public void Validate()
    {
        if (MissionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(MissionId));

        ArgumentException.ThrowIfNullOrWhiteSpace(OperationId);

        if (!Enum.IsDefined(typeof(OperationOutcomeStatus), Status))
            throw new ArgumentOutOfRangeException(nameof(Status));

        if (CompletedAt == default)
            throw new ArgumentOutOfRangeException(nameof(CompletedAt));
    }
}
