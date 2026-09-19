using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class ConflictSystemTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GroundPressureGeneratesCasAndSuppressionRequestsDeterministically()
    {
        var state = CreateWorld();

        state = ConflictWorldEngine.Advance(state, Epoch.AddMinutes(30));

        Assert.Equal(2, state.SupportRequests.Length);

        var cas = state.SupportRequests.Single(
            request => request.Type == SupportRequestType.CloseAirSupport);

        Assert.Equal(FriendlyInfantryId, cas.RequestingUnitId);
        Assert.Equal(HostileArmorId, cas.TargetUnitId);
        Assert.Equal(SupportUrgency.Immediate, cas.Urgency);

        var suppression = state.SupportRequests.Single(
            request => request.Type == SupportRequestType.Suppression);

        Assert.Equal(HostileAirDefenseId, suppression.TargetUnitId);

        var replay = ConflictWorldEngine.Advance(CreateWorld(), Epoch.AddMinutes(30));
        Assert.Equal(state.SupportRequests, replay.SupportRequests);
    }

    [Fact]
    public void SameSupportNeedDoesNotDuplicateOpenRequests()
    {
        var state = ConflictWorldEngine.Advance(CreateWorld(), Epoch.AddMinutes(30));

        state = ConflictWorldEngine.Advance(state, Epoch.AddMinutes(40));

        Assert.Equal(2, state.SupportRequests.Length);
        Assert.Equal(
            2,
            state.SupportRequests.Select(request => request.RequestId).Distinct().Count());
    }

    [Fact]
    public void AuthorizedPlayerActionChangesBattleStateExactlyOnce()
    {
        var state = ConflictWorldEngine.Advance(CreateWorld(), Epoch.AddMinutes(30));
        var service = new ConflictOperationsService();
        var request = state.SupportRequests.Single(
            item => item.Type == SupportRequestType.CloseAirSupport);
        var missionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var mission = service.AcceptSupportRequest(state, request.RequestId, missionId, Epoch.AddMinutes(31));

        var telemetryResult = service.UpdateMission(
            mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, altitudeAgl: 5_000, speed: 260),
            Epoch.AddMinutes(32));

        Assert.Equal(AirSupportMissionStage.ActionAuthorized, telemetryResult.Mission.Stage);
        Assert.True(telemetryResult.ActionWindowSatisfied);

        var before = state.Units.Single(unit => unit.UnitId == HostileArmorId);

        var first = service.ExecuteAuthorizedAction(
            state,
            telemetryResult.Mission,
            PlayerActionKind.PrecisionAttack,
            "action-001",
            geometryQuality: 0.90,
            targetConfidence: 0.95,
            Epoch.AddMinutes(32).AddSeconds(5));

        var after = first.World.Units.Single(unit => unit.UnitId == HostileArmorId);

        Assert.Equal(PlayerActionOutcome.Applied, first.Action.Outcome);
        Assert.True(after.Strength < before.Strength);
        Assert.Equal(AirSupportMissionStage.Egress, first.Mission.Stage);

        var duplicate = ConflictActionResolver.Apply(
            first.World,
            new PlayerActionRequest(
                "action-001",
                missionId,
                HostileArmorId,
                PlayerActionKind.PrecisionAttack,
                0.90,
                0.95));

        Assert.Equal(PlayerActionOutcome.DuplicateIgnored, duplicate.Result.Outcome);
        Assert.Equal(after, duplicate.State.Units.Single(unit => unit.UnitId == HostileArmorId));
    }

    [Fact]
    public void SuppressingAirDefenseReducesLinkedThreatSeverity()
    {
        var state = CreateWorld();
        var before = state.Threats.Single();

        var result = ConflictActionResolver.Apply(
            state,
            new PlayerActionRequest(
                "suppress-001",
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                HostileAirDefenseId,
                PlayerActionKind.Suppression,
                GeometryQuality: 1,
                TargetConfidence: 1));

        var after = result.State.Threats.Single();

        Assert.Equal(PlayerActionOutcome.Applied, result.Result.Outcome);
        Assert.True(after.Severity < before.Severity);
    }

    [Fact]
    public void ReconnaissanceRaisesIntelligenceWithoutDamagingTarget()
    {
        var state = CreateWorld();
        var targetBefore = state.Units.Single(unit => unit.UnitId == HostileArmorId);
        var sectorBefore = state.Sectors.Single();

        var result = ConflictActionResolver.Apply(
            state,
            new PlayerActionRequest(
                "recon-001",
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                HostileArmorId,
                PlayerActionKind.Reconnaissance,
                GeometryQuality: 0.8,
                TargetConfidence: 0.9));

        var targetAfter = result.State.Units.Single(unit => unit.UnitId == HostileArmorId);
        var sectorAfter = result.State.Sectors.Single();

        Assert.Equal(targetBefore.Strength, targetAfter.Strength);
        Assert.Equal(targetBefore.Readiness, targetAfter.Readiness);
        Assert.True(sectorAfter.IntelligenceConfidence > sectorBefore.IntelligenceConfidence);
    }

    [Fact]
    public void ThreatExposureUsesRealPlayerTelemetryButNotMsfsCombatEvents()
    {
        var state = CreateWorld();
        var threat = state.Threats.Single();

        var inside = ThreatExposureEvaluator.Evaluate(
            state,
            TelemetryAt(threat.Center, 5_000, 250));

        Assert.Single(inside);
        Assert.Equal(threat.ThreatId, inside[0].ThreatId);

        var paused = ThreatExposureEvaluator.Evaluate(
            state,
            TelemetryAt(threat.Center, 5_000, 250) with { Paused = true });

        Assert.Empty(paused);
    }

    [Fact]
    public void PausedOrSlewTelemetryCannotAuthorizeConflictAction()
    {
        var state = ConflictWorldEngine.Advance(CreateWorld(), Epoch.AddMinutes(30));
        var request = state.SupportRequests.Single(
            item => item.Type == SupportRequestType.CloseAirSupport);
        var mission = AirSupportMission.Accept(
            request,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Epoch.AddMinutes(31));

        var paused = AirSupportMissionEngine.Update(
            mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250) with { Paused = true },
            Epoch.AddMinutes(32));

        Assert.False(paused.ActionWindowSatisfied);
        Assert.NotEqual(AirSupportMissionStage.ActionAuthorized, paused.Mission.Stage);

        var slew = AirSupportMissionEngine.Update(
            mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250) with { SlewActive = true },
            Epoch.AddMinutes(32));

        Assert.False(slew.ActionWindowSatisfied);
        Assert.NotEqual(AirSupportMissionStage.ActionAuthorized, slew.Mission.Stage);
    }

    [Fact]
    public void ObjectiveCompletesOnlyAfterAppliedActionAndExit()
    {
        var state = ConflictWorldEngine.Advance(CreateWorld(), Epoch.AddMinutes(30));
        var request = state.SupportRequests.Single(
            item => item.Type == SupportRequestType.CloseAirSupport);
        var mission = AirSupportMission.Accept(
            request,
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Epoch.AddMinutes(31));

        var onStation = AirSupportMissionEngine.Update(
            mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250),
            Epoch.AddMinutes(32));

        var egress = AirSupportMissionEngine.MarkActionApplied(
            onStation.Mission,
            Epoch.AddMinutes(32).AddSeconds(5));

        var stillInside = AirSupportMissionEngine.Update(
            egress,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250),
            Epoch.AddMinutes(33));

        Assert.Equal(AirSupportMissionStage.Egress, stillInside.Mission.Stage);

        var outside = AirSupportMissionEngine.Update(
            stillInside.Mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(new GeoPoint(35.5, -96.0), 6_000, 280),
            Epoch.AddMinutes(40));

        Assert.Equal(AirSupportMissionStage.ObjectiveComplete, outside.Mission.Stage);
    }

    [Fact]
    public void ThreatResolutionIsDeterministicAndIdempotent()
    {
        var state = CreateWorld();
        var request = new ThreatEngagementRequest(
            "threat-event-001",
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            HostileThreatId,
            TimeSpan.FromSeconds(35),
            DefensiveResponseQuality: 0.25);

        var first = ThreatEngagementResolver.Resolve(
            state,
            request,
            PlayerCombatState.Undamaged);

        var replay = ThreatEngagementResolver.Resolve(
            CreateWorld(),
            request,
            PlayerCombatState.Undamaged);

        Assert.Equal(first.Result.Outcome, replay.Result.Outcome);
        Assert.Equal(first.Result.DamageLevel, replay.Result.DamageLevel);
        Assert.Equal(first.Result.PlayerState, replay.Result.PlayerState);

        var duplicate = ThreatEngagementResolver.Resolve(
            first.State,
            request,
            first.Result.PlayerState);

        Assert.Equal(ThreatEngagementOutcome.DuplicateIgnored, duplicate.Result.Outcome);
        Assert.Equal(first.Result.PlayerState, duplicate.Result.PlayerState);
    }

    [Fact]
    public void GroundBattleControlMovesTowardStrongerSide()
    {
        var state = CreateWorld();
        var before = state.Sectors.Single().FriendlyControl;

        var advanced = ConflictWorldEngine.Advance(state, Epoch.AddHours(2));
        var after = advanced.Sectors.Single().FriendlyControl;

        Assert.True(after < before);
    }

    private static ConflictWorldState CreateWorld()
    {
        var friendly = new GroundUnitState(
            FriendlyInfantryId,
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.000, -97.000),
            Strength: 0.58,
            Readiness: 0.72,
            Pressure: 0,
            IsMobile: true);

        var hostileArmor = new GroundUnitState(
            HostileArmorId,
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.040, -96.950),
            Strength: 0.92,
            Readiness: 0.92,
            Pressure: 0,
            IsMobile: true);

        var hostileAirDefense = new GroundUnitState(
            HostileAirDefenseId,
            ConflictSide.Hostile,
            GroundUnitRole.AirDefense,
            new GeoPoint(35.120, -96.900),
            Strength: 0.75,
            Readiness: 0.90,
            Pressure: 0,
            IsMobile: false);

        var sector = new ConflictSectorState(
            "EAST-01",
            new GeoPoint(35.05, -96.96),
            FriendlyControl: 0.48,
            IntelligenceConfidence: 0.35);

        var threat = new ThreatState(
            HostileThreatId,
            HostileAirDefenseId,
            ConflictSide.Hostile,
            AirThreatType.AirDefense,
            hostileAirDefense.Position,
            RadiusNauticalMiles: 16,
            Severity: hostileAirDefense.Strength * hostileAirDefense.Readiness,
            Active: true);

        return ConflictWorldState.Create(
            "FICTIONAL-EAST",
            theaterSeed: 0xC0FFEEUL,
            Epoch,
            new[] { friendly, hostileArmor, hostileAirDefense },
            new[] { sector },
            new[] { threat });
    }

    private static AircraftTelemetrySnapshot TelemetryAt(
        GeoPoint point,
        double altitudeAgl,
        double speed) =>
        new(
            Epoch,
            point.LatitudeDegrees,
            point.LongitudeDegrees,
            AltitudeMslFeet: altitudeAgl + 1_000,
            AltitudeAglFeet: altitudeAgl,
            IndicatedAirspeedKnots: speed,
            GroundSpeedKnots: speed,
            VerticalSpeedFeetPerMinute: 0,
            HeadingDegrees: 90,
            PitchDegrees: 0,
            BankDegrees: 0,
            NormalAccelerationG: 1,
            OnGround: false,
            ParkingBrakeSet: false,
            EnginesRunning: 2,
            FuelTotalPounds: 5_000,
            PayloadPounds: 0,
            FlapsPositionPercent: 0,
            GearDown: false,
            Paused: false,
            SlewActive: false);

    private static readonly Guid FriendlyInfantryId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly Guid HostileArmorId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly Guid HostileAirDefenseId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    private static readonly Guid HostileThreatId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");
}
