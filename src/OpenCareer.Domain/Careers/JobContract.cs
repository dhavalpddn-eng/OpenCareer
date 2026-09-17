using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Careers;

public enum ContractKind
{
    Ferry,
    Reposition,
    Cargo,
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
    GovernmentCourier,
    MilitaryTraining,
    MilitaryIntercept,
    MilitaryEscort,
    MilitaryPatrol,
    MilitaryTransport,
    MilitaryTankerSupport,
    MilitarySurveillance,
    Other
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

public sealed record JobContract(
    Guid ContractId,
    Guid? EmployerId,
    ContractKind Kind,
    string OriginIcao,
    string DestinationIcao,
    decimal GrossPay,
    DateTimeOffset OfferedAt,
    DateTimeOffset? MustStartBy,
    DateTimeOffset? MustCompleteBy,
    AircraftMissionRequirements AircraftRequirements,
    ContractStatus Status = ContractStatus.Offered,
    double ReputationReward = 1.0,
    double ReputationPenalty = 2.0,
    string? WorldEventId = null,
    bool GovernmentAuthorizationRequired = false)
{
    public JobContract Accept() => Status == ContractStatus.Offered
        ? this with { Status = ContractStatus.Accepted }
        : throw new InvalidOperationException($"Cannot accept contract in state {Status}.");

    public JobContract Start() => Status == ContractStatus.Accepted
        ? this with { Status = ContractStatus.InProgress }
        : throw new InvalidOperationException($"Cannot start contract in state {Status}.");

    public JobContract Complete() => Status == ContractStatus.InProgress
        ? this with { Status = ContractStatus.Completed }
        : throw new InvalidOperationException($"Cannot complete contract in state {Status}.");
}
