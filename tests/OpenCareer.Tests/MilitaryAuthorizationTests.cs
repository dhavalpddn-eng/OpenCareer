using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryAuthorizationTests
{
    [Fact]
    public void InstalledMilitaryAircraftDoesNotGrantMissionAccessByItself()
    {
        var career = QualifiedCareer(
            MilitaryQualification.CloseAirSupport);

        var result = MilitaryAuthorizationPolicy.Evaluate(
            career,
            Fighter(),
            aircraftAssignedForOperation: false,
            SupportRequestType.CloseAirSupport,
            PlayerCombatState.Undamaged);

        Assert.False(result.Eligible);
        Assert.Contains("assigned", result.BlockingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssignmentDoesNotBypassMissingQualification()
    {
        var career = new MilitaryCareerState(
            MilitaryAffiliation.Reserve,
            MilitaryQualification.MilitaryFlight,
            Trust: 0.55,
            SuccessfulOperations: 3,
            FailedOperations: 0);

        var result = MilitaryAuthorizationPolicy.Evaluate(
            career,
            Fighter(),
            aircraftAssignedForOperation: true,
            SupportRequestType.CloseAirSupport,
            PlayerCombatState.Undamaged);

        Assert.False(result.Eligible);
        Assert.Equal(
            MilitaryQualification.CloseAirSupport,
            result.RequiredQualification);
    }

    [Fact]
    public void QualifiedAssignedFighterCanAcceptCloseAirSupport()
    {
        var result = MilitaryAuthorizationPolicy.Evaluate(
            QualifiedCareer(MilitaryQualification.CloseAirSupport),
            Fighter(),
            aircraftAssignedForOperation: true,
            SupportRequestType.CloseAirSupport,
            PlayerCombatState.Undamaged);

        Assert.True(result.Eligible);
        Assert.Null(result.BlockingReason);
    }

    [Fact]
    public void CivilianAccessFlagCannotMasqueradeAsMilitaryAssignment()
    {
        var aircraft = Fighter() with
        {
            Access = AircraftAccess.Civilian
        };

        var result = MilitaryAuthorizationPolicy.Evaluate(
            QualifiedCareer(MilitaryQualification.CloseAirSupport),
            aircraft,
            aircraftAssignedForOperation: true,
            SupportRequestType.CloseAirSupport,
            PlayerCombatState.Undamaged);

        Assert.False(result.Eligible);
        Assert.Contains("military access", result.BlockingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogisticsQualificationRequiresCapableMilitaryAircraft()
    {
        var career = QualifiedCareer(
            MilitaryQualification.Logistics);

        var fighterResult = MilitaryAuthorizationPolicy.Evaluate(
            career,
            Fighter(),
            aircraftAssignedForOperation: true,
            SupportRequestType.Logistics,
            PlayerCombatState.Undamaged);

        var transportResult = MilitaryAuthorizationPolicy.Evaluate(
            career,
            Transport(),
            aircraftAssignedForOperation: true,
            SupportRequestType.Logistics,
            PlayerCombatState.Undamaged);

        Assert.False(fighterResult.Eligible);
        Assert.True(transportResult.Eligible);
    }

    [Fact]
    public void MissionKillBlocksCombatOperationEvenWhenOtherwiseQualified()
    {
        var damaged = new PlayerCombatState(
            AirframeDamage: 0.80,
            PropulsionDamage: 0.20,
            SystemsDamage: 0.10);

        var result = MilitaryAuthorizationPolicy.Evaluate(
            QualifiedCareer(MilitaryQualification.Suppression),
            Fighter(),
            aircraftAssignedForOperation: true,
            SupportRequestType.Suppression,
            damaged);

        Assert.False(result.Eligible);
        Assert.Contains("damage", result.BlockingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpecializedQualificationRequiresMilitaryFlightPrerequisite()
    {
        var career = new MilitaryCareerState(
            MilitaryAffiliation.Reserve,
            MilitaryQualification.None,
            Trust: 0.40,
            SuccessfulOperations: 0,
            FailedOperations: 0);

        Assert.Throws<InvalidOperationException>(
            () => MilitaryCareerProgression.GrantQualification(
                career,
                MilitaryQualification.CloseAirSupport));

        var withFlight = MilitaryCareerProgression.GrantQualification(
            career,
            MilitaryQualification.MilitaryFlight);

        var withCas = MilitaryCareerProgression.GrantQualification(
            withFlight,
            MilitaryQualification.CloseAirSupport);

        Assert.True(withCas.Has(MilitaryQualification.MilitaryFlight));
        Assert.True(withCas.Has(MilitaryQualification.CloseAirSupport));
    }

    [Fact]
    public void OperationResultsAdjustTrustWithinBounds()
    {
        var career = QualifiedCareer(
            MilitaryQualification.Patrol) with
        {
            Trust = 0.99
        };

        var success = MilitaryCareerProgression.RecordOperationResult(
            career,
            success: true,
            SupportUrgency.Immediate);

        Assert.Equal(1, success.Trust);
        Assert.Equal(career.SuccessfulOperations + 1, success.SuccessfulOperations);

        var failed = success;
        for (var index = 0; index < 40; index++)
        {
            failed = MilitaryCareerProgression.RecordOperationResult(
                failed,
                success: false,
                SupportUrgency.Immediate);
        }

        Assert.Equal(0, failed.Trust);
        Assert.Equal(success.FailedOperations + 40, failed.FailedOperations);
    }

    private static MilitaryCareerState QualifiedCareer(
        MilitaryQualification qualification) =>
        new(
            MilitaryAffiliation.Reserve,
            MilitaryQualification.MilitaryFlight | qualification,
            Trust: 0.60,
            SuccessfulOperations: 5,
            FailedOperations: 1);

    private static AircraftCapabilityProfile Fighter() =>
        new(
            AircraftId: "test-fighter",
            DisplayName: "Test Fighter",
            Capabilities:
                AircraftCapability.Military
                | AircraftCapability.Fighter
                | AircraftCapability.Supersonic,
            Access: AircraftAccess.Military,
            MaximumPayloadPounds: 18_000,
            MaximumRangeNauticalMiles: 1_500,
            TypicalCruiseKnots: 480,
            Seats: 1,
            EngineCount: 2,
            IfrCapable: true,
            Pressurized: true,
            RetractableGear: true);

    private static AircraftCapabilityProfile Transport() =>
        new(
            AircraftId: "test-transport",
            DisplayName: "Test Transport",
            Capabilities:
                AircraftCapability.Military
                | AircraftCapability.Cargo
                | AircraftCapability.StrategicTransport,
            Access: AircraftAccess.Military,
            MaximumPayloadPounds: 80_000,
            MaximumRangeNauticalMiles: 3_500,
            TypicalCruiseKnots: 430,
            Seats: 20,
            EngineCount: 4,
            IfrCapable: true,
            Pressurized: true,
            RetractableGear: true);
}
