using System.Collections.Immutable;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum AirframeMaintenanceScheduleEvidenceStatus
{
    Registered = 1,
    NotFound = 2
}

/// <summary>
/// Explicit enrollment input. It contains no simulator title and cannot infer a component from a
/// canonical model; the persistence authority captures the exact retained service baseline.
/// </summary>
public sealed record AirframeMaintenanceScheduleEvidenceRequest(
    Guid EvidenceId,
    AirframeId AirframeId,
    string ScheduleId,
    int ScheduleVersion,
    AirframeMaintenanceComponent Component,
    string CertifiedAircraftModel,
    string InstalledComponentModel,
    AirframeMaintenanceApplicabilityAuthority Authority,
    string SourceReference,
    AirframeMaintenanceBaselineAuthority BaselineAuthority,
    string BaselineSourceReference,
    long ExpectedServiceRevision,
    DateTimeOffset RecordedAt)
{
    public void Validate()
    {
        if (EvidenceId == Guid.Empty) throw new ArgumentException("Maintenance schedule evidence ID is required.");
        AirframeId.Validate();
        ValidateText(ScheduleId, "Maintenance schedule identity");
        ValidateText(CertifiedAircraftModel, "Certified aircraft model");
        ValidateText(InstalledComponentModel, "Installed component model");
        ValidateText(SourceReference, "Applicability evidence source reference");
        ValidateText(BaselineSourceReference, "Maintenance baseline source reference");
        if (ScheduleVersion < 1 || !Enum.IsDefined(Component)
            || !Enum.IsDefined(Authority)
            || Authority != AirframeMaintenanceApplicabilityAuthority.AuthoritativePhysicalAircraftRecord
            || !Enum.IsDefined(BaselineAuthority)
            || BaselineAuthority != AirframeMaintenanceBaselineAuthority.AuthoritativeMaintenanceRecord
            || ExpectedServiceRevision < 1 || RecordedAt == default)
            throw new ArgumentException("Invalid maintenance schedule applicability request.");
    }

    private static void ValidateText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException($"{field} is missing or not normalized.");
    }

    public bool Matches(AirframeMaintenanceScheduleEvidenceRecord retained)
    {
        ArgumentNullException.ThrowIfNull(retained);
        return EvidenceId == retained.EvidenceId
            && AirframeId == retained.Applicability.AirframeId
            && string.Equals(ScheduleId, retained.Baseline.ScheduleId, StringComparison.Ordinal)
            && ScheduleVersion == retained.Baseline.ScheduleVersion
            && Component == retained.Applicability.Component
            && Component == retained.Baseline.Component
            && string.Equals(CertifiedAircraftModel, retained.Applicability.CertifiedAircraftModel, StringComparison.Ordinal)
            && string.Equals(InstalledComponentModel, retained.Applicability.InstalledComponentModel, StringComparison.Ordinal)
            && Authority == retained.Applicability.Authority
            && string.Equals(SourceReference, retained.Applicability.SourceReference, StringComparison.Ordinal)
            && BaselineAuthority == retained.Baseline.Authority
            && string.Equals(BaselineSourceReference, retained.Baseline.SourceReference, StringComparison.Ordinal)
            && ExpectedServiceRevision == retained.Baseline.SourceServiceRevision
            && RecordedAt == retained.Applicability.RecordedAt
            && RecordedAt == retained.Baseline.EstablishedAt;
    }
}

public sealed record AirframeMaintenanceScheduleEvidenceResult(
    AirframeMaintenanceScheduleEvidenceStatus Status,
    AirframeMaintenanceScheduleEvidenceRecord? Record,
    bool WasNewlyApplied);

/// <summary>Authoritative create-only applicability and schedule-baseline persistence.</summary>
public interface IAirframeMaintenanceScheduleEvidenceStore
{
    Task<AirframeMaintenanceScheduleEvidenceResult> RegisterAsync(
        AirframeMaintenanceScheduleEvidenceRequest request,
        CancellationToken cancellationToken = default);

    Task<ImmutableArray<AirframeMaintenanceScheduleEvidenceRecord>> ReadForAirframeAsync(
        AirframeId airframeId,
        CancellationToken cancellationToken = default);
}
