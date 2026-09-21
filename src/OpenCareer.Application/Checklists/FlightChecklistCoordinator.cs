using OpenCareer.Domain.Checklists;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Checklists;

public sealed class FlightChecklistCoordinator : IFlightChecklistSnapshotSource
{
    private readonly FlightChecklistProfileSelector _profileSelector;

    private FlightChecklistProgression? _progression;
    private string? _profileId;

    public FlightChecklistCoordinator(
        FlightChecklistProfileSelector? profileSelector = null)
    {
        _profileSelector =
            profileSelector
            ?? new FlightChecklistProfileSelector();
    }

    public bool IsActive =>
        _progression is not null;

    public string? ActiveProfileId =>
        _profileId;

    public FlightChecklistSnapshot? Current =>
        _progression?.Current;

    public event EventHandler<FlightChecklistSnapshotChangedEventArgs>? SnapshotChanged;

    public FlightChecklistSnapshot Start(
        FlightChecklistSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsActive)
        {
            throw new InvalidOperationException(
                "A flight checklist is already active.");
        }

        FlightChecklistProfile profile =
            _profileSelector.Select(context);

        _progression =
            new FlightChecklistProgression(profile);

        _profileId = profile.Id;

        FlightChecklistSnapshot started =
            _progression.Current;

        Publish(started);
        return started;
    }

    public FlightChecklistSnapshot Process(
        FlightStateEvidence evidence)
    {
        FlightChecklistProgression progression =
            RequireProgression();

        FlightChecklistSnapshot previous =
            progression.Current;

        FlightChecklistSnapshot current =
            progression.Process(evidence);

        PublishIfChanged(previous, current);
        return current;
    }

    public FlightChecklistSnapshot ConfirmManual(
        FlightChecklistStepId stepId,
        DateTimeOffset timestamp)
    {
        FlightChecklistProgression progression =
            RequireProgression();

        FlightChecklistSnapshot previous =
            progression.Current;

        FlightChecklistSnapshot current =
            progression.ConfirmManual(
                stepId,
                timestamp);

        PublishIfChanged(previous, current);
        return current;
    }

    public FlightChecklistSnapshot End()
    {
        FlightChecklistProgression progression =
            RequireProgression();

        FlightChecklistSnapshot final =
            progression.Current;

        _progression = null;
        _profileId = null;

        Publish(snapshot: null);
        return final;
    }

    private void PublishIfChanged(
        FlightChecklistSnapshot previous,
        FlightChecklistSnapshot current)
    {
        if (previous.CurrentPhase == current.CurrentPhase
            && previous.Steps.SequenceEqual(current.Steps))
        {
            return;
        }

        Publish(current);
    }

    private void Publish(
        FlightChecklistSnapshot? snapshot) =>
        SnapshotChanged?.Invoke(
            this,
            new FlightChecklistSnapshotChangedEventArgs(
                _profileId,
                snapshot));

    private FlightChecklistProgression RequireProgression() =>
        _progression
        ?? throw new InvalidOperationException(
            "No flight checklist is active.");
}
