using OpenCareer.Domain.Events;

namespace OpenCareer.Application.Economy;

public sealed record WorldEventStateSnapshot(
    string SnapshotId,
    DateTimeOffset UpdatedAt,
    WorldEventLocation Location,
    WorldEventInstance[] Events)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SnapshotId);
        ArgumentNullException.ThrowIfNull(Location);
        ArgumentNullException.ThrowIfNull(Events);

        Location.Validate();

        if (Events.Select(static worldEvent => worldEvent.InstanceId)
            .Distinct(StringComparer.Ordinal)
            .Count() != Events.Length)
        {
            throw new ArgumentException("World-event snapshot contains duplicate event instances.");
        }

        foreach (WorldEventInstance worldEvent in Events)
        {
            WorldEventEngine.ValidateInstance(worldEvent);

            if (!worldEvent.IsActiveAt(UpdatedAt))
            {
                throw new ArgumentException(
                    $"World-event instance '{worldEvent.InstanceId}' is not active at the snapshot time.");
            }

            if (!Location.Matches(worldEvent))
            {
                throw new ArgumentException(
                    $"World-event instance '{worldEvent.InstanceId}' does not match the snapshot location.");
            }
        }
    }
}

public interface IWorldEventStateStore
{
    Task SaveAsync(
        WorldEventStateSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<WorldEventStateSnapshot?> GetAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldEventStateSnapshot>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}
