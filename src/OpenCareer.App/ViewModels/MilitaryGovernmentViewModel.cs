using System.ComponentModel;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;

namespace OpenCareer.App.ViewModels;

public sealed class MilitaryGovernmentViewModel : INotifyPropertyChanged
{
    private readonly ConflictCampaignRuntimeState _runtime;
    private readonly MilitaryCampaignTransitionService _transitions;
    private readonly ILogger<MilitaryGovernmentViewModel> _logger;

    private ConflictOperationsSnapshot? _snapshot;
    private MilitarySuccessorOperationOffer? _successorOffer;
    private MilitarySuccessorOperationPresentation? _successorPresentation;
    private IReadOnlyList<MilitarySupportRequestItemViewModel> _supportRequests =
        Array.Empty<MilitarySupportRequestItemViewModel>();
    private IReadOnlyList<MilitaryStrategicObjectiveItemViewModel> _objectives =
        Array.Empty<MilitaryStrategicObjectiveItemViewModel>();
    private string _statusMessage =
        "No military campaign is active. Military/Government operations will appear here when a campaign is available.";
    private bool _successorDeclined;
    private bool _isBusy;
    private string? _actionMessage;
    private (string? CampaignId, long? Revision) _messageSource;

    public MilitaryGovernmentViewModel(
        ConflictCampaignRuntimeState runtime,
        MilitaryCampaignTransitionService transitions,
        ILogger<MilitaryGovernmentViewModel>? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        _logger = logger ?? NullLogger<MilitaryGovernmentViewModel>.Instance;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasCampaign => _snapshot is not null;
    public bool HasSuccessorOffer => _successorPresentation is not null;
    public bool HasDeclinedSuccessorOffer => _successorDeclined;
    public bool IsBusy => _isBusy;
    public bool CanAcceptSuccessor => HasSuccessorOffer && !_isBusy;
    public bool CanDeclineSuccessor => HasSuccessorOffer && !_isBusy;
    public bool CanReconsiderSuccessor => _successorDeclined && !_isBusy;

    public string StatusMessage => _statusMessage;

    public string OperationName =>
        _snapshot?.OperationName ?? "No active operation";

    public string TheaterText =>
        _snapshot is null
            ? "Theater —"
            : $"Theater {_snapshot.TheaterId}";

    public string CampaignStateText =>
        _snapshot is null
            ? "NO CAMPAIGN"
            : $"{FormatWords(_snapshot.Phase.ToString())} • {FormatWords(_snapshot.Outcome.ToString())}";

    public string FriendlyFactionText =>
        _snapshot is null
            ? "Friendly faction —"
            : $"{_snapshot.FriendlyFaction.ShortCode} • {_snapshot.FriendlyFaction.DisplayName}";

    public string FriendlyPostureText =>
        _snapshot is null
            ? "Posture —"
            : $"{FormatWords(_snapshot.FriendlyFaction.Posture.ToString())} posture";

    public string HostileFactionText =>
        _snapshot is null
            ? "Hostile faction —"
            : $"{_snapshot.HostileFaction.ShortCode} • {_snapshot.HostileFaction.DisplayName}";

    public string HostilePostureText =>
        _snapshot is null
            ? "Posture —"
            : $"{FormatWords(_snapshot.HostileFaction.Posture.ToString())} posture";

    public string ControlText =>
        _snapshot is null
            ? "Control —"
            : $"{_snapshot.FriendlyControlAverage:P0} friendly control";

    public string MomentumText =>
        _snapshot is null
            ? "Momentum —"
            : $"{_snapshot.FriendlyMomentum:+0.00;-0.00;0.00} momentum";

    public string FriendlyReserveText =>
        _snapshot is null
            ? "Reserve —"
            : $"{_snapshot.FriendlyReplacementReserve:P0} replacement reserve";

    public string HostileReserveText =>
        _snapshot is null
            ? "Reserve —"
            : $"{_snapshot.HostileReplacementReserve:P0} replacement reserve";

    public string MilitaryTrustText =>
        _snapshot is null
            ? "Trust —"
            : $"{_snapshot.MilitaryTrust:P0} military trust";

    public string DamageText =>
        _snapshot is null
            ? "Damage —"
            : $"Airframe {_snapshot.PlayerCombatState.AirframeDamage:P0} • " +
              $"Propulsion {_snapshot.PlayerCombatState.PropulsionDamage:P0} • " +
              $"Systems {_snapshot.PlayerCombatState.SystemsDamage:P0}";

    public string ActiveMissionText =>
        _snapshot?.ActiveOperation is { } mission
            ? $"{FormatWords(mission.Type.ToString())} • {FormatWords(mission.Stage)}"
            : "No military flight currently assigned";

    public string FrontText =>
        _snapshot is null
            ? "Front —"
            : $"{_snapshot.Front.Points.Length} front point(s) • " +
              $"{_snapshot.Front.ContestedSectorShare:P0} contested share • " +
              $"{_snapshot.Threats.Length} active threat envelope(s)";

    public string AsOfText =>
        _snapshot is null
            ? "No campaign snapshot loaded"
            : $"Campaign snapshot {_snapshot.AsOf.LocalDateTime:g}";

    public IReadOnlyList<MilitarySupportRequestItemViewModel> SupportRequests =>
        _supportRequests;

    public IReadOnlyList<MilitaryStrategicObjectiveItemViewModel> Objectives =>
        _objectives;

    public string SupportRequestStatusText =>
        _supportRequests.Count == 0
            ? "No active support requests."
            : $"{_supportRequests.Count} active support request(s).";

    public string ObjectiveStatusText =>
        _objectives.Count == 0
            ? _snapshot?.Outcome == ConflictCampaignOutcome.Ongoing
                ? "No strategic objectives are active."
                : "Campaign objectives are closed."
            : $"{_objectives.Count} strategic objective(s) active.";

    public string SuccessorOperationName =>
        _successorPresentation?.OperationName ?? "No successor operation offered";

    public string SuccessorTheaterText =>
        _successorPresentation is null
            ? "Theater —"
            : $"Theater {_successorPresentation.TheaterId}";

    public string SuccessorFriendlyText =>
        _successorPresentation is null
            ? "Friendly faction —"
            : $"{_successorPresentation.FriendlyFactionCode} • " +
              $"{_successorPresentation.FriendlyFactionName} • " +
              $"{FormatWords(_successorPresentation.FriendlyPosture.ToString())}";

    public string SuccessorHostileText =>
        _successorPresentation is null
            ? "Hostile faction —"
            : $"{_successorPresentation.HostileFactionCode} • " +
              $"{_successorPresentation.HostileFactionName} • " +
              $"{FormatWords(_successorPresentation.HostilePosture.ToString())}";

    public string SuccessorAvailableText =>
        _successorPresentation is null
            ? "No successor operation is awaiting a decision."
            : $"Available {_successorPresentation.AvailableAt.LocalDateTime:g}";

    public void Refresh()
    {
        // Keep the displayed offer stable while its acceptance is being saved.
        if (_isBusy)
            return;

        ConflictCampaignStoreRecord? current = _runtime.Current;
        var source = (current?.Checkpoint.CampaignId, current?.Revision);
        if (_messageSource != source)
        {
            _actionMessage = null;
            _successorDeclined = false;
            _messageSource = source;
        }

        _snapshot = current is null
            ? null
            : ConflictOperationsSnapshotBuilder.Build(current.Checkpoint);

        _supportRequests = _snapshot?.SupportRequests
            .Select(static request =>
                new MilitarySupportRequestItemViewModel(request))
            .ToArray()
            ?? Array.Empty<MilitarySupportRequestItemViewModel>();

        _objectives = _snapshot?.StrategicObjectives
            .Select(static objective =>
                new MilitaryStrategicObjectiveItemViewModel(objective))
            .ToArray()
            ?? Array.Empty<MilitaryStrategicObjectiveItemViewModel>();

        _successorOffer = _transitions.GetCurrentOffer();
        _successorPresentation = _successorOffer is null
            ? null
            : MilitarySuccessorOperationPresentationBuilder.Build(
                _successorOffer);

        if (_successorPresentation is not null)
            _successorDeclined = false;

        _statusMessage = _actionMessage ?? (_snapshot switch
        {
            null =>
                "No military campaign is active. Military/Government operations will appear here when a campaign is available.",
            { Outcome: ConflictCampaignOutcome.Ongoing } =>
                "Campaign state is live. OpenCareer remains authoritative for objectives, threats and outcomes.",
            _ when _successorPresentation is not null =>
                "This campaign has ended. A successor operation is available for review.",
            _ when _successorDeclined =>
                "The successor operation is deferred. You can reconsider it without changing the completed campaign.",
            _ =>
                "This campaign has ended. No successor operation is currently available."
        });

        RaiseAll();
    }

    public async Task AcceptSuccessorAsync(
        CancellationToken cancellationToken = default)
    {
        if (_successorOffer is null || _isBusy)
            return;

        SetBusy(true);
        _actionMessage = null;

        try
        {
            await _transitions.AcceptAsync(
                _successorOffer,
                cancellationToken);

            SetActionMessage("Successor operation accepted and activated.");
            _successorDeclined = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetActionMessage("Operation acceptance cancelled. You can try again.");
        }
        catch (Exception ex) when (ex is DbException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save successor military operation.");
            SetActionMessage("The operation could not be saved. Check local storage and try again.");
        }
        catch (Exception ex) when (
            ex is ConflictCampaignConcurrencyException
                or InvalidOperationException
                or ArgumentException)
        {
            _logger.LogWarning(ex, "Successor military operation acceptance was rejected.");
            SetActionMessage("The operation could not be accepted. Review the current campaign and offer.");
        }
        finally
        {
            SetBusy(false);
            Refresh();
        }
    }

    public void DeclineSuccessor()
    {
        if (_successorOffer is null || _isBusy)
            return;

        if (_transitions.Decline(_successorOffer))
        {
            _successorOffer = null;
            _successorPresentation = null;
            _successorDeclined = true;
            SetActionMessage("Successor operation deferred. The completed campaign remains unchanged.");
        }
        else
        {
            SetActionMessage("The successor operation changed before it could be deferred.");
        }

        Refresh();
    }

    public void ReconsiderSuccessor()
    {
        if (_isBusy || !_transitions.Reconsider())
            return;

        _successorDeclined = false;
        _actionMessage = null;
        Refresh();
    }

    private void SetActionMessage(string message)
    {
        ConflictCampaignStoreRecord? current = _runtime.Current;
        _messageSource = (current?.Checkpoint.CampaignId, current?.Revision);
        _actionMessage = message;
    }

    private void SetBusy(bool value)
    {
        if (_isBusy == value)
            return;

        _isBusy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanAcceptSuccessor));
        OnPropertyChanged(nameof(CanDeclineSuccessor));
        OnPropertyChanged(nameof(CanReconsiderSuccessor));
    }

    private void RaiseAll()
    {
        string[] properties =
        [
            nameof(HasCampaign),
            nameof(HasSuccessorOffer),
            nameof(HasDeclinedSuccessorOffer),
            nameof(CanAcceptSuccessor),
            nameof(CanDeclineSuccessor),
            nameof(CanReconsiderSuccessor),
            nameof(StatusMessage),
            nameof(OperationName),
            nameof(TheaterText),
            nameof(CampaignStateText),
            nameof(FriendlyFactionText),
            nameof(FriendlyPostureText),
            nameof(HostileFactionText),
            nameof(HostilePostureText),
            nameof(ControlText),
            nameof(MomentumText),
            nameof(FriendlyReserveText),
            nameof(HostileReserveText),
            nameof(MilitaryTrustText),
            nameof(DamageText),
            nameof(ActiveMissionText),
            nameof(FrontText),
            nameof(AsOfText),
            nameof(SupportRequests),
            nameof(Objectives),
            nameof(SupportRequestStatusText),
            nameof(ObjectiveStatusText),
            nameof(SuccessorOperationName),
            nameof(SuccessorTheaterText),
            nameof(SuccessorFriendlyText),
            nameof(SuccessorHostileText),
            nameof(SuccessorAvailableText)
        ];

        foreach (string property in properties)
            OnPropertyChanged(property);
    }

    internal static string FormatWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var result = new System.Text.StringBuilder(value.Length + 8);

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];

            if (index > 0
                && char.IsUpper(current)
                && !char.IsUpper(value[index - 1]))
            {
                result.Append(' ');
            }

            result.Append(current);
        }

        return result.ToString();
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed class MilitarySupportRequestItemViewModel
{
    public MilitarySupportRequestItemViewModel(
        ConflictSupportProjection request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TypeText =
            MilitaryGovernmentViewModel.FormatWords(
                request.Type.ToString());
        UrgencyText = request.Urgency.ToString().ToUpperInvariant();
        StatusText =
            MilitaryGovernmentViewModel.FormatWords(
                request.Status.ToString());
        ExpiresText =
            $"Expires {request.ExpiresAt.LocalDateTime:t}";
    }

    public string TypeText { get; }
    public string UrgencyText { get; }
    public string StatusText { get; }
    public string ExpiresText { get; }
}

public sealed class MilitaryStrategicObjectiveItemViewModel
{
    public MilitaryStrategicObjectiveItemViewModel(
        ConflictStrategicObjective objective)
    {
        ArgumentNullException.ThrowIfNull(objective);

        Title =
            MilitaryGovernmentViewModel.FormatWords(
                objective.Kind.ToString());
        Scope =
            objective.SectorId
            ?? (objective.UnitId is Guid unitId
                ? unitId.ToString("N")[..8]
                : objective.ThreatId is Guid threatId
                    ? threatId.ToString("N")[..8]
                    : "Campaign");
        ProgressText = $"{objective.Progress:P0}";
        TargetText = $"Target {objective.TargetValue:P0}";
        IsComplete = objective.IsComplete;
    }

    public string Title { get; }
    public string Scope { get; }
    public string ProgressText { get; }
    public string TargetText { get; }
    public bool IsComplete { get; }
}
