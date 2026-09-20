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
    private IReadOnlyList<MilitaryCompletedOperationItemViewModel> _completedOperations =
        Array.Empty<MilitaryCompletedOperationItemViewModel>();
    private IReadOnlyList<MilitaryOperationalMapMarkerViewModel> _operationalMapMarkers =
        Array.Empty<MilitaryOperationalMapMarkerViewModel>();
    private IReadOnlyList<MilitaryCommunicationItemViewModel> _communications =
        Array.Empty<MilitaryCommunicationItemViewModel>();
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

    public IReadOnlyList<MilitaryCompletedOperationItemViewModel> CompletedOperations =>
        _completedOperations;

    public IReadOnlyList<MilitaryOperationalMapMarkerViewModel> OperationalMapMarkers =>
        _operationalMapMarkers;

    public string OperationalMapStatusText =>
        _snapshot is null
            ? "No tactical picture is available."
            : $"{_snapshot.Units.Length} unit(s) • {_snapshot.Threats.Length} threat(s) • " +
              $"{_snapshot.SupportRequests.Length} support target(s)";

    public string OperationalMapBoundsText =>
        MilitaryOperationalMapMarkerViewModel.FormatBounds(_operationalMapMarkers);

    public IReadOnlyList<MilitaryCommunicationItemViewModel> Communications =>
        _communications;

    public string CommunicationsStatusText =>
        _communications.Count == 0
            ? "No operational communications are available."
            : $"{_communications.Count} deterministic operational message(s), current state only.";

    public string CompletedOperationStatusText =>
        _completedOperations.Count == 0
            ? "No archived operations. Completed campaigns appear here after a successor operation is accepted."
            : $"{_completedOperations.Count} archived operation(s), newest first.";

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

        _completedOperations = _snapshot?.CompletedCampaigns
            .OrderByDescending(static entry => entry.EndedAt)
            .ThenBy(static entry => entry.CampaignId, StringComparer.Ordinal)
            .Select(static entry => new MilitaryCompletedOperationItemViewModel(entry))
            .ToArray()
            ?? Array.Empty<MilitaryCompletedOperationItemViewModel>();

        _operationalMapMarkers = _snapshot is null
            ? Array.Empty<MilitaryOperationalMapMarkerViewModel>()
            : MilitaryOperationalMapMarkerViewModel.Build(_snapshot);

        _communications = _snapshot is null
            ? Array.Empty<MilitaryCommunicationItemViewModel>()
            : ConflictCommunicationsBuilder.Build(_snapshot)
                .Select(static entry =>
                    new MilitaryCommunicationItemViewModel(entry))
                .ToArray();

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
            nameof(CompletedOperations),
            nameof(OperationalMapMarkers),
            nameof(OperationalMapStatusText),
            nameof(OperationalMapBoundsText),
            nameof(Communications),
            nameof(CommunicationsStatusText),
            nameof(CompletedOperationStatusText),
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

public sealed class MilitaryCommunicationItemViewModel
{
    public MilitaryCommunicationItemViewModel(
        ConflictCommunicationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();

        TimestampText = entry.Timestamp.LocalDateTime.ToString("t");
        ChannelText = entry.Channel.ToString().ToUpperInvariant();
        PriorityText =
            MilitaryGovernmentViewModel.FormatWords(
                entry.Priority.ToString())
            .ToUpperInvariant();
        Message = entry.Message;
    }

    public string TimestampText { get; }
    public string ChannelText { get; }
    public string PriorityText { get; }
    public string Message { get; }
}

public enum MilitaryOperationalMapMarkerKind
{
    FriendlyUnit,
    HostileUnit,
    NeutralUnit,
    Threat,
    SupportRequest
}

public sealed class MilitaryOperationalMapMarkerViewModel
{
    private const double EdgePadding = 0.06;

    private MilitaryOperationalMapMarkerViewModel(
        MilitaryOperationalMapMarkerKind kind,
        string symbol,
        string label,
        string detail,
        GeoPoint position,
        double normalizedX,
        double normalizedY)
    {
        Kind = kind;
        Symbol = symbol;
        Label = label;
        Detail = detail;
        Position = position;
        NormalizedX = normalizedX;
        NormalizedY = normalizedY;
    }

    public MilitaryOperationalMapMarkerKind Kind { get; }
    public string Symbol { get; }
    public string Label { get; }
    public string Detail { get; }
    public GeoPoint Position { get; }
    public double NormalizedX { get; }
    public double NormalizedY { get; }

    internal static IReadOnlyList<MilitaryOperationalMapMarkerViewModel> Build(
        ConflictOperationsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var raw = new List<RawMarker>(
            snapshot.Units.Length
            + snapshot.Threats.Length
            + snapshot.SupportRequests.Length);

        raw.AddRange(snapshot.Units.Select(static unit =>
            new RawMarker(
                UnitKind(unit.Side),
                unit.Side switch
                {
                    ConflictSide.Friendly => "F",
                    ConflictSide.Hostile => "H",
                    _ => "N"
                },
                $"{MilitaryGovernmentViewModel.FormatWords(unit.Role)} unit",
                $"{unit.Side} • strength {unit.Strength:P0} • readiness {unit.Readiness:P0}" +
                (unit.Airborne ? " • airborne" : string.Empty),
                unit.Position)));

        raw.AddRange(snapshot.Threats.Select(static threat =>
            new RawMarker(
                MilitaryOperationalMapMarkerKind.Threat,
                "!",
                MilitaryGovernmentViewModel.FormatWords(threat.Type.ToString()),
                $"{threat.RadiusNauticalMiles:0} NM radius • severity {threat.Severity:P0}",
                threat.Center)));

        raw.AddRange(snapshot.SupportRequests.Select(static request =>
            new RawMarker(
                MilitaryOperationalMapMarkerKind.SupportRequest,
                "S",
                MilitaryGovernmentViewModel.FormatWords(request.Type.ToString()),
                $"{request.Urgency} • {MilitaryGovernmentViewModel.FormatWords(request.Status.ToString())}",
                request.TargetPosition)));

        if (raw.Count == 0)
            return Array.Empty<MilitaryOperationalMapMarkerViewModel>();

        double minLatitude = raw.Min(static item => item.Position.LatitudeDegrees);
        double maxLatitude = raw.Max(static item => item.Position.LatitudeDegrees);
        double minLongitude = raw.Min(static item => item.Position.LongitudeDegrees);
        double maxLongitude = raw.Max(static item => item.Position.LongitudeDegrees);

        double latitudeSpan = Math.Max(maxLatitude - minLatitude, 0.01);
        double longitudeSpan = Math.Max(maxLongitude - minLongitude, 0.01);
        double usable = 1 - (EdgePadding * 2);

        return raw
            .Select(item =>
            {
                double x = EdgePadding
                    + ((item.Position.LongitudeDegrees - minLongitude)
                        / longitudeSpan * usable);
                double y = EdgePadding
                    + ((maxLatitude - item.Position.LatitudeDegrees)
                        / latitudeSpan * usable);

                return new MilitaryOperationalMapMarkerViewModel(
                    item.Kind,
                    item.Symbol,
                    item.Label,
                    item.Detail,
                    item.Position,
                    Math.Clamp(x, EdgePadding, 1 - EdgePadding),
                    Math.Clamp(y, EdgePadding, 1 - EdgePadding));
            })
            .OrderBy(static item => item.Kind)
            .ThenBy(static item => item.Label, StringComparer.Ordinal)
            .ThenBy(static item => item.Position.LatitudeDegrees)
            .ThenBy(static item => item.Position.LongitudeDegrees)
            .ToArray();
    }

    internal static string FormatBounds(
        IReadOnlyList<MilitaryOperationalMapMarkerViewModel> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        if (markers.Count == 0)
            return "Schematic map • no plotted markers";

        double minLatitude = markers.Min(static item => item.Position.LatitudeDegrees);
        double maxLatitude = markers.Max(static item => item.Position.LatitudeDegrees);
        double minLongitude = markers.Min(static item => item.Position.LongitudeDegrees);
        double maxLongitude = markers.Max(static item => item.Position.LongitudeDegrees);

        return $"Schematic map • {minLatitude:0.00}–{maxLatitude:0.00}° lat • " +
               $"{minLongitude:0.00}–{maxLongitude:0.00}° lon";
    }

    private static MilitaryOperationalMapMarkerKind UnitKind(
        ConflictSide side) =>
        side switch
        {
            ConflictSide.Friendly => MilitaryOperationalMapMarkerKind.FriendlyUnit,
            ConflictSide.Hostile => MilitaryOperationalMapMarkerKind.HostileUnit,
            _ => MilitaryOperationalMapMarkerKind.NeutralUnit
        };

    private sealed record RawMarker(
        MilitaryOperationalMapMarkerKind Kind,
        string Symbol,
        string Label,
        string Detail,
        GeoPoint Position);
}

public sealed class MilitaryCompletedOperationItemViewModel
{
    public MilitaryCompletedOperationItemViewModel(ConflictCampaignHistoryEntry entry)
        : this(CompletedMilitaryOperationDetailProjectionBuilder.Build(entry))
    {
    }

    public MilitaryCompletedOperationItemViewModel(
        CompletedMilitaryOperationDetailProjection detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        CampaignId = detail.CampaignId;
        CampaignText = $"Campaign {detail.CampaignId}";
        OperationName = detail.OperationName;
        TheaterText = $"Theater {detail.TheaterId}";
        OutcomeText =
            MilitaryGovernmentViewModel.FormatWords(
                detail.Outcome.ToString());
        FinalStateText =
            $"Final phase: {MilitaryGovernmentViewModel.FormatWords(detail.FinalPhase.ToString())} • " +
            $"{detail.FinalFriendlyControlAverage:P0} friendly control";
        EndedText = $"Ended {detail.EndedAt.LocalDateTime:g}";
        FriendlyFactionText = FormatFaction(
            detail.FriendlyFactionCode,
            detail.FriendlyFactionName,
            detail.FriendlyPosture);
        HostileFactionText = FormatFaction(
            detail.HostileFactionCode,
            detail.HostileFactionName,
            detail.HostilePosture);
    }

    public string CampaignId { get; }
    public string CampaignText { get; }
    public string OperationName { get; }
    public string TheaterText { get; }
    public string OutcomeText { get; }
    public string FinalStateText { get; }
    public string EndedText { get; }
    public string FriendlyFactionText { get; }
    public string HostileFactionText { get; }

    private static string FormatFaction(
        string shortCode,
        string displayName,
        ConflictFactionOperationalPosture posture) =>
        $"{shortCode} • {displayName} • " +
        $"{MilitaryGovernmentViewModel.FormatWords(posture.ToString())} posture";
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
