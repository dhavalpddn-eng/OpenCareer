using System.Text.Json.Serialization;

namespace OpenCareer.Domain.Aircraft;

/// <summary>
/// The retained applicability fact must originate in a record for the physical aircraft.
/// Simulator title, livery, and canonical model identity are deliberately not authorities.
/// </summary>
public enum AirframeMaintenanceApplicabilityAuthority
{
    AuthoritativePhysicalAircraftRecord = 1
}

/// <summary>
/// Proves the proxy counter baseline was deliberately established from retained maintenance
/// evidence, rather than inferred from registration time, simulator identity, or current usage.
/// </summary>
public enum AirframeMaintenanceBaselineAuthority
{
    AuthoritativeMaintenanceRecord = 1
}

/// <summary>Immutable physical-aircraft and installed-component evidence.</summary>
public sealed record AirframeComponentApplicabilityEvidence(
    [property: JsonRequired] AirframeId AirframeId,
    [property: JsonRequired] string CanonicalAircraftId,
    [property: JsonRequired] AirframeMaintenanceComponent Component,
    [property: JsonRequired] string CertifiedAircraftModel,
    [property: JsonRequired] string InstalledComponentModel,
    [property: JsonRequired] AirframeMaintenanceApplicabilityAuthority Authority,
    [property: JsonRequired] string SourceReference,
    [property: JsonRequired] long Revision,
    [property: JsonRequired] DateTimeOffset RecordedAt)
{
    public void Validate(Airframe airframe)
    {
        ArgumentNullException.ThrowIfNull(airframe);
        AirframeId.Validate();
        ValidateText(CanonicalAircraftId, "Canonical aircraft identity");
        ValidateText(CertifiedAircraftModel, "Certified aircraft model");
        ValidateText(InstalledComponentModel, "Installed component model");
        ValidateText(SourceReference, "Applicability evidence source reference");
        if (AirframeId != airframe.AirframeId
            || !string.Equals(CanonicalAircraftId, airframe.CanonicalAircraftId, StringComparison.Ordinal))
            throw new InvalidDataException("Component applicability evidence belongs to a different physical airframe/model.");
        if (!Enum.IsDefined(Component) || !Enum.IsDefined(Authority)
            || Authority != AirframeMaintenanceApplicabilityAuthority.AuthoritativePhysicalAircraftRecord
            || Revision != 1 || RecordedAt == default || RecordedAt < airframe.CreatedAt)
            throw new InvalidDataException("Invalid physical-airframe component applicability evidence.");
    }

    internal static void ValidateText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl))
            throw new InvalidDataException($"{field} is missing or not normalized.");
    }
}

/// <summary>
/// Immutable OpenCareer tracked-usage baseline for one versioned schedule. Tracked airborne time
/// remains a documented proxy for manufacturer operating time; landing cycles are factual context
/// only unless the referenced schedule defines a cycle interval.
/// </summary>
public sealed record AirframeMaintenanceServiceBaseline(
    [property: JsonRequired] AirframeId AirframeId,
    [property: JsonRequired] string ScheduleId,
    [property: JsonRequired] int ScheduleVersion,
    [property: JsonRequired] AirframeMaintenanceComponent Component,
    [property: JsonRequired] long ApplicabilityRevision,
    [property: JsonRequired] TimeSpan TrackedAirborneTime,
    [property: JsonRequired] long TrackedLandingCycles,
    [property: JsonRequired] AirframeUsageOrigin AirborneTimeOrigin,
    [property: JsonRequired] AirframeUsageOrigin LandingCycleOrigin,
    [property: JsonRequired] AirframeMaintenanceBaselineAuthority Authority,
    [property: JsonRequired] string SourceReference,
    [property: JsonRequired] long SourceServiceRevision,
    [property: JsonRequired] DateTimeOffset SourceServiceUpdatedAt,
    [property: JsonRequired] long Revision,
    [property: JsonRequired] DateTimeOffset EstablishedAt)
{
    public void Validate(
        Airframe airframe,
        AirframeServiceState currentService,
        AirframeComponentApplicabilityEvidence applicability)
    {
        ArgumentNullException.ThrowIfNull(airframe);
        ArgumentNullException.ThrowIfNull(currentService);
        ArgumentNullException.ThrowIfNull(applicability);
        applicability.Validate(airframe);
        currentService.Validate();
        AirframeId.Validate();
        AirframeComponentApplicabilityEvidence.ValidateText(ScheduleId, "Maintenance schedule identity");
        AirframeComponentApplicabilityEvidence.ValidateText(SourceReference, "Maintenance baseline source reference");
        if (AirframeId != airframe.AirframeId || currentService.AirframeId != airframe.AirframeId
            || applicability.AirframeId != airframe.AirframeId)
            throw new InvalidDataException("Maintenance service baseline belongs to a different physical airframe.");
        if (ScheduleVersion < 1 || !Enum.IsDefined(Component) || Component != applicability.Component
            || ApplicabilityRevision != applicability.Revision || ApplicabilityRevision < 1
            || TrackedAirborneTime < TimeSpan.Zero || TrackedLandingCycles < 0
            || !Enum.IsDefined(AirborneTimeOrigin) || !Enum.IsDefined(LandingCycleOrigin)
            || !Enum.IsDefined(Authority)
            || Authority != AirframeMaintenanceBaselineAuthority.AuthoritativeMaintenanceRecord
            || SourceServiceRevision < 1 || Revision != 1
            || SourceServiceUpdatedAt == default || EstablishedAt == default
            || SourceServiceUpdatedAt < airframe.CreatedAt || EstablishedAt < SourceServiceUpdatedAt
            || EstablishedAt != applicability.RecordedAt)
            throw new InvalidDataException("Invalid versioned maintenance service baseline.");
        if (SourceServiceRevision > currentService.Revision
            || SourceServiceUpdatedAt > currentService.UpdatedAt
            || TrackedAirborneTime > currentService.TotalTrackedAirborneTime
            || TrackedLandingCycles > currentService.TotalTrackedLandingCycles
            || AirborneTimeOrigin != currentService.UsageOrigin
            || LandingCycleOrigin != currentService.LandingCycleOrigin)
            throw new InvalidDataException("Maintenance service baseline is inconsistent with retained tracked usage.");
        if (SourceServiceRevision == currentService.Revision
            && (SourceServiceUpdatedAt != currentService.UpdatedAt
                || TrackedAirborneTime != currentService.TotalTrackedAirborneTime
                || TrackedLandingCycles != currentService.TotalTrackedLandingCycles))
            throw new InvalidDataException("Maintenance service baseline does not match its retained source revision.");
    }
}

/// <summary>
/// Create-only evidence binding exact physical configuration to one exact schedule version and
/// its captured service baseline. It does not replace the generic operational inspection schedule.
/// </summary>
public sealed record AirframeMaintenanceScheduleEvidenceRecord(
    [property: JsonRequired] Guid EvidenceId,
    [property: JsonRequired] AirframeComponentApplicabilityEvidence Applicability,
    [property: JsonRequired] AirframeMaintenanceServiceBaseline Baseline,
    [property: JsonRequired] int PayloadSchemaVersion)
{
    public const int CurrentPayloadSchemaVersion = 1;

    public void Validate(Airframe airframe, AirframeServiceState currentService)
    {
        if (EvidenceId == Guid.Empty) throw new InvalidDataException("Maintenance schedule evidence identity is required.");
        ArgumentNullException.ThrowIfNull(Applicability);
        ArgumentNullException.ThrowIfNull(Baseline);
        if (PayloadSchemaVersion != CurrentPayloadSchemaVersion)
            throw new NotSupportedException("Unsupported maintenance schedule evidence payload schema.");
        Applicability.Validate(airframe);
        Baseline.Validate(airframe, currentService, Applicability);
        AirframeMaintenanceScheduleDefinition definition = AirframeMaintenanceScheduleCatalog
            .FindCandidates(airframe.CanonicalAircraftId)
            .SingleOrDefault(candidate => candidate.ScheduleId == Baseline.ScheduleId
                && candidate.Version == Baseline.ScheduleVersion)
            ?? throw new InvalidDataException("Retained evidence references an unsupported schedule/version for this airframe model.");
        if (definition.Component != Applicability.Component || definition.Component != Baseline.Component)
            throw new InvalidDataException("Retained schedule evidence references the wrong maintenance component.");
    }

    public AirframeMaintenanceApplicabilityEvidence ToApplicabilityEvidence() => new(
        Applicability.AirframeId,
        Applicability.CertifiedAircraftModel,
        Applicability.InstalledComponentModel,
        Baseline.TrackedAirborneTime,
        Baseline.TrackedLandingCycles);
}
