using System.Collections.Immutable;

namespace OpenCareer.Domain.Aircraft;

public enum AirframeMaintenanceComponent { Engine = 1 }
public enum AirframeMaintenanceScheduleStatus
{
    ApplicabilityEvidenceRequired = 1,
    ApplicabilityMismatch = 2,
    TrackedProxyCurrent = 3,
    TrackedProxyDue = 4
}

[Flags]
public enum AirframeMaintenanceDueReason
{
    None = 0,
    TrackedAirborneTime = 1,
    TrackedLandingCycles = 2
}

[Flags]
public enum AirframeMaintenanceEvidenceRequirement
{
    None = 0,
    CertifiedAircraftModel = 1,
    InstalledComponentModel = 2,
    ComponentUsageBaseline = 4
}

public sealed record AirframeMaintenanceScheduleSource(
    string Organization,
    string DocumentTitle,
    string DocumentIdentifier,
    string RevisionOrPublication,
    string Url,
    string FactSupported)
{
    public void Validate()
    {
        foreach (string value in new[] { Organization, DocumentTitle, DocumentIdentifier,
                     RevisionOrPublication, Url, FactSupported })
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl))
                throw new InvalidDataException("Maintenance schedule provenance is incomplete or not normalized.");
        }
        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Maintenance schedule provenance must use an absolute HTTPS URL.");
    }
}

/// <summary>
/// Verified interval data is separate from applicability and service history. The manufacturer
/// operating-time interval is evaluated against OpenCareer tracked airborne time only as an
/// explicit gameplay proxy; it is not represented as manufacturer-validated meter equivalence.
/// </summary>
public sealed record AirframeMaintenanceScheduleDefinition(
    string ScheduleId,
    int Version,
    string CanonicalAircraftId,
    string RequiredCertifiedAircraftModel,
    string RequiredComponentModel,
    string DisplayName,
    AirframeMaintenanceComponent Component,
    TimeSpan? ManufacturerOperatingTimeInterval,
    long? LandingCycleInterval,
    ImmutableArray<AirframeMaintenanceScheduleSource> Sources,
    string Limitations)
{
    public void Validate()
    {
        foreach (string value in new[] { ScheduleId, CanonicalAircraftId, RequiredCertifiedAircraftModel,
                     RequiredComponentModel, DisplayName, Limitations })
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl))
                throw new InvalidDataException("Maintenance schedule definition is incomplete or not normalized.");
        }
        if (Version < 1 || !Enum.IsDefined(Component)
            || (ManufacturerOperatingTimeInterval is { } time && time <= TimeSpan.Zero)
            || (LandingCycleInterval is { } cycles && cycles <= 0)
            || (ManufacturerOperatingTimeInterval is null && LandingCycleInterval is null)
            || Sources.IsDefaultOrEmpty)
            throw new InvalidDataException("Invalid maintenance schedule definition.");
        foreach (AirframeMaintenanceScheduleSource source in Sources) source.Validate();
    }
}

/// <summary>Explicit physical configuration and service baseline; simulator TITLE cannot supply this.</summary>
public sealed record AirframeMaintenanceApplicabilityEvidence(
    AirframeId AirframeId,
    string CertifiedAircraftModel,
    string InstalledComponentModel,
    TimeSpan BaselineAtTrackedAirborneTime,
    long BaselineAtTrackedLandingCycles)
{
    public void Validate(Airframe airframe, AirframeServiceState serviceState)
    {
        AirframeId.Validate();
        if (AirframeId != airframe.AirframeId || serviceState.AirframeId != airframe.AirframeId)
            throw new InvalidDataException("Maintenance applicability evidence belongs to a different physical airframe.");
        if (serviceState.UpdatedAt < airframe.CreatedAt)
            throw new InvalidDataException("Maintenance service evidence predates the physical airframe record.");
        foreach (string value in new[] { CertifiedAircraftModel, InstalledComponentModel })
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl))
                throw new InvalidDataException("Maintenance applicability evidence is incomplete or not normalized.");
        }
        if (BaselineAtTrackedAirborneTime < TimeSpan.Zero
            || BaselineAtTrackedAirborneTime > serviceState.TotalTrackedAirborneTime
            || BaselineAtTrackedLandingCycles < 0
            || BaselineAtTrackedLandingCycles > serviceState.TotalTrackedLandingCycles)
            throw new InvalidDataException("Component service baseline is outside retained tracked usage.");
    }
}

public sealed record AirframeMaintenanceScheduleAssessment(
    AirframeMaintenanceScheduleDefinition Definition,
    AirframeMaintenanceScheduleStatus Status,
    AirframeMaintenanceEvidenceRequirement MissingEvidence,
    AirframeMaintenanceDueReason DueReasons,
    TimeSpan? TrackedAirborneTimeSinceBaseline,
    long? TrackedLandingCyclesSinceBaseline,
    TimeSpan? TrackedAirborneTimeUntilDue,
    long? TrackedLandingCyclesUntilDue)
{
    public bool IsDue => Status == AirframeMaintenanceScheduleStatus.TrackedProxyDue;
}

/// <summary>
/// Primary-source schedule catalog. A matching canonical model exposes a candidate; it never
/// activates that component schedule without explicit physical configuration and baseline evidence.
/// </summary>
public static class AirframeMaintenanceScheduleCatalog
{
    public const string SkyhawkIo360ScheduleId = "Cessna172S.LycomingIo360L2A.EngineInspection";
    public const int SkyhawkIo360ScheduleVersion = 1;

    public static AirframeMaintenanceScheduleDefinition SkyhawkIo360EngineInspection { get; } = new(
        SkyhawkIo360ScheduleId,
        SkyhawkIo360ScheduleVersion,
        AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId,
        "172S",
        "IO-360-L2A",
        "Lycoming IO-360 50-hour engine inspection",
        AirframeMaintenanceComponent.Engine,
        TimeSpan.FromHours(50),
        LandingCycleInterval: null,
        ImmutableArray.Create(
            new AirframeMaintenanceScheduleSource(
                "Federal Aviation Administration",
                "Type Certificate Data Sheet No. 3A12",
                "3A12",
                "Revision 88, 2025-06-27",
                "https://drs.faa.gov/browse/excelExternalWindow/DRSDOCID153801600120250627164731.0001",
                "The approved Model 172S configuration identifies the Lycoming IO-360-L2A engine."),
            new AirframeMaintenanceScheduleSource(
                "Lycoming Engines",
                "O-360, HO-360, IO-360, AIO-360, HIO-360 & TIO-360 Operator's Manual",
                "60297-12 and 60297-12-5",
                "December 2009 revision",
                "https://www.lycoming.com/sites/default/files/file/2025-11/60297-12%20-%20O-360%2C%20HO-360%2C%20IO-360%2C%20AIO-360%2C%20HIO-360%2C%20and%20TIO-360%20Series.pdf",
                "Section 4 specifies a recurring 50-operating-hour engine inspection for the IO-360 series.")),
        "Requires verified 172S and installed IO-360-L2A identity plus a component usage baseline. Initial 25-hour and prior-life applicability are not inferred. No landing-cycle threshold is established.");

    private static readonly ImmutableArray<AirframeMaintenanceScheduleDefinition> Verified =
        [SkyhawkIo360EngineInspection];

    static AirframeMaintenanceScheduleCatalog()
    {
        foreach (AirframeMaintenanceScheduleDefinition definition in Verified) definition.Validate();
        if (Verified.GroupBy(definition => (definition.ScheduleId, definition.Version)).Any(group => group.Count() != 1))
            throw new InvalidDataException("Maintenance schedule identity/version must be unique.");
    }

    public static ImmutableArray<AirframeMaintenanceScheduleDefinition> FindCandidates(string canonicalAircraftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        if (canonicalAircraftId != canonicalAircraftId.Trim() || canonicalAircraftId.Any(char.IsControl))
            throw new ArgumentException("Canonical aircraft identity must be normalized.", nameof(canonicalAircraftId));
        return Verified.Where(definition =>
            string.Equals(definition.CanonicalAircraftId, canonicalAircraftId, StringComparison.Ordinal)).ToImmutableArray();
    }

    public static ImmutableArray<AirframeMaintenanceScheduleAssessment> AssessCandidates(
        Airframe airframe,
        AirframeServiceState serviceState)
    {
        ArgumentNullException.ThrowIfNull(airframe);
        ArgumentNullException.ThrowIfNull(serviceState);
        serviceState.Validate();
        if (serviceState.AirframeId != airframe.AirframeId)
            throw new InvalidDataException("Maintenance schedule inputs belong to different physical airframes.");
        return FindCandidates(airframe.CanonicalAircraftId)
            .Select(definition => AssessDefinition(airframe, serviceState, definition, evidence: null))
            .ToImmutableArray();
    }

    public static AirframeMaintenanceScheduleAssessment Assess(
        Airframe airframe,
        AirframeServiceState serviceState,
        string scheduleId,
        int scheduleVersion,
        AirframeMaintenanceApplicabilityEvidence? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(airframe);
        ArgumentNullException.ThrowIfNull(serviceState);
        serviceState.Validate();
        if (serviceState.AirframeId != airframe.AirframeId)
            throw new InvalidDataException("Maintenance schedule inputs belong to different physical airframes.");
        AirframeMaintenanceScheduleDefinition definition = FindCandidates(airframe.CanonicalAircraftId)
            .SingleOrDefault(candidate => candidate.ScheduleId == scheduleId && candidate.Version == scheduleVersion)
            ?? throw new NotSupportedException("The requested verified maintenance schedule does not apply to this canonical aircraft.");
        return AssessDefinition(airframe, serviceState, definition, evidence);
    }

    private static AirframeMaintenanceScheduleAssessment AssessDefinition(
        Airframe airframe,
        AirframeServiceState serviceState,
        AirframeMaintenanceScheduleDefinition definition,
        AirframeMaintenanceApplicabilityEvidence? evidence)
    {
        if (evidence is null)
            return new(definition, AirframeMaintenanceScheduleStatus.ApplicabilityEvidenceRequired,
                AirframeMaintenanceEvidenceRequirement.CertifiedAircraftModel
                | AirframeMaintenanceEvidenceRequirement.InstalledComponentModel
                | AirframeMaintenanceEvidenceRequirement.ComponentUsageBaseline,
                AirframeMaintenanceDueReason.None, null, null, null, null);

        evidence.Validate(airframe, serviceState);
        if (!string.Equals(evidence.CertifiedAircraftModel, definition.RequiredCertifiedAircraftModel, StringComparison.Ordinal)
            || !string.Equals(evidence.InstalledComponentModel, definition.RequiredComponentModel, StringComparison.Ordinal))
            return new(definition, AirframeMaintenanceScheduleStatus.ApplicabilityMismatch,
                AirframeMaintenanceEvidenceRequirement.None, AirframeMaintenanceDueReason.None,
                null, null, null, null);

        TimeSpan trackedTime = serviceState.TotalTrackedAirborneTime - evidence.BaselineAtTrackedAirborneTime;
        long trackedCycles = serviceState.TotalTrackedLandingCycles - evidence.BaselineAtTrackedLandingCycles;
        AirframeMaintenanceDueReason reasons = AirframeMaintenanceDueReason.None;
        TimeSpan? timeRemaining = null;
        long? cyclesRemaining = null;
        if (definition.ManufacturerOperatingTimeInterval is { } interval)
        {
            if (trackedTime >= interval) reasons |= AirframeMaintenanceDueReason.TrackedAirborneTime;
            timeRemaining = trackedTime >= interval ? TimeSpan.Zero : interval - trackedTime;
        }
        if (definition.LandingCycleInterval is { } cycleInterval)
        {
            if (trackedCycles >= cycleInterval) reasons |= AirframeMaintenanceDueReason.TrackedLandingCycles;
            cyclesRemaining = Math.Max(0, cycleInterval - trackedCycles);
        }
        return new(definition,
            reasons == AirframeMaintenanceDueReason.None
                ? AirframeMaintenanceScheduleStatus.TrackedProxyCurrent
                : AirframeMaintenanceScheduleStatus.TrackedProxyDue,
            AirframeMaintenanceEvidenceRequirement.None, reasons, trackedTime, trackedCycles, timeRemaining, cyclesRemaining);
    }
}
