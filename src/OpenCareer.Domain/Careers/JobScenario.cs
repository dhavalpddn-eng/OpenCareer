using OpenCareer.Domain.Events;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Careers;

public enum JobScenarioKind
{
    Standard,
    OrganTransport,
    TroopMovement,
    HumanitarianAirlift,
    ConflictReconnaissance,
    RecoverySupply,
    InfrastructureAssessment
}

public static class JobScenarioSelector
{
    public static JobScenarioKind Select(
        ServiceTrack track,
        ContractKind kind,
        RegionalSecurityState? security,
        DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        security?.Validate();

        // Rare historical-style emergency: an organ can justify unusually fast government/military lift.
        if (kind == ContractKind.Medical && random.Chance(0.025))
            return JobScenarioKind.OrganTransport;
        if (track == ServiceTrack.MilitaryService && kind == ContractKind.MilitaryTransport && random.Chance(0.005))
            return JobScenarioKind.OrganTransport;

        if (security?.Phase == RegionalSecurityPhase.ActiveConflict)
        {
            if (kind == ContractKind.MilitaryTransport && random.Chance(0.60))
                return JobScenarioKind.TroopMovement;
            if (kind == ContractKind.MilitarySurveillance && random.Chance(0.75))
                return JobScenarioKind.ConflictReconnaissance;
            if (kind is ContractKind.Medevac or ContractKind.Evacuation or ContractKind.DisasterRelief)
                return JobScenarioKind.HumanitarianAirlift;
        }

        if (security?.Phase is RegionalSecurityPhase.Ceasefire or RegionalSecurityPhase.Recovery)
        {
            if (kind is ContractKind.Cargo or ContractKind.ExpressCargo or ContractKind.DisasterRelief && random.Chance(0.65))
                return JobScenarioKind.RecoverySupply;
            if (kind == ContractKind.Survey && random.Chance(0.60))
                return JobScenarioKind.InfrastructureAssessment;
            if (kind is ContractKind.Medical or ContractKind.Medevac && random.Chance(0.45))
                return JobScenarioKind.HumanitarianAirlift;
        }

        return JobScenarioKind.Standard;
    }
}
