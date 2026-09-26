using System.Collections.Immutable;
using System.Text.Json.Serialization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum AirframeMaintenanceEventKind { DiscreteDamageRepair = 1, RoutineInspection = 2 }
public enum AirframeRepairStatus { Repaired, NoRepairRequired, NotFound }

/// <summary>One explicit action; expected revision identifies the condition being repaired.</summary>
public sealed record AirframeRepairRequest(
    [property: JsonRequired] Guid MaintenanceActionId,
    [property: JsonRequired] AirframeId AirframeId,
    [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] DateTimeOffset PerformedAt)
{
    public void Validate()
    {
        if (MaintenanceActionId == Guid.Empty) throw new ArgumentException("Maintenance action ID is required.");
        AirframeId.Validate();
        if (ExpectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(ExpectedRevision));
        if (PerformedAt == default) throw new ArgumentOutOfRangeException(nameof(PerformedAt));
    }

    public void ValidateBefore(AirframeStoreRecord before)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(before);
        before.Validate();
        if (before.Airframe.AirframeId != AirframeId)
            throw new InvalidDataException("Repair targets a different physical airframe.");
        if (before.Revision != ExpectedRevision)
            throw new AirframeConcurrencyException("Airframe condition changed before repair; refresh the expected revision.");
        if (PerformedAt <= before.SavedAt)
            throw new ArgumentOutOfRangeException(nameof(PerformedAt), "Repair time must advance the last saved condition timestamp.");
    }
}

/// <summary>Immutable factual discrete-damage repair, not an inspection or component service.</summary>
public sealed record AirframeMaintenanceEvent(
    [property: JsonRequired] AirframeRepairRequest Request,
    AirframeMaintenanceEventKind Kind,
    AirframeStoreRecord Before,
    AirframeStoreRecord After,
    string Rationale) : AirframeServiceEvent(Kind, Before, After, Rationale)
{
    public const string DiscreteRepairRationale = "Cleared recorded discrete airframe damage; gradual wear unchanged. No component inspection or wear service recorded.";
    [JsonIgnore] public override Guid MaintenanceActionId => Request.MaintenanceActionId;
    [JsonIgnore] public override AirframeId AirframeId => Request.AirframeId;
    [JsonIgnore] public override DateTimeOffset PerformedAt => Request.PerformedAt;

    public override void Validate()
    {
        ArgumentNullException.ThrowIfNull(Request);
        ArgumentNullException.ThrowIfNull(After);
        Request.ValidateBefore(Before);
        After.Validate();
        if (Kind != AirframeMaintenanceEventKind.DiscreteDamageRepair
            || Before.Condition.Damage is not (AirframeDamageState.Recorded or AirframeDamageState.Grounding)
            || Before.Airframe != After.Airframe
            || After.Condition != new AirframeCondition(Before.Condition.WearFraction, AirframeDamageState.None)
            || After.Revision != checked(Before.Revision + 1) || After.SavedAt != PerformedAt
            || Rationale != DiscreteRepairRationale)
            throw new InvalidDataException("Repair event does not match its retained identity, condition, revision or semantics.");
    }

    public override AirframeRepairResult Replay(AirframeRepairRequest request)
    {
        Validate();
        if (Request != request)
            throw new InvalidOperationException("Maintenance action ID already belongs to a different repair request.");
        return new(AirframeRepairStatus.Repaired, After, this, WasNewlyApplied: false);
    }
}

public sealed record AirframeRepairResult(
    AirframeRepairStatus Status, AirframeStoreRecord? Current, AirframeMaintenanceEvent? Event, bool WasNewlyApplied);

public sealed record AirframeServiceHistoryCursor(DateTimeOffset PerformedAt, Guid MaintenanceActionId)
{
    public void Validate()
    {
        if (PerformedAt == default || MaintenanceActionId == Guid.Empty)
            throw new ArgumentException("Service history cursor requires time and action identity.");
    }
}

public sealed record AirframeServiceHistoryQuery(AirframeId AirframeId, int Limit = 100, AirframeServiceHistoryCursor? Before = null)
{
    public void Validate()
    {
        AirframeId.Validate();
        if (Limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(Limit));
        Before?.Validate();
    }
}

/// <summary>Newest performed UTC time first, then ordinal D-format action ID descending; separate from flight pagination.</summary>
public sealed record AirframeServiceHistoryPage(ImmutableList<AirframeServiceEvent> Events, AirframeServiceHistoryCursor? Next);
public sealed record AirframeServiceHistorySnapshot(AirframeStoreRecord Current, AirframeServiceHistoryPage History, AirframeServiceState ServiceState);
public sealed record AirframeServiceHistoryReadResult(AirframeId AirframeId, AirframeMaintenanceReadStatus Status, AirframeServiceHistorySnapshot? Snapshot);

public interface IAirframeMaintenanceStore
{
    Task<AirframeServiceEvent?> FindMaintenanceActionAsync(Guid maintenanceActionId, CancellationToken cancellationToken = default);
    Task<AirframeRepairResult> RepairDiscreteDamageAsync(AirframeRepairRequest request, AirframeStoreRecord expected,
        CancellationToken cancellationToken = default);
    Task<AirframeInspectionResult> PerformRoutineInspectionAsync(AirframeInspectionRequest request,
        CancellationToken cancellationToken = default);
    Task<AirframeServiceHistoryPage> ReadServiceHistoryAsync(AirframeServiceHistoryQuery query, CancellationToken cancellationToken = default);
}
