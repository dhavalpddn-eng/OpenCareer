using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryMissionEngineTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AirSupportMissionRequiresObjectiveAndRecoveryBeforeCompletion()
    {
        var plan = CreateAirSupportPlan();
        var authorization = new MilitaryAuthorizationProfile(
            MilitaryQualification.ServiceAuthorization | MilitaryQualification.FastJet,
            ServiceTrust: 0.90);
        var aircraft = CreateFighter();
        var eligibility = MilitaryOperationAccessPolicy.Evaluate(plan, authorization, aircraft);

        var progress = MilitaryMissionProgress.Briefed(plan, Epoch)
            .Accept(plan, eligibility, Epoch.AddSeconds(1));

        progress = Advance(
            plan,
            progress,
            Epoch.AddSeconds(2),
            stable: true,
            atOrigin: true);
        Assert.Equal(MilitaryOperationPhase.Preflight, progress.Phase);

        progress = Advance(
            plan,
            progress,
            Epoch.AddSeconds(10),
            airborne: true);
        Assert.Equal(MilitaryOperationPhase.EnRoute, progress.Phase);

        progress = Advance(
            plan,
            progress,
            Epoch.AddSeconds(40),
            airborne: true,
            inObjectiveArea: true,
            deltaSeconds: 30);
        Assert.Equal(MilitaryOperationPhase.OnStation, progress.Phase);
        Assert.Equal(30, progress.OnStationSeconds);

        progress = Advance(
            plan,
            progress,
            Epoch.AddSeconds(70),
            airborne: true,
            inObjectiveArea: true,
            objectiveActionVerified: true,
            deltaSeconds: 30);
        Assert.Equal(MilitaryOperationPhase.Objective, progress.Phase);

        progress = Advance(
            plan,
            progress,
            Epoch.AddSeconds(71),
            airborne: true);
        Assert.Equal(MilitaryOperationPhase.Egress, progress.Phase);

        progress = Advance(
            plan,
            progress,
            Epoch.AddMinutes(8),
            atRecoveryAirfield: true);
        Assert.Equal(MilitaryOperationPhase.Recovery, progress.Phase);

        progress = Advance(
            plan,
            progress,
            Epoch.AddMinutes(8).AddSeconds(30),
            atRecoveryAirfield: true,
            parkedAndSecured: true);
        Assert.Equal(MilitaryOperationPhase.Complete, progress.Phase);
    }

    [Fact]
    public void LandingAloneCannotCompleteOperation()
    {
        var plan = CreateAirSupportPlan();
        var progress = MilitaryMissionProgress.Briefed(plan, Epoch) with
        {
            Phase = MilitaryOperationPhase.EnRoute,
            UpdatedAt = Epoch.AddMinutes(1)
        };

        progress = Advance(
            plan,
            progress,
            Epoch.AddMinutes(2),
            atRecoveryAirfield: true,
            parkedAndSecured: true);

        Assert.Equal(MilitaryOperationPhase.EnRoute, progress.Phase);
    }

    [Fact]
    public void AbortIsTerminal()
    {
        var plan = CreateAirSupportPlan();
        var progress = MilitaryMissionProgress.Briefed(plan, Epoch) with
        {
            Phase = MilitaryOperationPhase.EnRoute,
            UpdatedAt = Epoch.AddMinutes(1)
        };

        progress = MilitaryMissionEngine.Advance(
            plan,
            progress,
            new MilitaryMissionEvidence(
                Epoch.AddMinutes(2),
                HasStableTelemetry: true,
                AtOrigin: false,
                Airborne: true,
                InObjectiveArea: false,
                ObjectiveActionVerified: false,
                AtRecoveryAirfield: false,
                ParkedAndSecured: false,
                AbortRequested: true));

        Assert.Equal(MilitaryOperationPhase.Aborted, progress.Phase);

        var unchanged = Advance(
            plan,
            progress,
            Epoch.AddMinutes(3),
            atRecoveryAirfield: true,
            parkedAndSecured: true);

        Assert.Equal(progress, unchanged);
    }

    private static MilitaryMissionProgress Advance(
        MilitaryOperationPlan plan,
        MilitaryMissionProgress progress,
        DateTimeOffset time,
        bool stable = true,
        bool atOrigin = false,
        bool airborne = false,
        bool inObjectiveArea = false,
        bool objectiveActionVerified = false,
        bool atRecoveryAirfield = false,
        bool parkedAndSecured = false,
        double deltaSeconds = 0) =>
        MilitaryMissionEngine.Advance(
            plan,
            progress,
            new MilitaryMissionEvidence(
                time,
                stable,
                atOrigin,
                airborne,
                inObjectiveArea,
                objectiveActionVerified,
                atRecoveryAirfield,
                parkedAndSecured,
                DeltaSeconds: deltaSeconds));

    private static MilitaryOperationPlan CreateAirSupportPlan() =>
        new(
            Guid.NewGuid(),
            MilitaryOperationKind.AirSupport,
            "KRME",
            "KRME",
            "SIM-AREA-1",
            MilitaryOperationRequirementsCatalog.For(MilitaryOperationKind.AirSupport),
            MinimumOnStationSeconds: 60,
            RequiresObjectiveAction: true,
            CampaignId: "campaign-1",
            SupportedSideId: "side-a");

    private static AircraftCapabilityProfile CreateFighter() =>
        new(
            "test-fighter",
            "Test Fighter",
            AircraftCapability.Military | AircraftCapability.Fighter | AircraftCapability.Supersonic,
            AircraftAccess.Military,
            MaximumPayloadPounds: 5_000,
            MaximumRangeNauticalMiles: 1_500,
            TypicalCruiseKnots: 500,
            Seats: 1,
            EngineCount: 2,
            IfrCapable: true,
            Pressurized: true,
            RetractableGear: true);
}
