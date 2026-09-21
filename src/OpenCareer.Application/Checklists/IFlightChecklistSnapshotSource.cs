using OpenCareer.Domain.Checklists;

namespace OpenCareer.Application.Checklists;

public interface IFlightChecklistSnapshotSource
{
    bool IsActive { get; }

    string? ActiveProfileId { get; }

    FlightChecklistSnapshot? Current { get; }

    event EventHandler<FlightChecklistSnapshotChangedEventArgs>? SnapshotChanged;
}

public sealed class FlightChecklistSnapshotChangedEventArgs(
    string? activeProfileId,
    FlightChecklistSnapshot? snapshot) : EventArgs
{
    public string? ActiveProfileId { get; } = activeProfileId;

    public FlightChecklistSnapshot? Snapshot { get; } = snapshot;

    public bool IsActive =>
        Snapshot is not null;
}
