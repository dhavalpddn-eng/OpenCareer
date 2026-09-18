using OpenCareer.Domain.Careers;

namespace OpenCareer.Domain.Events;

public enum RegionalSecurityPhase
{
    Normal,
    ElevatedTension,
    ActiveConflict,
    Ceasefire,
    Recovery
}

public enum RegionalSecuritySource
{
    Simulated,
    CuratedLiveSignal
}

/// <summary>
/// Region-level security state. Live data may inform this record, but game effects are deterministic
/// and explicit; external text or AI output never directly changes payouts, access or mission completion.
/// </summary>
public sealed record RegionalSecurityState(
    string RegionId,
    RegionalSecurityPhase Phase,
    double Severity,
    RegionalSecuritySource Source,
    DateTimeOffset StartsAt,
    DateTimeOffset UpdatedAt,
    string? SourceReference = null)
{
    public static RegionalSecurityState Normal(string regionId, DateTimeOffset time) =>
        new(regionId, RegionalSecurityPhase.Normal, 0, RegionalSecuritySource.Simulated, time, time);

    public bool SuppressesCivilianBoard => Phase == RegionalSecurityPhase.ActiveConflict && Severity >= 0.85;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RegionId);
        if (!Enum.IsDefined(Phase) || !Enum.IsDefined(Source))
            throw new ArgumentOutOfRangeException(nameof(Phase));
        if (!double.IsFinite(Severity) || Severity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Severity));
        if (UpdatedAt < StartsAt)
            throw new ArgumentException("Security-state update cannot precede its start time.");
        if (Source == RegionalSecuritySource.CuratedLiveSignal && string.IsNullOrWhiteSpace(SourceReference))
            throw new ArgumentException("A live security state requires source provenance.", nameof(SourceReference));
    }

    public RegionalSecurityState TransitionTo(
        RegionalSecurityPhase nextPhase,
        double nextSeverity,
        DateTimeOffset time,
        RegionalSecuritySource source,
        string? sourceReference = null)
    {
        Validate();
        if (time < UpdatedAt)
            throw new InvalidOperationException("Security state cannot move backwards in time.");
        if (!IsAllowedTransition(Phase, nextPhase))
            throw new InvalidOperationException($"Invalid regional security transition: {Phase} -> {nextPhase}.");

        var result = new RegionalSecurityState(
            RegionId,
            nextPhase,
            nextSeverity,
            source,
            nextPhase == Phase ? StartsAt : time,
            time,
            sourceReference);
        result.Validate();
        return result;
    }

    public bool RequiresImmediateBoardRefreshFrom(RegionalSecurityState previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        Validate();
        previous.Validate();
        if (!string.Equals(RegionId, previous.RegionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot compare security states from different regions.");
        if (Phase == previous.Phase)
            return false;
        return Phase is RegionalSecurityPhase.ActiveConflict or RegionalSecurityPhase.Ceasefire or RegionalSecurityPhase.Recovery
            || previous.Phase == RegionalSecurityPhase.ActiveConflict;
    }

    public double TrackMultiplier(ServiceTrack track)
    {
        Validate();
        var civilian = track is ServiceTrack.CivilianEmployment or ServiceTrack.IndependentContract or ServiceTrack.CompanyContract;
        return Phase switch
        {
            RegionalSecurityPhase.Normal => 1,
            RegionalSecurityPhase.ElevatedTension when civilian => 0.85,
            RegionalSecurityPhase.ElevatedTension when track == ServiceTrack.GovernmentContract => 1.35,
            RegionalSecurityPhase.ElevatedTension when track == ServiceTrack.MilitaryService => 1.65,
            RegionalSecurityPhase.ActiveConflict when civilian => SuppressesCivilianBoard
                ? 0
                : Math.Max(0.05, 1 - 1.15 * Severity),
            RegionalSecurityPhase.ActiveConflict when track == ServiceTrack.GovernmentContract => 1.5 + 1.5 * Severity,
            RegionalSecurityPhase.ActiveConflict when track == ServiceTrack.MilitaryService => 2.0 + 4.0 * Severity,
            RegionalSecurityPhase.Ceasefire when civilian => 0.65,
            RegionalSecurityPhase.Ceasefire when track == ServiceTrack.GovernmentContract => 1.75,
            RegionalSecurityPhase.Ceasefire when track == ServiceTrack.MilitaryService => 1.35,
            RegionalSecurityPhase.Recovery when civilian => 1.20,
            RegionalSecurityPhase.Recovery when track == ServiceTrack.GovernmentContract => 1.45,
            RegionalSecurityPhase.Recovery when track == ServiceTrack.MilitaryService => 0.80,
            _ => 1
        };
    }

    public double KindMultiplier(ContractKind kind)
    {
        Validate();
        if (Phase == RegionalSecurityPhase.ActiveConflict)
        {
            return kind switch
            {
                ContractKind.MilitaryTransport => 2.40,
                ContractKind.MilitarySurveillance => 2.20,
                ContractKind.MilitaryPatrol => 1.90,
                ContractKind.MilitaryEscort => 1.65,
                ContractKind.MilitaryIntercept => 1.45,
                ContractKind.MilitaryTankerSupport => 1.50,
                ContractKind.Medevac => 2.10,
                ContractKind.Evacuation => 1.80,
                ContractKind.GovernmentCourier => 1.45,
                ContractKind.DisasterRelief => 1.35,
                _ => 1
            };
        }

        if (Phase is RegionalSecurityPhase.Ceasefire or RegionalSecurityPhase.Recovery)
        {
            return kind switch
            {
                ContractKind.Cargo => 1.75,
                ContractKind.ExpressCargo => 1.45,
                ContractKind.Medical => 1.55,
                ContractKind.Medevac => 1.45,
                ContractKind.DisasterRelief => 2.10,
                ContractKind.Survey => 1.55,
                ContractKind.Reposition => 1.35,
                ContractKind.Passenger => Phase == RegionalSecurityPhase.Recovery ? 1.25 : 0.75,
                ContractKind.Charter => Phase == RegionalSecurityPhase.Recovery ? 1.20 : 0.70,
                _ => 1
            };
        }

        return 1;
    }

    private static bool IsAllowedTransition(RegionalSecurityPhase current, RegionalSecurityPhase next) =>
        current == next || (current, next) switch
        {
            (RegionalSecurityPhase.Normal, RegionalSecurityPhase.ElevatedTension) => true,
            (RegionalSecurityPhase.Normal, RegionalSecurityPhase.ActiveConflict) => true,
            (RegionalSecurityPhase.ElevatedTension, RegionalSecurityPhase.Normal) => true,
            (RegionalSecurityPhase.ElevatedTension, RegionalSecurityPhase.ActiveConflict) => true,
            (RegionalSecurityPhase.ActiveConflict, RegionalSecurityPhase.Ceasefire) => true,
            (RegionalSecurityPhase.Ceasefire, RegionalSecurityPhase.ActiveConflict) => true,
            (RegionalSecurityPhase.Ceasefire, RegionalSecurityPhase.Recovery) => true,
            (RegionalSecurityPhase.Recovery, RegionalSecurityPhase.Normal) => true,
            (RegionalSecurityPhase.Recovery, RegionalSecurityPhase.ActiveConflict) => true,
            _ => false
        };
}
