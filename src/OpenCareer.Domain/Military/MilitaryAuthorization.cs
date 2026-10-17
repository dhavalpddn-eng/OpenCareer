using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Conflict;

namespace OpenCareer.Domain.Military;

public enum MilitaryAffiliation
{
    None,
    GovernmentContractor,
    Reserve,
    ActiveDuty
}

[Flags]
public enum MilitaryQualification
{
    None = 0,
    MilitaryFlight = 1 << 0,
    Reconnaissance = 1 << 1,
    Logistics = 1 << 2,
    Patrol = 1 << 3,
    Escort = 1 << 4,
    Intercept = 1 << 5,
    CloseAirSupport = 1 << 6,
    Suppression = 1 << 7,
    CarrierOperations = 1 << 8
}

public sealed record MilitaryCareerState(
    MilitaryAffiliation Affiliation,
    MilitaryQualification Qualifications,
    double Trust,
    int SuccessfulOperations,
    int FailedOperations)
{
    public static MilitaryCareerState Civilian { get; } =
        new(
            MilitaryAffiliation.None,
            MilitaryQualification.None,
            Trust: 0,
            SuccessfulOperations: 0,
            FailedOperations: 0);

    public bool Has(MilitaryQualification qualification) =>
        (Qualifications & qualification) == qualification;

    public void Validate()
    {
        if ((Qualifications & ~AllQualifications) != 0)
            throw new ArgumentException("Unknown military qualification.");

        if (!double.IsFinite(Trust) || Trust is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Trust));

        if (SuccessfulOperations < 0)
            throw new ArgumentOutOfRangeException(nameof(SuccessfulOperations));

        if (FailedOperations < 0)
            throw new ArgumentOutOfRangeException(nameof(FailedOperations));

        if (Affiliation == MilitaryAffiliation.None
            && Qualifications != MilitaryQualification.None)
        {
            throw new ArgumentException(
                "Unaffiliated career cannot hold military qualifications.");
        }
    }

    private const MilitaryQualification AllQualifications =
        MilitaryQualification.MilitaryFlight
        | MilitaryQualification.Reconnaissance
        | MilitaryQualification.Logistics
        | MilitaryQualification.Patrol
        | MilitaryQualification.Escort
        | MilitaryQualification.Intercept
        | MilitaryQualification.CloseAirSupport
        | MilitaryQualification.Suppression
        | MilitaryQualification.CarrierOperations;
}

public sealed record MilitaryOperationEligibility(
    bool Eligible,
    MilitaryQualification RequiredQualification,
    string? BlockingReason);

public static class MilitaryAuthorizationPolicy
{
    public static MilitaryOperationEligibility Evaluate(
        MilitaryCareerState career,
        AircraftCapabilityProfile aircraft,
        bool aircraftAssignedForOperation,
        SupportRequestType operationType,
        PlayerCombatState playerCombatState)
    {
        ArgumentNullException.ThrowIfNull(career);
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(playerCombatState);

        career.Validate();
        aircraft.Validate();
        playerCombatState.Validate();

        var requiredQualification =
            RequiredQualification(operationType);

        if (career.Affiliation == MilitaryAffiliation.None)
        {
            return Blocked(
                requiredQualification,
                "Military/government affiliation is required.");
        }

        if (!career.Has(MilitaryQualification.MilitaryFlight))
        {
            return Blocked(
                requiredQualification,
                "Military flight qualification is required.");
        }

        if (!career.Has(requiredQualification))
        {
            return Blocked(
                requiredQualification,
                $"Qualification {requiredQualification} is required.");
        }

        if (!aircraftAssignedForOperation)
        {
            return Blocked(
                requiredQualification,
                "The aircraft is not assigned/authorized for this operation.");
        }

        if ((aircraft.Access & AircraftAccess.Military) == 0)
        {
            return Blocked(
                requiredQualification,
                "The aircraft profile does not permit military access.");
        }

        if (!aircraft.Has(AircraftCapability.Military))
        {
            return Blocked(
                requiredQualification,
                "The aircraft is not classified for military operations.");
        }

        if (!AircraftSupports(aircraft, operationType))
        {
            return Blocked(
                requiredQualification,
                "The assigned aircraft lacks the required mission capability.");
        }

        if (!playerCombatState.MissionCapable
            && operationType is (
                SupportRequestType.CloseAirSupport
                or SupportRequestType.Suppression
                or SupportRequestType.Escort
                or SupportRequestType.Intercept))
        {
            return Blocked(
                requiredQualification,
                "Current simulated aircraft damage makes the aircraft mission-incapable.");
        }

        return new MilitaryOperationEligibility(
            Eligible: true,
            requiredQualification,
            BlockingReason: null);
    }

    public static MilitaryQualification RequiredQualification(
        SupportRequestType type) =>
        type switch
        {
            SupportRequestType.CloseAirSupport =>
                MilitaryQualification.CloseAirSupport,
            SupportRequestType.Suppression =>
                MilitaryQualification.Suppression,
            SupportRequestType.Reconnaissance =>
                MilitaryQualification.Reconnaissance,
            SupportRequestType.Logistics =>
                MilitaryQualification.Logistics,
            SupportRequestType.Patrol =>
                MilitaryQualification.Patrol,
            SupportRequestType.Escort =>
                MilitaryQualification.Escort,
            SupportRequestType.Intercept =>
                MilitaryQualification.Intercept,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private static bool AircraftSupports(
        AircraftCapabilityProfile aircraft,
        SupportRequestType operationType) =>
        operationType switch
        {
            SupportRequestType.CloseAirSupport =>
                aircraft.Has(AircraftCapability.Fighter),
            SupportRequestType.Suppression =>
                aircraft.Has(AircraftCapability.Fighter),
            SupportRequestType.Reconnaissance =>
                aircraft.Has(AircraftCapability.Surveillance)
                || aircraft.Has(AircraftCapability.Fighter)
                || aircraft.Has(AircraftCapability.MaritimePatrol),
            SupportRequestType.Logistics =>
                aircraft.Has(AircraftCapability.Cargo)
                || aircraft.Has(AircraftCapability.StrategicTransport),
            SupportRequestType.Patrol =>
                aircraft.Has(AircraftCapability.Fighter)
                || aircraft.Has(AircraftCapability.Surveillance)
                || aircraft.Has(AircraftCapability.MaritimePatrol),
            SupportRequestType.Escort =>
                aircraft.Has(AircraftCapability.Fighter),
            SupportRequestType.Intercept =>
                aircraft.Has(AircraftCapability.Fighter),
            _ => false
        };

    private static MilitaryOperationEligibility Blocked(
        MilitaryQualification requiredQualification,
        string reason) =>
        new(
            Eligible: false,
            requiredQualification,
            reason);
}

public static class MilitaryCareerProgression
{
    public static MilitaryCareerState GrantQualification(
        MilitaryCareerState career,
        MilitaryQualification qualification)
    {
        ArgumentNullException.ThrowIfNull(career);
        career.Validate();

        var rawQualification = (int)qualification;
        if (qualification == MilitaryQualification.None
            || (rawQualification & (rawQualification - 1)) != 0)
        {
            throw new ArgumentException(
                "Grant exactly one qualification at a time.",
                nameof(qualification));
        }

        if (career.Affiliation == MilitaryAffiliation.None)
            throw new InvalidOperationException("Military affiliation is required.");

        if (qualification != MilitaryQualification.MilitaryFlight
            && !career.Has(MilitaryQualification.MilitaryFlight))
        {
            throw new InvalidOperationException(
                "Military flight qualification is prerequisite to mission qualifications.");
        }

        var updated = career with
        {
            Qualifications = career.Qualifications | qualification
        };

        updated.Validate();
        return updated;
    }

    public static MilitaryCareerState RecordOperationResult(
        MilitaryCareerState career,
        bool success,
        SupportUrgency urgency)
    {
        ArgumentNullException.ThrowIfNull(career);
        career.Validate();

        if (career.Affiliation == MilitaryAffiliation.None)
            throw new InvalidOperationException("Military affiliation is required.");

        var successGain = urgency switch
        {
            SupportUrgency.Immediate => 0.025,
            SupportUrgency.Priority => 0.020,
            _ => 0.015
        };

        var failureLoss = urgency switch
        {
            SupportUrgency.Immediate => 0.040,
            SupportUrgency.Priority => 0.035,
            _ => 0.030
        };

        var updated = success
            ? career with
            {
                Trust = Math.Clamp(career.Trust + successGain, 0, 1),
                SuccessfulOperations = checked(career.SuccessfulOperations + 1)
            }
            : career with
            {
                Trust = Math.Clamp(career.Trust - failureLoss, 0, 1),
                FailedOperations = checked(career.FailedOperations + 1)
            };

        updated.Validate();
        return updated;
    }
}
