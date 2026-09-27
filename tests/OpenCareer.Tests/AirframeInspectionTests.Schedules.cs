using System.Collections.Immutable;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed partial class AirframeInspectionTests
{
    [Fact]
    public void VerifiedCatalogRetainsExactScopeVersionAndPrimarySources()
    {
        AirframeMaintenanceScheduleDefinition schedule = Assert.IsType<AirframeMaintenanceScheduleDefinition>(
            AirframeMaintenanceScheduleCatalog.FindCandidates(AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId).Single());

        Assert.Equal(AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleId, schedule.ScheduleId);
        Assert.Equal(1, schedule.Version);
        Assert.Equal("172S", schedule.RequiredCertifiedAircraftModel);
        Assert.Equal("IO-360-L2A", schedule.RequiredComponentModel);
        Assert.Equal(TimeSpan.FromHours(50), schedule.ManufacturerOperatingTimeInterval!.Value);
        Assert.Null(schedule.LandingCycleInterval);
        Assert.Equal(2, schedule.Sources.Length);
        Assert.Contains(schedule.Sources, source => source.Organization == "Federal Aviation Administration"
            && source.DocumentIdentifier == "3A12" && source.RevisionOrPublication == "Revision 88, 2025-06-27");
        Assert.Contains(schedule.Sources, source => source.Organization == "Lycoming Engines"
            && source.DocumentIdentifier == "60297-12 and 60297-12-5");
        schedule.Validate();

        Assert.Empty(AirframeMaintenanceScheduleCatalog.FindCandidates("C172SP Classic Passengers"));
        Assert.Empty(AirframeMaintenanceScheduleCatalog.FindCandidates("msfs-title:C172SP Classic Passengers"));
        Assert.Empty(AirframeMaintenanceScheduleCatalog.FindCandidates("msfs-title:Unseeded Aircraft"));
    }

    [Fact]
    public void InvalidScheduleDefinitionsFailClosed()
    {
        AirframeMaintenanceScheduleDefinition valid = AirframeMaintenanceScheduleCatalog.SkyhawkIo360EngineInspection;
        Assert.Throws<InvalidDataException>(() => (valid with { CanonicalAircraftId = "" }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with
        {
            Sources = default(ImmutableArray<AirframeMaintenanceScheduleSource>)
        }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with
        {
            ManufacturerOperatingTimeInterval = null,
            LandingCycleInterval = null
        }).Validate());
    }

    [Fact]
    public void ComponentScheduleNeedsExplicitPhysicalConfigurationAndBaseline()
    {
        var airframe = new Airframe(new(Guid.NewGuid()), AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId, Epoch);
        AirframeServiceState service = AirframeServiceState.Initial(airframe.AirframeId, Epoch,
            AirframeUsageOrigin.TrackingFromCreation);

        AirframeMaintenanceScheduleAssessment assessment = Assert.IsType<AirframeMaintenanceScheduleAssessment>(
            AirframeMaintenanceScheduleCatalog.AssessCandidates(airframe, service).Single());

        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityEvidenceRequired, assessment.Status);
        Assert.Equal(AirframeMaintenanceEvidenceRequirement.CertifiedAircraftModel
            | AirframeMaintenanceEvidenceRequirement.InstalledComponentModel
            | AirframeMaintenanceEvidenceRequirement.ComponentUsageBaseline, assessment.MissingEvidence);
        Assert.Null(assessment.TrackedAirborneTimeSinceBaseline);
        Assert.Null(assessment.TrackedLandingCyclesSinceBaseline);
        Assert.False(assessment.IsDue);
    }

    [Fact]
    public void ExplicitMismatchNeverActivatesComponentSchedule()
    {
        var airframe = new Airframe(new(Guid.NewGuid()), AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId, Epoch);
        AirframeServiceState service = AirframeServiceState.Initial(airframe.AirframeId, Epoch,
            AirframeUsageOrigin.TrackingFromCreation);
        var evidence = new AirframeMaintenanceApplicabilityEvidence(airframe.AirframeId, "172S", "Continental O-300",
            TimeSpan.Zero, 0);

        AirframeMaintenanceScheduleAssessment assessment = Assert.IsType<AirframeMaintenanceScheduleAssessment>(
            AirframeMaintenanceScheduleCatalog.Assess(airframe, service,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleId,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleVersion, evidence));

        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityMismatch, assessment.Status);
        Assert.Equal(AirframeMaintenanceDueReason.None, assessment.DueReasons);
        Assert.False(assessment.IsDue);
    }

    [Fact]
    public void WrongPhysicalIdentityAndOutOfRangeBaselineFailClosed()
    {
        var airframe = new Airframe(new(Guid.NewGuid()), AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId, Epoch);
        AirframeServiceState service = AirframeServiceState.Initial(airframe.AirframeId, Epoch,
            AirframeUsageOrigin.TrackingFromCreation).AddTrustedUsage(TimeSpan.FromHours(1), 1, Epoch.AddMinutes(1));

        AirframeMaintenanceScheduleAssessment Assess(AirframeMaintenanceApplicabilityEvidence evidence) =>
            AirframeMaintenanceScheduleCatalog.Assess(airframe, service,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleId,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleVersion, evidence);
        Assert.Throws<InvalidDataException>(() => Assess(
            new(new AirframeId(Guid.NewGuid()), "172S", "IO-360-L2A", TimeSpan.Zero, 0)));
        Assert.Throws<InvalidDataException>(() => Assess(
            new(airframe.AirframeId, "172S", "IO-360-L2A", TimeSpan.FromHours(2), 0)));
        Assert.Throws<InvalidDataException>(() => Assess(
            new(airframe.AirframeId, "172S", "IO-360-L2A", TimeSpan.Zero, 2)));
    }

    [Fact]
    public void VerifiedIntervalUsesExactTrackedTicksAndLandingCyclesDoNotInventDueState()
    {
        var airframe = new Airframe(new(Guid.NewGuid()), AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId, Epoch);
        AirframeServiceState initial = AirframeServiceState.Initial(airframe.AirframeId, Epoch,
            AirframeUsageOrigin.TrackingFromCreation);
        AirframeServiceState before = initial.AddTrustedUsage(TimeSpan.FromHours(50) - TimeSpan.FromTicks(1),
            landingCycles: 10_000, Epoch.AddMinutes(1));
        var evidence = new AirframeMaintenanceApplicabilityEvidence(airframe.AirframeId, "172S", "IO-360-L2A",
            TimeSpan.Zero, 0);

        AirframeMaintenanceScheduleAssessment current = Assert.IsType<AirframeMaintenanceScheduleAssessment>(
            AirframeMaintenanceScheduleCatalog.Assess(airframe, before,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleId,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleVersion, evidence));
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyCurrent, current.Status);
        Assert.Equal(TimeSpan.FromTicks(1), current.TrackedAirborneTimeUntilDue!.Value);
        Assert.Equal(10_000L, current.TrackedLandingCyclesSinceBaseline!.Value);
        Assert.Null(current.TrackedLandingCyclesUntilDue);
        Assert.Equal(AirframeMaintenanceDueReason.None, current.DueReasons);

        AirframeServiceState boundary = before.AddTrustedUsage(TimeSpan.FromTicks(1), landingCycles: 0, Epoch.AddMinutes(2));
        AirframeMaintenanceScheduleAssessment due = Assert.IsType<AirframeMaintenanceScheduleAssessment>(
            AirframeMaintenanceScheduleCatalog.Assess(airframe, boundary,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleId,
                AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleVersion, evidence));
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyDue, due.Status);
        Assert.Equal(AirframeMaintenanceDueReason.TrackedAirborneTime, due.DueReasons);
        Assert.Equal(TimeSpan.Zero, due.TrackedAirborneTimeUntilDue!.Value);
        Assert.Null(due.TrackedLandingCyclesUntilDue);
    }

    [Fact]
    public async Task PersistedUsageProjectsCandidateButKeepsOperationalFallbackAcrossRestart()
    {
        AirframeStoreRecord airframe = await CreateAsync();
        await FlyAsync(airframe, TimeSpan.FromHours(50), verticalSpeeds: [-500]);
        AirframeMaintenanceSnapshot snapshot = Assert.IsType<AirframeMaintenanceSnapshot>(
            (await History().ReadAsync(new(airframe.Airframe.AirframeId))).Snapshot);

        Assert.Equal(LightAircraftRoutineInspectionV1.ScheduleId, snapshot.ScheduleId);
        Assert.Equal(AirframeInspectionStatus.InspectionDue, snapshot.InspectionStatus);
        Assert.Equal(1, snapshot.TotalTrackedLandingCycles);
        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityEvidenceRequired,
            snapshot.VerifiedScheduleCandidates.Single().Status);
        ClearPool();

        AirframeMaintenanceSnapshot recovered = Assert.IsType<AirframeMaintenanceSnapshot>(
            (await History().ReadAsync(new(airframe.Airframe.AirframeId))).Snapshot);
        Assert.Equal(snapshot.ServiceState, recovered.ServiceState);
        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityEvidenceRequired,
            recovered.VerifiedScheduleCandidates.Single().Status);
    }

    [Fact]
    public async Task UnsupportedAircraftHasNoVerifiedCandidateAndRetainsFallback()
    {
        var airframe = new Airframe(new(Guid.NewGuid()), "msfs-title:Unseeded Aircraft", Epoch.AddDays(-1));
        AirframeStoreRecord created = await Store().CreateAsync(airframe,
            new AirframeCondition(0, AirframeDamageState.None), Epoch);
        AirframeServiceState service = await StateAsync(airframe.AirframeId);

        Assert.Equal(LightAircraftRoutineInspectionV1.ScheduleId, service.ScheduleId);
        Assert.Equal(TimeSpan.FromHours(50), service.NextInspectionDueAtTrackedAirborneTime);
        Assert.Empty(AirframeMaintenanceScheduleCatalog.AssessCandidates(created.Airframe, service));
    }
}
