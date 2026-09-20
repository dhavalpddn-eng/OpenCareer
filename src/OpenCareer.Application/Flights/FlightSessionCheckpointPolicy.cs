using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed record FlightSessionCheckpointPolicy(
    TimeSpan MaximumSteadyStateInterval)
{
    public static FlightSessionCheckpointPolicy Default { get; } =
        new(TimeSpan.FromSeconds(30));

    public void Validate()
    {
        if (MaximumSteadyStateInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumSteadyStateInterval));
        }
    }

    public bool ShouldCheckpoint(
        FlightSession? lastPersisted,
        FlightSession next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Validate();

        if (lastPersisted is null)
            return true;

        if (lastPersisted.SessionId != next.SessionId)
            return true;

        if (next.UpdatedAt < lastPersisted.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(next),
                "A checkpoint candidate cannot be older than the last persisted session.");
        }

        if (next.IsTerminal
            || next.Status != lastPersisted.Status
            || next.OperationState
                != lastPersisted.OperationState
            || next.Tracking.State
                != lastPersisted.Tracking.State
            || next.Tracking.TakeoffCount
                != lastPersisted.Tracking.TakeoffCount
            || next.Tracking.LandingEpisodeCount
                != lastPersisted.Tracking.LandingEpisodeCount
            || next.Tracking.BounceCount
                != lastPersisted.Tracking.BounceCount
            || next.Tracking.TouchAndGoCount
                != lastPersisted.Tracking.TouchAndGoCount
            || next.Tracking.RejectedTakeoffCount
                != lastPersisted.Tracking.RejectedTakeoffCount
            || next.Tracking.CrashReported
                != lastPersisted.Tracking.CrashReported
            || next.Milestones
                != lastPersisted.Milestones)
        {
            return true;
        }

        return next.UpdatedAt
            - lastPersisted.UpdatedAt
            >= MaximumSteadyStateInterval;
    }
}
