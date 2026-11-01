using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Application.Military;

public sealed record MilitaryOperationOffer(
    AirSupportRequest Request,
    MilitaryOperationEligibility Eligibility);

public sealed record AcceptedMilitaryOperation(
    ConflictWorldState World,
    Guid MissionId,
    SupportRequestType Type,
    AirSupportMission? CombatSupportMission,
    AreaSupportMission? AreaSupportMission,
    AirOperationMission? AirOperationMission)
{
    public void Validate()
    {
        ConflictValidation.Validate(World);

        if (MissionId == Guid.Empty)
            throw new ArgumentException("Accepted military mission ID is required.");

        int populated =
            (CombatSupportMission is null ? 0 : 1)
            + (AreaSupportMission is null ? 0 : 1)
            + (AirOperationMission is null ? 0 : 1);

        if (populated != 1)
            throw new ArgumentException("Accepted military operation must contain exactly one mission payload.");
    }
}

public sealed class MilitaryOperationAuthorizationException : InvalidOperationException
{
    public MilitaryOperationAuthorizationException(string message)
        : base(message)
    {
    }
}

public sealed class MilitaryDispatchService
{
    private readonly ConflictOperationsService _operations;

    public MilitaryDispatchService(ConflictOperationsService operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public IReadOnlyList<MilitaryOperationOffer> GetOffers(
        ConflictWorldState world,
        MilitaryCareerState career,
        PlayerCombatState playerCombatState,
        AircraftCapabilityProfile aircraft,
        bool aircraftAssignedForOperation)
    {
        ConflictValidation.Validate(world);
        ArgumentNullException.ThrowIfNull(career);
        ArgumentNullException.ThrowIfNull(playerCombatState);
        ArgumentNullException.ThrowIfNull(aircraft);

        career.Validate();
        playerCombatState.Validate();
        aircraft.Validate();

        return world.SupportRequests
            .Where(request =>
                request.Status == SupportRequestStatus.Open
                && !request.IsPastOpenDeadline(world.UpdatedAt))
            .OrderByDescending(request => request.Urgency)
            .ThenBy(request => request.CreatedAt)
            .ThenBy(request => request.RequestId, StringComparer.Ordinal)
            .Select(request => new MilitaryOperationOffer(
                request,
                MilitaryAuthorizationPolicy.Evaluate(
                    career,
                    aircraft,
                    aircraftAssignedForOperation,
                    request.Type,
                    playerCombatState)))
            .ToArray();
    }

    public AcceptedMilitaryOperation Accept(
        ConflictWorldState world,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt,
        MilitaryCareerState career,
        PlayerCombatState playerCombatState,
        AircraftCapabilityProfile aircraft,
        bool aircraftAssignedForOperation)
    {
        ConflictValidation.Validate(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var request = world.SupportRequests.SingleOrDefault(
            candidate => string.Equals(
                candidate.RequestId,
                requestId,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "Military support request was not found.");

        if (request.Status != SupportRequestStatus.Open)
            throw new InvalidOperationException("Military support request is not open.");

        if (request.IsPastOpenDeadline(acceptedAt))
            throw new InvalidOperationException("Military support request has expired.");

        MilitaryOperationEligibility eligibility =
            MilitaryAuthorizationPolicy.Evaluate(
                career,
                aircraft,
                aircraftAssignedForOperation,
                request.Type,
                playerCombatState);

        if (!eligibility.Eligible)
        {
            throw new MilitaryOperationAuthorizationException(
                eligibility.BlockingReason
                ?? "Military operation is not authorized.");
        }

        AcceptedMilitaryOperation accepted = request.Type switch
        {
            SupportRequestType.CloseAirSupport
                or SupportRequestType.Suppression =>
                AcceptCombat(world, requestId, missionId, acceptedAt, request.Type),

            SupportRequestType.Reconnaissance
                or SupportRequestType.Logistics
                or SupportRequestType.Patrol =>
                AcceptArea(world, requestId, missionId, acceptedAt, request.Type),

            SupportRequestType.Escort
                or SupportRequestType.Intercept =>
                AcceptAirOperation(world, requestId, missionId, acceptedAt, request.Type),

            _ => throw new NotSupportedException(
                $"Support request type {request.Type} is not implemented by military dispatch.")
        };

        accepted.Validate();
        return accepted;
    }

    private AcceptedMilitaryOperation AcceptCombat(
        ConflictWorldState world,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt,
        SupportRequestType type)
    {
        AcceptedAirSupportMission result =
            _operations.AcceptCombatSupportRequest(
                world,
                requestId,
                missionId,
                acceptedAt);

        return new AcceptedMilitaryOperation(
            result.World,
            missionId,
            type,
            result.Mission,
            AreaSupportMission: null,
            AirOperationMission: null);
    }

    private AcceptedMilitaryOperation AcceptArea(
        ConflictWorldState world,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt,
        SupportRequestType type)
    {
        AcceptedAreaSupportMission result =
            _operations.AcceptAreaSupportRequest(
                world,
                requestId,
                missionId,
                acceptedAt);

        return new AcceptedMilitaryOperation(
            result.World,
            missionId,
            type,
            CombatSupportMission: null,
            result.Mission,
            AirOperationMission: null);
    }

    private AcceptedMilitaryOperation AcceptAirOperation(
        ConflictWorldState world,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt,
        SupportRequestType type)
    {
        AcceptedAirOperationMission result =
            _operations.AcceptAirOperationRequest(
                world,
                requestId,
                missionId,
                acceptedAt);

        return new AcceptedMilitaryOperation(
            result.World,
            missionId,
            type,
            CombatSupportMission: null,
            AreaSupportMission: null,
            result.Mission);
    }
}
