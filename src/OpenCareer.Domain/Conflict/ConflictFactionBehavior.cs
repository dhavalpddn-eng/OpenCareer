namespace OpenCareer.Domain.Conflict;

public enum ConflictFactionOperationalPosture
{
    Defensive,
    Aggressive,
    LogisticsFocused,
    AirFocused
}

public sealed record ConflictFactionBehaviorProfile(
    double BattlefieldPressureThreshold,
    double LowReadinessThreshold,
    double LowIntelligenceThreshold,
    double PatrolControlMinimum,
    double PatrolControlMaximum,
    double InterceptRangeNauticalMiles,
    double EscortRangeNauticalMiles,
    bool PromoteBattlefieldUrgency,
    bool PromoteLogisticsUrgency,
    bool PromoteAirUrgency)
{
    public void Validate()
    {
        ValidateUnitInterval(BattlefieldPressureThreshold, nameof(BattlefieldPressureThreshold));
        ValidateUnitInterval(LowReadinessThreshold, nameof(LowReadinessThreshold));
        ValidateUnitInterval(LowIntelligenceThreshold, nameof(LowIntelligenceThreshold));
        ValidateUnitInterval(PatrolControlMinimum, nameof(PatrolControlMinimum));
        ValidateUnitInterval(PatrolControlMaximum, nameof(PatrolControlMaximum));

        if (PatrolControlMinimum > PatrolControlMaximum)
            throw new ArgumentException("Patrol-control minimum cannot exceed maximum.");

        ValidateRange(InterceptRangeNauticalMiles, nameof(InterceptRangeNauticalMiles));
        ValidateRange(EscortRangeNauticalMiles, nameof(EscortRangeNauticalMiles));
    }

    private static void ValidateUnitInterval(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateRange(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 20 or > 200)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class ConflictFactionBehaviorPolicy
{
    private static readonly ConflictFactionBehaviorProfile Defensive = new(0.55, 0.35, 0.30, 0.40, 0.60, 120, 100, false, false, false);
    private static readonly ConflictFactionBehaviorProfile Aggressive = new(0.48, 0.30, 0.28, 0.43, 0.57, 130, 110, true, false, false);
    private static readonly ConflictFactionBehaviorProfile LogisticsFocused = new(0.62, 0.45, 0.30, 0.40, 0.60, 110, 90, false, true, false);
    private static readonly ConflictFactionBehaviorProfile AirFocused = new(0.56, 0.35, 0.36, 0.35, 0.65, 145, 125, false, false, true);

    public static ConflictFactionBehaviorProfile For(ConflictFactionOperationalPosture posture)
    {
        ConflictFactionBehaviorProfile profile = posture switch
        {
            ConflictFactionOperationalPosture.Defensive => Defensive,
            ConflictFactionOperationalPosture.Aggressive => Aggressive,
            ConflictFactionOperationalPosture.LogisticsFocused => LogisticsFocused,
            ConflictFactionOperationalPosture.AirFocused => AirFocused,
            _ => throw new ArgumentOutOfRangeException(nameof(posture))
        };

        profile.Validate();
        return profile;
    }

    public static ConflictFactionOperationalPosture EvolveForPhase(
        ConflictFactionOperationalPosture current,
        ConflictSide side,
        ConflictCampaignPhase previousPhase,
        ConflictCampaignPhase nextPhase)
    {
        if (!Enum.IsDefined(current))
            throw new ArgumentOutOfRangeException(nameof(current));
        if (side is not (ConflictSide.Friendly or ConflictSide.Hostile))
            throw new ArgumentOutOfRangeException(nameof(side));
        if (!Enum.IsDefined(previousPhase))
            throw new ArgumentOutOfRangeException(nameof(previousPhase));
        if (!Enum.IsDefined(nextPhase))
            throw new ArgumentOutOfRangeException(nameof(nextPhase));

        if (previousPhase == nextPhase || nextPhase == ConflictCampaignPhase.Contested)
            return current;

        bool sideHasPressure = side switch
        {
            ConflictSide.Friendly => nextPhase is ConflictCampaignPhase.FriendlyPressure or ConflictCampaignPhase.FriendlySecured,
            ConflictSide.Hostile => nextPhase is ConflictCampaignPhase.HostilePressure or ConflictCampaignPhase.HostileSecured,
            _ => false
        };

        bool sideIsSecured = side switch
        {
            ConflictSide.Friendly => nextPhase == ConflictCampaignPhase.FriendlySecured,
            ConflictSide.Hostile => nextPhase == ConflictCampaignPhase.HostileSecured,
            _ => false
        };

        if (sideIsSecured)
            return ConflictFactionOperationalPosture.LogisticsFocused;

        return sideHasPressure
            ? ConflictFactionOperationalPosture.Aggressive
            : ConflictFactionOperationalPosture.Defensive;
    }

    public static int ReplacementPriority(ConflictFactionOperationalPosture posture, GroundUnitRole role) =>
        posture switch
        {
            ConflictFactionOperationalPosture.Defensive => 0,
            ConflictFactionOperationalPosture.Aggressive => role switch
            {
                GroundUnitRole.Armor => 0, GroundUnitRole.Infantry => 1, GroundUnitRole.Command => 2,
                GroundUnitRole.AirDefense => 3, GroundUnitRole.Logistics => 4, _ => 5
            },
            ConflictFactionOperationalPosture.LogisticsFocused => role switch
            {
                GroundUnitRole.Logistics => 0, GroundUnitRole.Command => 1, GroundUnitRole.Infantry => 2,
                GroundUnitRole.AirDefense => 3, GroundUnitRole.Armor => 4, _ => 5
            },
            ConflictFactionOperationalPosture.AirFocused => role switch
            {
                GroundUnitRole.AirDefense => 0, GroundUnitRole.Command => 1, GroundUnitRole.Logistics => 2,
                GroundUnitRole.Armor => 3, GroundUnitRole.Infantry => 4, _ => 5
            },
            _ => throw new ArgumentOutOfRangeException(nameof(posture))
        };

    public static SupportUrgency Promote(SupportUrgency urgency) => urgency switch
    {
        SupportUrgency.Routine => SupportUrgency.Priority,
        SupportUrgency.Priority => SupportUrgency.Immediate,
        _ => SupportUrgency.Immediate
    };
}
