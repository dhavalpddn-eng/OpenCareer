using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Events;

namespace OpenCareer.Domain.Careers;

public enum ContractKind
{
    Ferry,
    Reposition,
    Cargo,
    ExpressCargo,
    AogPartsDelivery,
    Passenger,
    Charter,
    Medical,
    Medevac,
    Survey,
    Photography,
    GliderTow,
    Skydiving,
    BannerTow,
    Agricultural,
    Firefighting,
    SearchAndRescue,
    DisasterRelief,
    Evacuation,
    GovernmentCourier,
    MilitaryTraining,
    MilitaryReadiness,
    MilitaryIntercept,
    MilitaryEscort,
    MilitaryPatrol,
    MilitaryTransport,
    MilitaryFerry,
    MilitaryTankerSupport,
    MilitarySurveillance,
    Other
}

public enum ServiceTrack
{
    CivilianEmployment,
    IndependentContract,
    CompanyContract,
    GovernmentContract,
    MilitaryService
}

public enum CompensationModel
{
    PilotWage,
    MissionFee,
    CompanyRevenue,
    SalaryDuty,
    Reimbursement
}

public enum ContractStatus
{
    Offered,
    Accepted,
    InProgress,
    Completed,
    Failed,
    Cancelled,
    Expired
}

public sealed record ContractCompensation(
    CompensationModel Model,
    decimal GrossCustomerRevenue,
    decimal PilotCompensation,
    bool EmployerCoversFuel,
    bool EmployerCoversMaintenance,
    bool EmployerCoversAirportFees);

public sealed record JobContract(
    Guid ContractId,
    Guid? EmployerId,
    ContractKind Kind,
    ServiceTrack ServiceTrack,
    string OriginIcao,
    string DestinationIcao,
    ContractCompensation Compensation,
    DateTimeOffset OfferedAt,
    DateTimeOffset? MustStartBy,
    DateTimeOffset? MustCompleteBy,
    AircraftMissionRequirements AircraftRequirements,
    ContractStatus Status = ContractStatus.Offered,
    double ReputationReward = 1.0,
    double ReputationPenalty = 2.0,
    string? MarketId = null,
    string? WorldEventId = null,
    bool GovernmentAuthorizationRequired = false,
    DateTimeOffset? AcceptedAt = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    ContractEconomicSnapshot? EconomicSnapshot = null)
{
    public JobContract Accept(ContractDispatchContext context)
    {
        if (Status != ContractStatus.Offered) throw InvalidTransition("accept");
        ValidateDispatch(context);
        return this with { Status = ContractStatus.Accepted, AcceptedAt = context.Time };
    }

    public JobContract Start(ContractDispatchContext context)
    {
        if (Status != ContractStatus.Accepted) throw InvalidTransition("start");
        ValidateDispatch(context); // Recheck restrictions and eligibility at departure.
        if (AcceptedAt is not { } accepted || context.Time < accepted)
            throw new InvalidOperationException("Departure cannot precede acceptance.");
        return this with { Status = ContractStatus.InProgress, StartedAt = context.Time };
    }

    public JobContract Complete(DateTimeOffset time, bool flightCompletionVerified)
    {
        Validate();
        if (Status != ContractStatus.InProgress) throw InvalidTransition("complete");
        if (!flightCompletionVerified || StartedAt is not { } started || time < started || (MustCompleteBy is { } deadline && time > deadline))
            throw new InvalidOperationException("Completion requires verified flight evidence within the contract deadline.");
        return this with { Status = ContractStatus.Completed, CompletedAt = time };
    }

    public JobContract Fail() => Status is ContractStatus.Accepted or ContractStatus.InProgress
        ? this with { Status = ContractStatus.Failed }
        : throw InvalidTransition("fail");

    public JobContract Cancel() => Status is ContractStatus.Offered or ContractStatus.Accepted
        ? this with { Status = ContractStatus.Cancelled }
        : throw InvalidTransition("cancel");

    public JobContract Expire(DateTimeOffset time)
    {
        Validate();
        if (Status != ContractStatus.Offered) throw InvalidTransition("expire");
        if (!(MustStartBy is { } start && time > start) && !(MustCompleteBy is { } end && time > end))
            throw new InvalidOperationException("The offer has not expired.");
        return this with { Status = ContractStatus.Expired };
    }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Compensation);
        ArgumentNullException.ThrowIfNull(AircraftRequirements);
        AircraftRequirements.Validate();
        EconomicSnapshot?.Validate();

        if (EconomicSnapshot is not null
            && EconomicSnapshot.Compensation != Compensation)
        {
            throw new ArgumentException(
                "Contract compensation must match the immutable quoted economic snapshot.");
        }
        if ((AcceptedAt is { } accepted && accepted < OfferedAt)
            || (StartedAt is { } started && (AcceptedAt is not { } a || started < a))
            || (CompletedAt is { } completed && (StartedAt is not { } s || completed < s))
            || (Status is ContractStatus.Accepted or ContractStatus.InProgress or ContractStatus.Completed && AcceptedAt is null)
            || (Status is ContractStatus.InProgress or ContractStatus.Completed && StartedAt is null)
            || (Status == ContractStatus.Completed && CompletedAt is null))
            throw new ArgumentException("Contract lifecycle timestamps are inconsistent.");
        if (ContractId == Guid.Empty || string.IsNullOrWhiteSpace(OriginIcao) || string.IsNullOrWhiteSpace(DestinationIcao)
            || !Enum.IsDefined(Kind) || !Enum.IsDefined(ServiceTrack) || !Enum.IsDefined(Status)
            || !Enum.IsDefined(Compensation.Model) || Compensation.GrossCustomerRevenue < 0 || Compensation.PilotCompensation < 0
            || !double.IsFinite(ReputationReward) || ReputationReward < 0
            || !double.IsFinite(ReputationPenalty) || ReputationPenalty < 0
            || (MustStartBy is { } start && start < OfferedAt)
            || (MustCompleteBy is { } end && (end < OfferedAt || (MustStartBy is { } begin && end < begin))))
            throw new ArgumentException("Invalid contract.");
    }

    private void ValidateDispatch(ContractDispatchContext context)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Aircraft);
        WorldEventEngine.ValidateEffects(context.Effects);
        if (context.Time < OfferedAt || (MustStartBy is { } start && context.Time > start)
            || (MustCompleteBy is { } end && context.Time > end))
            throw new InvalidOperationException("Contract is not available at this career time.");
        if (!context.QualificationsVerified || !context.DispatchFeasibilityVerified
            || !context.Aircraft.Satisfies(AircraftRequirements)
            || (context.AuthorizedAccess & context.Aircraft.Access & AircraftRequirements.AllowedAccess) == 0
            || (GovernmentAuthorizationRequired && !context.GovernmentAuthorized)
            || (ServiceTrack == ServiceTrack.GovernmentContract && !context.GovernmentAuthorized)
            || (ServiceTrack == ServiceTrack.MilitaryService && (context.AuthorizedAccess & AircraftAccess.Military) == 0))
            throw new InvalidOperationException("Aircraft, qualifications, dispatch feasibility or authorization does not permit this job.");
        var restriction = context.Effects.AirspaceRestriction;
        if (restriction == AirspaceRestriction.Closed
            || (restriction == AirspaceRestriction.AuthorizedOperationsOnly && !context.RestrictedAirspaceAuthorized)
            || (restriction == AirspaceRestriction.MilitaryAndEmergencyAuthorizedOnly
                && !(context.RestrictedAirspaceAuthorized &&
                    ((ServiceTrack == ServiceTrack.MilitaryService && (context.AuthorizedAccess & AircraftAccess.Military) != 0)
                     || (context.EmergencyOperationAuthorized && IsEmergencyJob())))))
            throw new InvalidOperationException("Airspace restriction prevents departure.");
        if (context.Effects.NavigationAvailability != NavigationAvailability.Normal && !context.NavigationPlanVerified)
            throw new InvalidOperationException("A navigation plan verified for the current outage is required.");
    }

    private bool IsEmergencyJob() => Kind is ContractKind.Medical or ContractKind.Medevac
        or ContractKind.SearchAndRescue or ContractKind.DisasterRelief or ContractKind.Evacuation or ContractKind.Firefighting;

    private InvalidOperationException InvalidTransition(string action) =>
        new($"Cannot {action} contract in state {Status}.");
}

// Application-supplied evidence. Defaults deny unverified operation. These fields
// are not a replacement for the planned licensing, route planning and telemetry services.
public sealed record ContractDispatchContext(
    DateTimeOffset Time,
    AircraftCapabilityProfile Aircraft,
    AircraftAccess AuthorizedAccess,
    WorldEventEffects Effects,
    bool QualificationsVerified = false,
    bool DispatchFeasibilityVerified = false,
    bool GovernmentAuthorized = false,
    bool RestrictedAirspaceAuthorized = false,
    bool EmergencyOperationAuthorized = false,
    bool NavigationPlanVerified = false);
