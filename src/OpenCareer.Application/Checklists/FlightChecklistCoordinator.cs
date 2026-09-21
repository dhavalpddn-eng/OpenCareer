using OpenCareer.Domain.Checklists;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Checklists;

public sealed class FlightChecklistCoordinator
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

        return _progression.Current;
    }

    public FlightChecklistSnapshot Process(
        FlightStateEvidence evidence) =>
        RequireProgression()
            .Process(evidence);

    public FlightChecklistSnapshot ConfirmManual(
        FlightChecklistStepId stepId,
        DateTimeOffset timestamp) =>
        RequireProgression()
            .ConfirmManual(
                stepId,
                timestamp);

    public FlightChecklistSnapshot End()
    {
        FlightChecklistProgression progression =
            RequireProgression();

        FlightChecklistSnapshot final =
            progression.Current;

        _progression = null;
        _profileId = null;

        return final;
    }

    private FlightChecklistProgression RequireProgression() =>
        _progression
        ?? throw new InvalidOperationException(
            "No flight checklist is active.");
}
