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
        var state = ConflictWorldEngine.Advance(
            CreateWorld(),
            Epoch.AddMinutes(30));

        var active = state.SupportRequests
            .Where(request => request.IsActive)
            .ToArray();

        Assert.Equal(2, active.Length);

        var cas = active.Single(
            request => request.Type == SupportRequestType.CloseAirSupport);

        Assert.Equal(FriendlyInfantryId, cas.RequestingUnitId);
        Assert.Equal(HostileArmorId, cas.TargetUnitId);
        Assert.Equal(SupportUrgency.Immediate, cas.Urgency);
        Assert.Equal(SupportRequestStatus.Open, cas.Status);

        var suppression = active.Single(
            request => request.Type == SupportRequestType.Suppression);

        Assert.Equal(HostileAirDefenseId, suppression.TargetUnitId);

        var replay = ConflictWorldEngine.Advance(
            CreateWorld(),
            Epoch.AddMinutes(30));

        Assert.Equal(state.SupportRequests, replay.SupportRequests);
    }

    [Fact]
    public void SameSupportNeedDoesNotDuplicateOpenRequests()
    {
        var state = ConflictWorldEngine.Advance(
            CreateWorld(),
            Epoch.AddMinutes(30));

        state = ConflictWorldEngine.Advance(
            state,
            Epoch.AddMinutes(40));

        var active = state.SupportRequests
            .Where(request => request.IsActive)
            .ToArray();

        Assert.Equal(2, active.Length);
        Assert.Equal(
            2,
            active.Select(request => request.RequestId).Distinct().Count());
    }

    [Fact]
    public void AcceptingSupportRequestReservesItAndBlocksSecondMission()
    {
        var state = ConflictWorldEngine.Advance(
            CreateWorld(),
            Epoch.AddMinutes(30));

        var service = new ConflictOperationsService();
        var request = state.SupportRequests.Single(
            item => item.Type == SupportRequestType.CloseAirSupport);

        var firstMissionId =
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var accepted = service.AcceptCombatSupportRequest(
            state,
            request.RequestId,
            firstMissionId,
            Epoch.AddMinutes(31));

        var reserved = accepted.World.SupportRequests.Single(
            item => item.RequestId == request.RequestId);

        Assert.Equal(SupportRequestStatus.Reserved, reserved.Status);
        Assert.Equal(firstMissionId, reserved.ReservedMissionId);

        Assert.Throws<InvalidOperationException>(
            () => service.AcceptCombatSupportRequest(
                accepted.World,
                request.RequestId,
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab"),
                Epoch.AddMinutes(32)));
    }

    [Fact]
    public void AuthorizedPlayerActionChangesBattleStateExactlyOnce()
    {
        var state = ConflictWorldEngine.Advance(
            CreateWorld(),
            Epoch.AddMinutes(30));

        var service = new ConflictOperationsService();
        var request = state.SupportRequests.Single(
            item => item.Type == SupportRequestType.CloseAirSupport);

        var accepted = service.AcceptCombatSupportRequest(
            state,
            request.RequestId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Epoch.AddMinutes(31));

        var telemetryResult = service.UpdateMission(
            accepted.Mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, altitudeAgl: 5_000, speed: 260),
            Epoch.AddMinutes(32));

        Assert.Equal(
            AirSupportMissionStage.ActionAuthorized,
            telemetryResult.Mission.Stage);
        Assert.True(telemetryResult.ActionWindowSatisfied);

        var before = accepted.World.Units.Single(
            unit => unit.UnitId == HostileArmorId);

        var first = service.ExecuteAuthorizedAction(
            accepted.World,
            telemetryResult.Mission,
            PlayerActionKind.PrecisionAttack,
            "action-001",
            geometryQuality: 0.90,
            targetConfidence: 0.95,
            Epoch.AddMinutes(32).AddSeconds(5));

        var after = first.World.Units.Single(
            unit => unit.UnitId == HostileArmorId);

        Assert.Equal(PlayerActionOutcome.Applied, first.Action.Outcome);
        Assert.True(after.Strength < before.Strength);
        Assert.Equal(AirSupportMissionStage.Egress, first.Mission.Stage);

        var duplicate = ConflictActionResolver.Apply(
            first.World,
            new PlayerActionRequest(
                "action-001",
                first.Mission.MissionId,
                HostileArmorId,
                PlayerActionKind.PrecisionAttack,
                0.90,
                0.95));

        Assert.Equal(
            PlayerActionOutcome.DuplicateIgnored,
            duplicate.Result.Outcome);
        Assert.Equal(
            after,
            duplicate.State.Units.Single(
                unit => unit.UnitId == HostileArmorId));
    }

    [Fact]
    public void CombatMissionClosesRequestOnlyAfterActionAndEgress()
    {
        var state = ConflictWorldEngine.Advance(
            CreateWorld(),
            Epoch.AddMinutes(30));

        var service = new ConflictOperationsService();
        var request = state.SupportRequests.Single(
            item => item.Type == SupportRequestType.CloseAirSupport);

        var accepted = service.AcceptCombatSupportRequest(
            state,
            request.RequestId,
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Epoch.AddMinutes(31));

        var onStation = service.UpdateMission(
            accepted.Mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250),
            Epoch.AddMinutes(32));

        var action = service.ExecuteAuthorizedAction(
            accepted.World,
            onStation.Mission,
            PlayerActionKind.PrecisionAttack,
            "action-egress-001",
            0.90,
            0.90,
            Epoch.AddMinutes(32).AddSeconds(5));

        var stillInside = service.UpdateMission(
            action.Mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250),
            Epoch.AddMinutes(33));

        Assert.Equal(AirSupportMissionStage.Egress, stillInside.Mission.Stage);
        Assert.Equal(
            SupportRequestStatus.Reserved,
            action.World.SupportRequests.Single(
                item => item.RequestId == request.RequestId).Status);

        var outside = service.UpdateMission(
            stillInside.Mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(new GeoPoint(35.5, -96.0), 6_000, 280),
            Epoch.AddMinutes(40));

        Assert.Equal(
            AirSupportMissionStage.ObjectiveComplete,
            outside.Mission.Stage);

        var completedWorld = service.CompleteCombatSupportMission(
            action.World,
            outside.Mission,
            Epoch.AddMinutes(40));

        Assert.Equal(
            SupportRequestStatus.Completed,
            completedWorld.SupportRequests.Single(
                item => item.RequestId == request.RequestId).Status);
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
    public void ReconActionRaisesIntelligenceWithoutDamagingTarget()
    {
        var state = CreateWorld();
        var targetBefore = state.Units.Single(
            unit => unit.UnitId == HostileArmorId);
        var sectorBefore = state.Sectors.Single();

        var result = ConflictActionResolver.Apply(
            state,
            new PlayerActionRequest(
                "recon-action-001",
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                HostileArmorId,
                PlayerActionKind.Reconnaissance,
                GeometryQuality: 0.8,
                TargetConfidence: 0.9));

        var targetAfter = result.State.Units.Single(
            unit => unit.UnitId == HostileArmorId);
        var sectorAfter = result.State.Sectors.Single();

        Assert.Equal(targetBefore.Strength, targetAfter.Strength);
        Assert.Equal(targetBefore.Readiness, targetAfter.Readiness);
        Assert.True(
            sectorAfter.IntelligenceConfidence
            > sectorBefore.IntelligenceConfidence);
    }

    [Fact]
    public void LowIntelligenceGeneratesReconRequestAndMissionImprovesSectorIntel()
    {
        var world = CreateWorld();
        world = world with
        {
            Sectors = new[]
            {
                world.Sectors[0] with { IntelligenceConfidence = 0.10 }
            }
        };

        world = ConflictWorldEngine.Advance(
            world,
            Epoch.AddMinutes(30));

        var request = world.SupportRequests.Single(
            item => item.Type == SupportRequestType.Reconnaissance);

        var service = new ConflictOperationsService();
        var accepted = service.AcceptAreaSupportRequest(
            world,
            request.RequestId,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Epoch.AddMinutes(31));

        var profile = AreaSupportMissionProfile.For(
            SupportRequestType.Reconnaissance) with
        {
            RequiredVerifiedPresence = TimeSpan.FromSeconds(20)
        };

        var sample = TelemetryAt(
            request.TargetPosition,
            altitudeAgl: 6_000,
            speed: 220);

        var first = service.UpdateAreaMission(
            accepted.Mission,
            profile,
            sample,
            Epoch.AddMinutes(31).AddSeconds(1));

        var second = service.UpdateAreaMission(
            first.Mission,
            profile,
            sample,
            Epoch.AddMinutes(31).AddSeconds(11));

        var third = service.UpdateAreaMission(
            second.Mission,
            profile,
            sample,
            Epoch.AddMinutes(31).AddSeconds(21));

        Assert.Equal(
            AreaSupportMissionStage.ObjectiveComplete,
            third.Mission.Stage);

        var before = accepted.World.Sectors.Single().IntelligenceConfidence;

        var completed = service.CompleteAreaSupportMission(
            accepted.World,
            third.Mission,
            Epoch.AddMinutes(31).AddSeconds(21));

        var after = completed.World.Sectors.Single().IntelligenceConfidence;

        Assert.True(after > before);
        Assert.True(completed.Outcome.Applied);
        Assert.Equal(
            SupportRequestStatus.Completed,
            completed.World.SupportRequests.Single(
                item => item.RequestId == request.RequestId).Status);
    }

    [Fact]
    public void LogisticsMissionRequiresLandedStoppedParkingStateAndRestoresReadiness()
    {
        var world = CreateWorld();
        var request = new AirSupportRequest(
            "logistics-authored-001",
            SupportRequestType.Logistics,
            SupportUrgency.Priority,
            FriendlyInfantryId,
            FriendlyInfantryId,
            world.Units.Single(unit => unit.UnitId == FriendlyInfantryId).Position,
            RequiredEffect: 0.20,
            CreatedAt: Epoch,
            ExpiresAt: Epoch.AddHours(1));

        world = world with
        {
            Units = world.Units
                .Select(unit => unit.UnitId == FriendlyInfantryId
                    ? unit with { Readiness = 0.30, Pressure = 0 }
                    : unit)
                .ToArray(),
            SupportRequests = new[] { request }
        };

        ConflictValidation.Validate(world);

        var service = new ConflictOperationsService();
        var accepted = service.AcceptAreaSupportRequest(
            world,
            request.RequestId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Epoch.AddMinutes(5));

        var invalid = service.UpdateAreaMission(
            accepted.Mission,
            AreaSupportMissionProfile.For(SupportRequestType.Logistics),
            TelemetryAt(request.TargetPosition, 50, 20),
            Epoch.AddMinutes(6));

        Assert.NotEqual(
            AreaSupportMissionStage.ObjectiveComplete,
            invalid.Mission.Stage);

        var parked = TelemetryAt(
            request.TargetPosition,
            altitudeAgl: 0,
            speed: 0) with
        {
            OnGround = true,
            ParkingBrakeSet = true
        };

        var completedMission = service.UpdateAreaMission(
            invalid.Mission,
            AreaSupportMissionProfile.For(SupportRequestType.Logistics),
            parked,
            Epoch.AddMinutes(7));

        Assert.Equal(
            AreaSupportMissionStage.ObjectiveComplete,
            completedMission.Mission.Stage);

        var readinessBefore = accepted.World.Units.Single(
            unit => unit.UnitId == FriendlyInfantryId).Readiness;

        var completed = service.CompleteAreaSupportMission(
            accepted.World,
            completedMission.Mission,
            Epoch.AddMinutes(7));

        var readinessAfter = completed.World.Units.Single(
            unit => unit.UnitId == FriendlyInfantryId).Readiness;

        Assert.True(readinessAfter > readinessBefore);
        Assert.Equal(
            SupportRequestStatus.Completed,
            completed.World.SupportRequests.Single(
                item => item.RequestId == request.RequestId).Status);
    }

    [Fact]
    public void ThreatExposureUsesPlayerTelemetryButNotMsfsCombatEvents()
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
        var state = ConflictWorldEngine.Advance(
            CreateWorld(),
            Epoch.AddMinutes(30));

        var service = new ConflictOperationsService();
        var request = state.SupportRequests.Single(
            item => item.Type == SupportRequestType.CloseAirSupport);

        var accepted = service.AcceptCombatSupportRequest(
            state,
            request.RequestId,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Epoch.AddMinutes(31));

        var paused = service.UpdateMission(
            accepted.Mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250) with
            {
                Paused = true
            },
            Epoch.AddMinutes(32));

        Assert.False(paused.ActionWindowSatisfied);
        Assert.NotEqual(
            AirSupportMissionStage.ActionAuthorized,
            paused.Mission.Stage);

        var slew = service.UpdateMission(
            accepted.Mission,
            AirSupportMissionProfile.FixedWingDefault,
            TelemetryAt(request.TargetPosition, 5_000, 250) with
            {
                SlewActive = true
            },
            Epoch.AddMinutes(32));

        Assert.False(slew.ActionWindowSatisfied);
        Assert.NotEqual(
            AirSupportMissionStage.ActionAuthorized,
            slew.Mission.Stage);
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

        Assert.Equal(
            ThreatEngagementOutcome.DuplicateIgnored,
            duplicate.Result.Outcome);
        Assert.Equal(
            first.Result.PlayerState,
            duplicate.Result.PlayerState);
    }

    [Fact]
    public void GroundBattleControlMovesTowardStrongerSide()
    {
        var state = CreateWorld();
        var before = state.Sectors.Single().FriendlyControl;

        var advanced = ConflictWorldEngine.Advance(
            state,
            Epoch.AddHours(2));

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
