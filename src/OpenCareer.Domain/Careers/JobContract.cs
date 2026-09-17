using OpenCareer.Domain.Aircraft;

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
    bool GovernmentAuthorizationRequired = false)
{
    public JobContract Accept() => Status == ContractStatus.Offered
        ? this with { Status = ContractStatus.Accepted }
        : throw InvalidTransition("accept");

    public JobContract Start() => Status == ContractStatus.Accepted
        ? this with { Status = ContractStatus.InProgress }
        : throw InvalidTransition("start");

    public JobContract Complete() => Status == ContractStatus.InProgress
        ? this with { Status = ContractStatus.Completed }
        : throw InvalidTransition("complete");

    public JobContract Fail() => Status is ContractStatus.Accepted or ContractStatus.InProgress
        ? this with { Status = ContractStatus.Failed }
        : throw InvalidTransition("fail");

    public JobContract Cancel() => Status is ContractStatus.Offered or ContractStatus.Accepted
        ? this with { Status = ContractStatus.Cancelled }
        : throw InvalidTransition("cancel");

    public JobContract Expire() => Status == ContractStatus.Offered
        ? this with { Status = ContractStatus.Expired }
        : throw InvalidTransition("expire");

    private InvalidOperationException InvalidTransition(string action) =>
        new($"Cannot {action} contract in state {Status}.");
}
