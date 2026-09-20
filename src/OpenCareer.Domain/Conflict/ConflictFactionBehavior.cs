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
        ValidateUnitInterval(
            BattlefieldPressureThreshold,
            nameof(BattlefieldPressureThreshold));
        ValidateUnitInterval(
            LowReadinessThreshold,
            nameof(LowReadinessThreshold));
        ValidateUnitInterval(
            LowIntelligenceThreshold,
            nameof(LowIntelligenceThreshold));
        ValidateUnitInterval(
            PatrolControlMinimum,
            nameof(PatrolControlMinimum));
        ValidateUnitInterval(
            PatrolControlMaximum,
            nameof(PatrolControlMaximum));

        if (PatrolControlMinimum > PatrolControlMaximum)
        {
            throw new ArgumentException(
                "Patrol-control minimum cannot exceed maximum.");
        }

        ValidateRange(
            InterceptRangeNauticalMiles,
            nameof(InterceptRangeNauticalMiles));
        ValidateRange(
            EscortRangeNauticalMiles,
            nameof(EscortRangeNauticalMiles));
    }

    private static void ValidateUnitInterval(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateRange(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value is < 20 or > 200)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class ConflictFactionBehaviorPolicy
{
    private static readonly ConflictFactionBehaviorProfile Defensive =
        new(
            BattlefieldPressureThreshold: 0.55,
            LowReadinessThreshold: 0.35,
            LowIntelligenceThreshold: 0.30,
            PatrolControlMinimum: 0.40,
            PatrolControlMaximum: 0.60,
            InterceptRangeNauticalMiles: 120,
            EscortRangeNauticalMiles: 100,
            PromoteBattlefieldUrgency: false,
            PromoteLogisticsUrgency: false,
            PromoteAirUrgency: false);

    private static readonly ConflictFactionBehaviorProfile Aggressive =
        new(
            BattlefieldPressureThreshold: 0.48,
            LowReadinessThreshold: 0.30,
            LowIntelligenceThreshold: 0.28,
            PatrolControlMinimum: 0.43,
            PatrolControlMaximum: 0.57,
            InterceptRangeNauticalMiles: 130,
            EscortRangeNauticalMiles: 110,
            PromoteBattlefieldUrgency: true,
            PromoteLogisticsUrgency: false,
            PromoteAirUrgency: false);

    private static readonly ConflictFactionBehaviorProfile LogisticsFocused =
        new(
            BattlefieldPressureThreshold: 0.62,
            LowReadinessThreshold: 0.45,
            LowIntelligenceThreshold: 0.30,
            PatrolControlMinimum: 0.40,
            PatrolControlMaximum: 0.60,
            InterceptRangeNauticalMiles: 110,
            EscortRangeNauticalMiles: 90,
            PromoteBattlefieldUrgency: false,
            PromoteLogisticsUrgency: true,
            PromoteAirUrgency: false);

    private static readonly ConflictFactionBehaviorProfile AirFocused =
        new(
            BattlefieldPressureThreshold: 0.56,
            LowReadinessThreshold: 0.35,
            LowIntelligenceThreshold: 0.36,
            PatrolControlMinimum: 0.35,
            PatrolControlMaximum: 0.65,
            InterceptRangeNauticalMiles: 145,
            EscortRangeNauticalMiles: 125,
            PromoteBattlefieldUrgency: false,
            PromoteLogisticsUrgency: false,
            PromoteAirUrgency: true);

    public static ConflictFactionBehaviorProfile For(
        ConflictFactionOperationalPosture posture)
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

    public static int ReplacementPriority(
        ConflictFactionOperationalPosture posture,
        GroundUnitRole role) =>
        posture switch
        {
            ConflictFactionOperationalPosture.Defensive => 0,
            ConflictFactionOperationalPosture.Aggressive => role switch
            {
                GroundUnitRole.Armor => 0,
                GroundUnitRole.Infantry => 1,
                GroundUnitRole.Command => 2,
                GroundUnitRole.AirDefense => 3,
                GroundUnitRole.Logistics => 4,
                _ => 5
            },
            ConflictFactionOperationalPosture.LogisticsFocused => role switch
            {
                GroundUnitRole.Logistics => 0,
                GroundUnitRole.Command => 1,
                GroundUnitRole.Infantry => 2,
                GroundUnitRole.AirDefense => 3,
                GroundUnitRole.Armor => 4,
                _ => 5
            },
            ConflictFactionOperationalPosture.AirFocused => role switch
            {
                GroundUnitRole.AirDefense => 0,
                GroundUnitRole.Command => 1,
                GroundUnitRole.Logistics => 2,
                GroundUnitRole.Armor => 3,
                GroundUnitRole.Infantry => 4,
                _ => 5
            },
            _ => throw new ArgumentOutOfRangeException(nameof(posture))
        };

    public static SupportUrgency Promote(
        SupportUrgency urgency) =>
        urgency switch
        {
            SupportUrgency.Routine => SupportUrgency.Priority,
            SupportUrgency.Priority => SupportUrgency.Immediate,
            _ => SupportUrgency.Immediate
        };
}
