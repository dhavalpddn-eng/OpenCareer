using OpenCareer.Application.Military;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryDispatchServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OfferRemainsVisibleButIneligibleWhenQualificationIsMissing()
    {
        var world = WorldWithCasRequest();
        var dispatch = new MilitaryDispatchService(
            new ConflictOperationsService());

        var career = new MilitaryCareerState(
            MilitaryAffiliation.Reserve,
            MilitaryQualification.MilitaryFlight,
            Trust: 0.5,
            SuccessfulOperations: 0,
            FailedOperations: 0);

        var offer = Assert.Single(
            dispatch.GetOffers(
                world,
                career,
                PlayerCombatState.Undamaged,
                Fighter(),
                aircraftAssignedForOperation: true));

        Assert.False(offer.Eligibility.Eligible);
        Assert.Equal(
            MilitaryQualification.CloseAirSupport,
            offer.Eligibility.RequiredQualification);
    }

    [Fact]
    public void AcceptRejectsCallerThatWouldOtherwiseBypassAuthorization()
    {
        var world = WorldWithCasRequest();
        var dispatch = new MilitaryDispatchService(
            new ConflictOperationsService());

        var career = new MilitaryCareerState(
            MilitaryAffiliation.Reserve,
            MilitaryQualification.MilitaryFlight,
            Trust: 0.5,
            SuccessfulOperations: 0,
            FailedOperations: 0);

        Assert.Throws<MilitaryOperationAuthorizationException>(
            () => dispatch.Accept(
                world,
                world.SupportRequests.Single().RequestId,
                Guid.Parse("81000000-0000-0000-0000-000000000001"),
                Epoch.AddMinutes(1),
                career,
                PlayerCombatState.Undamaged,
                Fighter(),
                aircraftAssignedForOperation: true));
    }

    [Fact]
    public void AuthorizedCasAcceptanceReservesRequestAndReturnsCombatMission()
    {
        var world = WorldWithCasRequest();
        var dispatch = new MilitaryDispatchService(
            new ConflictOperationsService());

        var career = new MilitaryCareerState(
            MilitaryAffiliation.Reserve,
            MilitaryQualification.MilitaryFlight
                | MilitaryQualification.CloseAirSupport,
            Trust: 0.65,
            SuccessfulOperations: 3,
            FailedOperations: 0);

        Guid missionId =
            Guid.Parse("82000000-0000-0000-0000-000000000001");

        AcceptedMilitaryOperation accepted = dispatch.Accept(
            world,
            world.SupportRequests.Single().RequestId,
            missionId,
            Epoch.AddMinutes(1),
            career,
            PlayerCombatState.Undamaged,
            Fighter(),
            aircraftAssignedForOperation: true);

        Assert.Equal(SupportRequestType.CloseAirSupport, accepted.Type);
        Assert.NotNull(accepted.CombatSupportMission);
        Assert.Null(accepted.AreaSupportMission);
        Assert.Null(accepted.AirOperationMission);

        var request = accepted.World.SupportRequests.Single();

        Assert.Equal(SupportRequestStatus.Reserved, request.Status);
        Assert.Equal(missionId, request.ReservedMissionId);
    }

    [Fact]
    public void InstalledButUnassignedAircraftCannotAcceptOperation()
    {
        var world = WorldWithCasRequest();
        var dispatch = new MilitaryDispatchService(
            new ConflictOperationsService());

        var career = new MilitaryCareerState(
            MilitaryAffiliation.ActiveDuty,
            MilitaryQualification.MilitaryFlight
                | MilitaryQualification.CloseAirSupport,
            Trust: 0.8,
            SuccessfulOperations: 10,
            FailedOperations: 1);

        Assert.Throws<MilitaryOperationAuthorizationException>(
            () => dispatch.Accept(
                world,
                world.SupportRequests.Single().RequestId,
                Guid.Parse("83000000-0000-0000-0000-000000000001"),
                Epoch.AddMinutes(1),
                career,
                PlayerCombatState.Undamaged,
                Fighter(),
                aircraftAssignedForOperation: false));
    }

    private static ConflictWorldState WorldWithCasRequest()
    {
        var friendly = new GroundUnitState(
            Guid.Parse("84000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35, -97),
            0.8,
            0.8,
            0.8,
            true);

        var hostile = new GroundUnitState(
            Guid.Parse("85000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.03, -96.97),
            0.9,
            0.9,
            0,
            true);

        var world = ConflictWorldState.Create(
            "FICTIONAL-DISPATCH",
            0xD15A7C4UL,
            Epoch,
            new[] { friendly, hostile },
            new[]
            {
                new ConflictSectorState(
                    "DISPATCH-S1",
                    new GeoPoint(35.01, -96.99),
                    0.5,
                    0.6)
            });

        var request = new AirSupportRequest(
            "dispatch-cas-001",
            SupportRequestType.CloseAirSupport,
            SupportUrgency.Priority,
            friendly.UnitId,
            hostile.UnitId,
            hostile.Position,
            0.25,
            Epoch,
            Epoch.AddHours(1));

        return world with
        {
            SupportRequests = new[] { request }
        };
    }

    private static AircraftCapabilityProfile Fighter() =>
        new(
            AircraftId: "dispatch-fighter",
            DisplayName: "Assigned Fighter",
            Capabilities:
                AircraftCapability.Military
                | AircraftCapability.Fighter,
            Access: AircraftAccess.Military,
            MaximumPayloadPounds: 12_000,
            MaximumRangeNauticalMiles: 1_200,
            TypicalCruiseKnots: 450,
            Seats: 1,
            EngineCount: 2,
            IfrCapable: true,
            Pressurized: true,
            RetractableGear: true);
}
