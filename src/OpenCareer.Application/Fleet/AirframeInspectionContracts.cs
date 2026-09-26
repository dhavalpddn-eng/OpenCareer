using System.Text.Json.Serialization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public interface IAirframeServiceStateStore
{
    Task<AirframeServiceState?> ReadServiceStateAsync(AirframeId airframeId, CancellationToken cancellationToken = default);
}

public enum AirframeInspectionResultStatus { Inspected, InspectionNotDue, NotFound }

public sealed record AirframeInspectionRequest(
    [property: JsonRequired] Guid MaintenanceActionId,
    [property: JsonRequired] AirframeId AirframeId,
    [property: JsonRequired] long ExpectedConditionRevision,
    [property: JsonRequired] long ExpectedServiceRevision,
    [property: JsonRequired] DateTimeOffset PerformedAt)
{
    public void Validate()
    {
        if (MaintenanceActionId == Guid.Empty) throw new ArgumentException("Maintenance action ID is required.");
        AirframeId.Validate();
        if (ExpectedConditionRevision < 1 || ExpectedServiceRevision < 1) throw new ArgumentOutOfRangeException(nameof(ExpectedServiceRevision));
        if (PerformedAt == default) throw new ArgumentOutOfRangeException(nameof(PerformedAt));
    }

    public void ValidateBefore(AirframeStoreRecord condition, AirframeServiceState service)
    {
        Validate(); condition.Validate(); service.Validate();
        if (condition.Airframe.AirframeId != AirframeId || service.AirframeId != AirframeId || service.UpdatedAt < condition.Airframe.CreatedAt)
            throw new InvalidDataException("Inspection state does not belong to the requested physical airframe.");
        if (condition.Revision != ExpectedConditionRevision || service.Revision != ExpectedServiceRevision)
            throw new AirframeConcurrencyException("Condition or service state changed before inspection; refresh expected revisions.");
        if (PerformedAt <= service.UpdatedAt || PerformedAt < condition.SavedAt)
            throw new ArgumentOutOfRangeException(nameof(PerformedAt), "Inspection time must advance service state and cannot predate condition.");
    }
}

/// <summary>Common retained service facts. Concrete payload kinds are validated independently.</summary>
public abstract record AirframeServiceEvent(
    [property: JsonRequired] AirframeMaintenanceEventKind Kind,
    [property: JsonRequired] AirframeStoreRecord Before,
    [property: JsonRequired] AirframeStoreRecord After,
    [property: JsonRequired] string Rationale)
{
    [JsonIgnore] public abstract Guid MaintenanceActionId { get; }
    [JsonIgnore] public abstract AirframeId AirframeId { get; }
    [JsonIgnore] public string CanonicalAircraftId => Before.Airframe.CanonicalAircraftId;
    [JsonIgnore] public abstract DateTimeOffset PerformedAt { get; }
    public abstract void Validate();
    public virtual AirframeRepairResult Replay(AirframeRepairRequest request) =>
        throw new InvalidOperationException("Maintenance action ID belongs to a different service action kind.");
    public virtual AirframeInspectionResult Replay(AirframeInspectionRequest request) =>
        throw new InvalidOperationException("Maintenance action ID belongs to a different service action kind.");
}

/// <summary>Inspection acknowledges a usage interval; it makes no repair/component claim.</summary>
public sealed record AirframeRoutineInspectionEvent(
    [property: JsonRequired] AirframeInspectionRequest Request,
    AirframeMaintenanceEventKind Kind,
    AirframeStoreRecord Before,
    AirframeStoreRecord After,
    string Rationale,
    [property: JsonRequired] AirframeServiceState ServiceBefore,
    [property: JsonRequired] AirframeServiceState ServiceAfter) : AirframeServiceEvent(Kind, Before, After, Rationale)
{
    public const string InspectionRationale = "Completed the OpenCareer fallback routine inspection interval; wear and discrete damage unchanged. No component repair or replacement recorded.";
    [JsonIgnore] public override Guid MaintenanceActionId => Request.MaintenanceActionId;
    [JsonIgnore] public override AirframeId AirframeId => Request.AirframeId;
    [JsonIgnore] public override DateTimeOffset PerformedAt => Request.PerformedAt;

    public override void Validate()
    {
        ArgumentNullException.ThrowIfNull(Request); ArgumentNullException.ThrowIfNull(Before);
        ArgumentNullException.ThrowIfNull(After); ArgumentNullException.ThrowIfNull(ServiceBefore);
        ArgumentNullException.ThrowIfNull(ServiceAfter);
        Request.ValidateBefore(Before, ServiceBefore); After.Validate(); ServiceAfter.Validate();
        if (Kind != AirframeMaintenanceEventKind.RoutineInspection || After != Before
            || ServiceAfter != ServiceBefore.Inspect(PerformedAt) || Rationale != InspectionRationale)
            throw new InvalidDataException("Routine inspection does not match its retained condition, service state or semantics.");
    }

    public override AirframeInspectionResult Replay(AirframeInspectionRequest request)
    {
        Validate();
        if (Request != request) throw new InvalidOperationException("Maintenance action ID already belongs to a different inspection request.");
        return new(AirframeInspectionResultStatus.Inspected, After, ServiceAfter, this, false);
    }
}

public sealed record AirframeInspectionResult(AirframeInspectionResultStatus Status, AirframeStoreRecord? Condition,
    AirframeServiceState? ServiceState, AirframeRoutineInspectionEvent? Event, bool WasNewlyApplied);
