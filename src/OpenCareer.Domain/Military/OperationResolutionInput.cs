namespace OpenCareer.Domain.Military;

public enum MissionExecutionResult
{
    Completed,
    Failed,
    Aborted
}

public sealed record OperationResolutionInput(
    Guid MissionId,
    string OperationId,
    MissionExecutionResult MissionResult,
    int ObjectivesCompleted,
    int ObjectivesRequired,
    bool AircraftSurvived,
    bool CrewSurvived,
    TimeSpan MissionDuration,
    DateTimeOffset ResolvedAt)
{
    public double ObjectiveCompletionRatio =>
        ObjectivesRequired == 0
            ? 0
            : (double)ObjectivesCompleted / ObjectivesRequired;

    public void Validate()
    {
        if (MissionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(MissionId));

        ArgumentException.ThrowIfNullOrWhiteSpace(OperationId);

        if (!Enum.IsDefined(typeof(MissionExecutionResult), MissionResult))
            throw new ArgumentOutOfRangeException(nameof(MissionResult));

        if (ObjectivesRequired <= 0)
            throw new ArgumentOutOfRangeException(nameof(ObjectivesRequired));

        if (ObjectivesCompleted < 0 || ObjectivesCompleted > ObjectivesRequired)
            throw new ArgumentOutOfRangeException(nameof(ObjectivesCompleted));

        if (MissionDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MissionDuration));

        if (ResolvedAt == default)
            throw new ArgumentOutOfRangeException(nameof(ResolvedAt));
    }
}
