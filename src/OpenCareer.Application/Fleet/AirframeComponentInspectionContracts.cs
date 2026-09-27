using System.Text.Json.Serialization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum AirframeComponentInspectionResultStatus
{
    Inspected = 1,
    InspectionNotDue = 2,
    AirframeNotFound = 3,
    ScheduleEvidenceNotFound = 4
}

/// <summary>
/// One verified component inspection. Identity and expected revisions bind the action to the
/// exact physical aircraft, physical configuration evidence, schedule and authoritative usage.
/// Usage counters are deliberately not caller supplied.
/// </summary>
public sealed record AirframeComponentInspectionRequest(
    [property: JsonRequired] Guid MaintenanceActionId,
    [property: JsonRequired] Guid EvidenceId,
    [property: JsonRequired] AirframeId AirframeId,
    [property: JsonRequired] string CanonicalAircraftId,
    [property: JsonRequired] string CertifiedAircraftModel,
    [property: JsonRequired] string InstalledComponentModel,
    [property: JsonRequired] AirframeMaintenanceComponent Component,
    [property: JsonRequired] string ScheduleId,
    [property: JsonRequired] int ScheduleVersion,
    [property: JsonRequired] long ExpectedApplicabilityRevision,
    [property: JsonRequired] long ExpectedBaselineRevision,
    [property: JsonRequired] long ExpectedConditionRevision,
    [property: JsonRequired] long ExpectedServiceRevision,
    [property: JsonRequired] AirframeMaintenanceBaselineAuthority Authority,
    [property: JsonRequired] string ServiceRecordReference,
    [property: JsonRequired] DateTimeOffset PerformedAt)
{
    public void Validate()
    {
        if (MaintenanceActionId == Guid.Empty)
            throw new ArgumentException("Maintenance action ID is required.");
        if (EvidenceId == Guid.Empty)
            throw new ArgumentException("Maintenance schedule evidence ID is required.");
        AirframeId.Validate();
        ValidateText(CanonicalAircraftId, "Canonical aircraft identity");
        ValidateText(CertifiedAircraftModel, "Certified aircraft model");
        ValidateText(InstalledComponentModel, "Installed component model");
        ValidateText(ScheduleId, "Maintenance schedule identity");
        ValidateText(ServiceRecordReference, "Component service record reference");
        if (!Enum.IsDefined(Component) || ScheduleVersion < 1
            || ExpectedApplicabilityRevision < 1 || ExpectedBaselineRevision < 1
            || ExpectedConditionRevision < 1 || ExpectedServiceRevision < 1
            || !Enum.IsDefined(Authority)
            || Authority != AirframeMaintenanceBaselineAuthority.AuthoritativeMaintenanceRecord
            || PerformedAt == default)
            throw new ArgumentException("Invalid verified component inspection request.");
    }

    public void ValidateBefore(
        AirframeStoreRecord condition,
        AirframeServiceState authoritativeService,
        AirframeMaintenanceScheduleEvidenceRecord scheduleState)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(authoritativeService);
        ArgumentNullException.ThrowIfNull(scheduleState);
        condition.Validate();
        authoritativeService.Validate();
        scheduleState.Validate(condition.Airframe, authoritativeService);

        if (condition.Airframe.AirframeId != AirframeId
            || authoritativeService.AirframeId != AirframeId
            || scheduleState.Applicability.AirframeId != AirframeId
            || scheduleState.Baseline.AirframeId != AirframeId)
            throw new InvalidDataException("Component inspection state belongs to a different physical airframe.");
        if (!string.Equals(condition.Airframe.CanonicalAircraftId, CanonicalAircraftId, StringComparison.Ordinal)
            || !string.Equals(scheduleState.Applicability.CanonicalAircraftId, CanonicalAircraftId, StringComparison.Ordinal)
            || scheduleState.EvidenceId != EvidenceId
            || !string.Equals(scheduleState.Applicability.CertifiedAircraftModel, CertifiedAircraftModel, StringComparison.Ordinal)
            || !string.Equals(scheduleState.Applicability.InstalledComponentModel, InstalledComponentModel, StringComparison.Ordinal)
            || scheduleState.Applicability.Component != Component
            || scheduleState.Baseline.Component != Component
            || !string.Equals(scheduleState.Baseline.ScheduleId, ScheduleId, StringComparison.Ordinal)
            || scheduleState.Baseline.ScheduleVersion != ScheduleVersion
            || scheduleState.Applicability.Revision != ExpectedApplicabilityRevision)
            throw new InvalidDataException("Component inspection does not exactly match retained physical schedule evidence.");
        if (condition.Revision != ExpectedConditionRevision
            || authoritativeService.Revision != ExpectedServiceRevision
            || scheduleState.Baseline.Revision != ExpectedBaselineRevision)
            throw new AirframeConcurrencyException(
                "Condition, usage or component baseline changed before inspection; refresh expected revisions.");
        if (PerformedAt <= scheduleState.Baseline.EstablishedAt
            || PerformedAt <= authoritativeService.UpdatedAt
            || PerformedAt < condition.SavedAt)
            throw new ArgumentOutOfRangeException(
                nameof(PerformedAt),
                "Component inspection time must advance retained schedule and usage evidence.");

        AirframeMaintenanceScheduleDefinition definition = AirframeMaintenanceScheduleCatalog
            .FindCandidates(CanonicalAircraftId)
            .SingleOrDefault(candidate => candidate.ScheduleId == ScheduleId
                && candidate.Version == ScheduleVersion)
            ?? throw new NotSupportedException(
                "The requested verified component schedule does not apply to this aircraft model.");
        if (definition.Component != Component
            || !string.Equals(definition.RequiredCertifiedAircraftModel, CertifiedAircraftModel, StringComparison.Ordinal)
            || !string.Equals(definition.RequiredComponentModel, InstalledComponentModel, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Physical component evidence does not exactly activate the requested verified schedule.");
    }

    private static void ValidateText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException($"{field} is missing or not normalized.");
    }
}

/// <summary>
/// Immutable component-service history. It advances a tracked-usage proxy baseline only; condition,
/// the generic fallback inspection schedule and physical applicability evidence are unchanged.
/// </summary>
public sealed record AirframeComponentInspectionEvent(
    [property: JsonRequired] AirframeComponentInspectionRequest Request,
    AirframeMaintenanceEventKind Kind,
    AirframeStoreRecord Before,
    AirframeStoreRecord After,
    string Rationale,
    [property: JsonRequired] AirframeServiceState AuthoritativeServiceState,
    [property: JsonRequired] AirframeMaintenanceScheduleEvidenceRecord ScheduleBefore,
    [property: JsonRequired] AirframeMaintenanceScheduleEvidenceRecord ScheduleAfter)
    : AirframeServiceEvent(Kind, Before, After, Rationale)
{
    public const string InspectionRationale =
        "Completed the exact verified component inspection schedule and advanced its OpenCareer tracked-usage proxy baseline; no repair, replacement, condition change or generic routine inspection recorded.";

    [JsonIgnore] public override Guid MaintenanceActionId => Request.MaintenanceActionId;
    [JsonIgnore] public override AirframeId AirframeId => Request.AirframeId;
    [JsonIgnore] public override DateTimeOffset PerformedAt => Request.PerformedAt;

    public override void Validate()
    {
        ArgumentNullException.ThrowIfNull(Request);
        ArgumentNullException.ThrowIfNull(Before);
        ArgumentNullException.ThrowIfNull(After);
        ArgumentNullException.ThrowIfNull(AuthoritativeServiceState);
        ArgumentNullException.ThrowIfNull(ScheduleBefore);
        ArgumentNullException.ThrowIfNull(ScheduleAfter);
        Request.ValidateBefore(Before, AuthoritativeServiceState, ScheduleBefore);
        After.Validate();

        AirframeMaintenanceScheduleAssessment beforeAssessment = AirframeMaintenanceScheduleCatalog
            .AssessCandidates(Before.Airframe, AuthoritativeServiceState, [ScheduleBefore])
            .Single(assessment => assessment.Definition.ScheduleId == Request.ScheduleId
                && assessment.Definition.Version == Request.ScheduleVersion);
        AirframeMaintenanceServiceBaseline expectedBaseline = ScheduleBefore.Baseline.Advance(
            AuthoritativeServiceState,
            Request.ServiceRecordReference,
            Request.PerformedAt);
        var expectedAfter = ScheduleBefore with { Baseline = expectedBaseline };
        AirframeMaintenanceScheduleAssessment afterAssessment = AirframeMaintenanceScheduleCatalog
            .AssessCandidates(Before.Airframe, AuthoritativeServiceState, [ScheduleAfter])
            .Single(assessment => assessment.Definition.ScheduleId == Request.ScheduleId
                && assessment.Definition.Version == Request.ScheduleVersion);

        if (Kind != AirframeMaintenanceEventKind.VerifiedComponentInspection
            || After != Before
            || Rationale != InspectionRationale
            || beforeAssessment.Status != AirframeMaintenanceScheduleStatus.TrackedProxyDue
            || ScheduleAfter != expectedAfter
            || afterAssessment.Status != AirframeMaintenanceScheduleStatus.TrackedProxyCurrent
            || afterAssessment.TrackedAirborneTimeSinceBaseline != TimeSpan.Zero
            || afterAssessment.TrackedLandingCyclesSinceBaseline != 0)
            throw new InvalidDataException(
                "Verified component inspection does not match retained identity, usage baseline or semantics.");
    }

    public override AirframeComponentInspectionResult Replay(AirframeComponentInspectionRequest request)
    {
        Validate();
        if (Request != request)
            throw new InvalidOperationException(
                "Maintenance action ID already belongs to a different component inspection request.");
        return new(
            AirframeComponentInspectionResultStatus.Inspected,
            After,
            AuthoritativeServiceState,
            ScheduleAfter,
            this,
            WasNewlyApplied: false);
    }
}

public sealed record AirframeComponentInspectionResult(
    AirframeComponentInspectionResultStatus Status,
    AirframeStoreRecord? Condition,
    AirframeServiceState? ServiceState,
    AirframeMaintenanceScheduleEvidenceRecord? ScheduleState,
    AirframeComponentInspectionEvent? Event,
    bool WasNewlyApplied);
