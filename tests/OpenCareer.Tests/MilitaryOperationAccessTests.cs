using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryOperationAccessTests
{
    [Fact]
    public void FastJetOperationRequiresAuthorizationQualificationAndCapability()
    {
        var plan = CreatePlan(MilitaryOperationKind.Intercept);
        var aircraft = CreateAircraft(
            AircraftCapability.Military | AircraftCapability.Fighter,
            AircraftAccess.Military);
        var authorization = new MilitaryAuthorizationProfile(
            MilitaryQualification.ServiceAuthorization,
            ServiceTrust: 0.90);

        var eligibility = MilitaryOperationAccessPolicy.Evaluate(
            plan,
            authorization,
            aircraft);

        Assert.False(eligibility.IsEligible);
        Assert.Contains(
            eligibility.Reasons,
            reason => reason.Contains("qualifications", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QualifiedFastJetOperationIsEligible()
    {
        var plan = CreatePlan(MilitaryOperationKind.Intercept);
        var aircraft = CreateAircraft(
            AircraftCapability.Military | AircraftCapability.Fighter,
            AircraftAccess.Military);
        var authorization = new MilitaryAuthorizationProfile(
            MilitaryQualification.ServiceAuthorization | MilitaryQualification.FastJet,
            ServiceTrust: 0.90);

        var eligibility = MilitaryOperationAccessPolicy.Evaluate(
            plan,
            authorization,
            aircraft);

        Assert.True(eligibility.IsEligible);
        Assert.Empty(eligibility.Reasons);
    }

    [Fact]
    public void InstalledCivilianAircraftCannotBypassMilitaryAccess()
    {
        var plan = CreatePlan(MilitaryOperationKind.Transport);
        var aircraft = CreateAircraft(
            AircraftCapability.Military | AircraftCapability.Cargo,
            AircraftAccess.Civilian);
        var authorization = new MilitaryAuthorizationProfile(
            MilitaryQualification.ServiceAuthorization | MilitaryQualification.Mobility,
            ServiceTrust: 0.90);

        var eligibility = MilitaryOperationAccessPolicy.Evaluate(
            plan,
            authorization,
            aircraft);

        Assert.False(eligibility.IsEligible);
        Assert.Contains(
            eligibility.Reasons,
            reason => reason.Contains("not authorized for military service", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MobilityOperationUsesCapabilityProfileNotAircraftName()
    {
        var plan = CreatePlan(MilitaryOperationKind.Transport);
        var aircraft = CreateAircraft(
            AircraftCapability.Military | AircraftCapability.Cargo | AircraftCapability.StrategicTransport,
            AircraftAccess.Military,
            displayName: "Community Add-on Heavy Transport");
        var authorization = new MilitaryAuthorizationProfile(
            MilitaryQualification.ServiceAuthorization | MilitaryQualification.Mobility,
            ServiceTrust: 0.90);

        var eligibility = MilitaryOperationAccessPolicy.Evaluate(
            plan,
            authorization,
            aircraft);

        Assert.True(eligibility.IsEligible);
    }

    private static MilitaryOperationPlan CreatePlan(MilitaryOperationKind kind)
    {
        var sideSpecific = MilitaryOperationPlan.IsSideSpecific(kind);

        return new MilitaryOperationPlan(
            Guid.NewGuid(),
            kind,
            "KRME",
            "KRME",
            "AREA-ALPHA",
            MilitaryOperationRequirementsCatalog.For(kind),
            MinimumOnStationSeconds: 60,
            RequiresObjectiveAction: sideSpecific,
            CampaignId: sideSpecific ? "campaign-1" : null,
            SupportedSideId: sideSpecific ? "side-a" : null);
    }

    private static AircraftCapabilityProfile CreateAircraft(
        AircraftCapability capabilities,
        AircraftAccess access,
        string displayName = "Test Aircraft") =>
        new(
            "test-aircraft",
            displayName,
            capabilities,
            access,
            MaximumPayloadPounds: 20_000,
            MaximumRangeNauticalMiles: 2_000,
            TypicalCruiseKnots: 450,
            Seats: 4,
            EngineCount: 2,
            IfrCapable: true,
            Pressurized: true,
            RetractableGear: true);
}
