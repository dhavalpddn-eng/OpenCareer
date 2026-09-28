using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class AirConflictSystemTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SimulatedAirUnitsMoveDeterministicallyAndLinkedThreatFollows()
    {
        var initial = CreateAirWorld();
        var hostileBefore = initial.AirUnits.Single(
            unit => unit.UnitId == HostileFighterId);

        var advanced = ConflictWorldEngine.Advance(
            initial,
            Epoch.AddMinutes(6));

        var hostileAfter = advanced.AirUnits.Single(
            unit => unit.UnitId == HostileFighterId);

        Assert.NotEqual(hostileBefore.Position, hostileAfter.Position);

        var beforeDistance = ConflictGeometry.DistanceNauticalMiles(
            hostileBefore.Position,
            hostileBefore.Destination);

        var afterDistance = ConflictGeometry.DistanceNauticalMiles(
            hostileAfter.Position,
            hostileAfter.Destination);

        Assert.True(afterDistance < beforeDistance);

        var threat = advanced.Threats.Single(
            item => item.SourceUnitId == HostileFighterId);

        Assert.Equal(hostileAfter.Position, threat.Center);

        var replay = ConflictWorldEngine.Advance(
            CreateAirWorld(),
            Epoch.AddMinutes(6));

        Assert.Equal(advanced.AirUnits, replay.AirUnits);
        Assert.Equal(advanced.Threats, replay.Threats);
    }

    [Fact]
    public void HostileFighterCreatesInterceptAndThreatenedPackageCreatesEscort()
    {
        var advanced = ConflictWorldEngine.Advance(
            CreateAirWorld(),
            Epoch.AddMinutes(2));

        var intercept = advanced.SupportRequests.Single(
            request => request.Type == SupportRequestType.Intercept);

        var escort = advanced.SupportRequests.Single(
            request => request.Type == SupportRequestType.Escort);

        Assert.Equal(HostileFighterId, intercept.TargetUnitId);
        Assert.Equal(FriendlyTransportId, escort.TargetUnitId);
        Assert.True(intercept.IsActive);
        Assert.True(escort.IsActive);
    }

    [Fact]
    public void EscortCompletesAfterVerifiedContinuousProximity()
    {
        var world = ConflictWorldEngine.Advance(
            CreateAirWorld(),
            Epoch.AddMinutes(2));

        var request = world.SupportRequests.Single(
            item => item.Type == SupportRequestType.Escort);

        var service = new ConflictOperationsService();
        var accepted = service.AcceptAirOperationRequest(
            world,
            request.RequestId,
            Guid.Parse("41000000-0000-0000-0000-000000000001"),
            Epoch.AddMinutes(3));

        var profile = AirOperationMissionProfile.For(
            SupportRequestType.Escort) with
        {
            RequiredVerifiedProximity = TimeSpan.FromSeconds(20)
        };

        var target = accepted.World.AirUnits.Single(
            unit => unit.UnitId == FriendlyTransportId);

        var telemetry = TelemetryAt(target.Position);

        var first = service.UpdateAirOperationMission(
            accepted.World,
            accepted.Mission,
            profile,
            telemetry,
            Epoch.AddMinutes(3).AddSeconds(1));

        var second = service.UpdateAirOperationMission(
            accepted.World,
            first.Mission,
            profile,
            telemetry,
            Epoch.AddMinutes(3).AddSeconds(11));

        var third = service.UpdateAirOperationMission(
            accepted.World,
            second.Mission,
            profile,
            telemetry,
            Epoch.AddMinutes(3).AddSeconds(21));

        Assert.Equal(
            AirOperationMissionStage.ObjectiveComplete,
            third.Mission.Stage);

        var completedWorld = service.CompleteAirOperationMission(
            accepted.World,
            third.Mission,
            Epoch.AddMinutes(3).AddSeconds(21));

        Assert.Equal(
            SupportRequestStatus.Completed,
            completedWorld.SupportRequests.Single(
                item => item.RequestId == request.RequestId).Status);
    }

    [Fact]
    public void InterceptRequiresTrackingThenAppliesAbstractEffectAndEgress()
    {
        var world = ConflictWorldEngine.Advance(
            CreateAirWorld(),
            Epoch.AddMinutes(2));

        var request = world.SupportRequests.Single(
            item => item.Type == SupportRequestType.Intercept);

        var service = new ConflictOperationsService();
        var accepted = service.AcceptAirOperationRequest(
            world,
            request.RequestId,
            Guid.Parse("42000000-0000-0000-0000-000000000001"),
            Epoch.AddMinutes(3));

        var profile = AirOperationMissionProfile.For(
            SupportRequestType.Intercept) with
        {
            RequiredVerifiedProximity = TimeSpan.FromSeconds(20)
        };

        var target = accepted.World.AirUnits.Single(
            unit => unit.UnitId == HostileFighterId);

        var telemetry = TelemetryAt(target.Position);

        var first = service.UpdateAirOperationMission(
            accepted.World,
            accepted.Mission,
            profile,
            telemetry,
            Epoch.AddMinutes(3).AddSeconds(1));

        var second = service.UpdateAirOperationMission(
            accepted.World,
            first.Mission,
            profile,
            telemetry,
            Epoch.AddMinutes(3).AddSeconds(11));

        var third = service.UpdateAirOperationMission(
            accepted.World,
            second.Mission,
            profile,
            telemetry,
            Epoch.AddMinutes(3).AddSeconds(21));

        Assert.Equal(
            AirOperationMissionStage.ActionAuthorized,
            third.Mission.Stage);

        var strengthBefore = accepted.World.AirUnits.Single(
            unit => unit.UnitId == HostileFighterId).Strength;

        var action = service.ExecuteInterceptAction(
            accepted.World,
            third.Mission,
            "intercept-action-001",
            InterceptActionKind.Neutralize,
            geometryQuality: 0.90,
            Epoch.AddMinutes(3).AddSeconds(22));

        var strengthAfter = action.World.AirUnits.Single(
            unit => unit.UnitId == HostileFighterId).Strength;

        Assert.True(action.Action.Applied);
        Assert.True(strengthAfter < strengthBefore);
        Assert.Equal(
            AirOperationMissionStage.Egress,
            action.Mission.Stage);

        var farTelemetry = TelemetryAt(
            new GeoPoint(37.0, -99.0));

        var egress = service.UpdateAirOperationMission(
            action.World,
            action.Mission,
            profile,
            farTelemetry,
            Epoch.AddMinutes(5));

        Assert.Equal(
            AirOperationMissionStage.ObjectiveComplete,
            egress.Mission.Stage);

        var completed = service.CompleteAirOperationMission(
            action.World,
            egress.Mission,
            Epoch.AddMinutes(5));

        Assert.Equal(
            SupportRequestStatus.Completed,
            completed.SupportRequests.Single(
                item => item.RequestId == request.RequestId).Status);
    }

    [Fact]
    public void LeavingEscortRadiusResetsContinuousProximityCredit()
    {
        var world = ConflictWorldEngine.Advance(
            CreateAirWorld(),
            Epoch.AddMinutes(2));

        var request = world.SupportRequests.Single(
            item => item.Type == SupportRequestType.Escort);

        var service = new ConflictOperationsService();
        var accepted = service.AcceptAirOperationRequest(
            world,
            request.RequestId,
            Guid.Parse("43000000-0000-0000-0000-000000000001"),
            Epoch.AddMinutes(3));

        var profile = AirOperationMissionProfile.For(
            SupportRequestType.Escort) with
        {
            RequiredVerifiedProximity = TimeSpan.FromSeconds(30)
        };

        var target = accepted.World.AirUnits.Single(
            unit => unit.UnitId == FriendlyTransportId);

        var first = service.UpdateAirOperationMission(
            accepted.World,
            accepted.Mission,
            profile,
            TelemetryAt(target.Position),
            Epoch.AddMinutes(3).AddSeconds(1));

        var second = service.UpdateAirOperationMission(
            accepted.World,
            first.Mission,
            profile,
            TelemetryAt(target.Position),
            Epoch.AddMinutes(3).AddSeconds(11));

        Assert.Equal(TimeSpan.FromSeconds(10), second.Mission.VerifiedProximity);

        var outside = service.UpdateAirOperationMission(
            accepted.World,
            second.Mission,
            profile,
            TelemetryAt(new GeoPoint(37.0, -99.0)),
            Epoch.AddMinutes(3).AddSeconds(21));

        Assert.Equal(TimeSpan.Zero, outside.Mission.VerifiedProximity);
        Assert.Equal(
            AirOperationMissionStage.Rendezvous,
            outside.Mission.Stage);
    }

    private static ConflictWorldState CreateAirWorld()
    {
        var friendlyGround = new GroundUnitState(
            FriendlyGroundId,
            ConflictSide.Friendly,
            GroundUnitRole.Command,
            new GeoPoint(35.20, -97.10),
            Strength: 0.85,
            Readiness: 0.90,
            Pressure: 0,
            IsMobile: false);

        var sector = new ConflictSectorState(
            "AIR-01",
            new GeoPoint(35.40, -97.00),
            FriendlyControl: 0.60,
            IntelligenceConfidence: 0.70);

        var friendlyTransport = new SimulatedAirUnitState(
            FriendlyTransportId,
            ConflictSide.Friendly,
            AirUnitRole.Transport,
            new GeoPoint(35.45, -97.15),
            new GeoPoint(36.40, -95.50),
            AltitudeFeet: 18_000,
            GroundSpeedKnots: 300,
            Strength: 0.95,
            Readiness: 0.90,
            Active: true);

        var hostileFighter = new SimulatedAirUnitState(
            HostileFighterId,
            ConflictSide.Hostile,
            AirUnitRole.Fighter,
            new GeoPoint(35.55, -96.95),
            new GeoPoint(34.80, -97.40),
            AltitudeFeet: 22_000,
            GroundSpeedKnots: 420,
            Strength: 0.85,
            Readiness: 0.90,
            Active: true);

        var interceptorThreat = new ThreatState(
            HostileAirThreatId,
            HostileFighterId,
            ConflictSide.Hostile,
            AirThreatType.Interceptor,
            hostileFighter.Position,
            RadiusNauticalMiles: 25,
            Severity: hostileFighter.Strength * hostileFighter.Readiness,
            Active: true);

        return ConflictWorldState.Create(
            "FICTIONAL-AIR",
            theaterSeed: 0xA11FACEUL,
            Epoch,
            new[] { friendlyGround },
            new[] { sector },
            new[] { interceptorThreat },
            new[] { friendlyTransport, hostileFighter });
    }

    private static AircraftTelemetrySnapshot TelemetryAt(
        GeoPoint point) =>
        new(
            Epoch,
            point.LatitudeDegrees,
            point.LongitudeDegrees,
            AltitudeMslFeet: 20_000,
            AltitudeAglFeet: 18_000,
            IndicatedAirspeedKnots: 300,
            GroundSpeedKnots: 320,
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

    private static readonly Guid FriendlyGroundId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    private static readonly Guid FriendlyTransportId =
        Guid.Parse("40000000-0000-0000-0000-000000000002");

    private static readonly Guid HostileFighterId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    private static readonly Guid HostileAirThreatId =
        Guid.Parse("60000000-0000-0000-0000-000000000001");
}
