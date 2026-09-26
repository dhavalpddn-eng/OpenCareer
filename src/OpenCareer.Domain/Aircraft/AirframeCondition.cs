namespace OpenCareer.Domain.Aircraft;

/// <summary>Discrete unresolved damage; grounding is an explicit assessment, not a wear threshold.</summary>
public enum AirframeDamageState
{
    None = 0,
    Recorded = 1,
    Grounding = 2
}

/// <summary>
/// Structural wear and discrete damage are independent. No accumulation formula, service limit,
/// component simulation, or simulator-derived condition is implied by this baseline.
/// </summary>
public sealed record AirframeCondition
{
    public AirframeCondition(double wearFraction, AirframeDamageState damage)
    {
        if (!double.IsFinite(wearFraction) || wearFraction is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(wearFraction));
        if (!Enum.IsDefined(damage))
            throw new ArgumentOutOfRangeException(nameof(damage));

        WearFraction = wearFraction;
        Damage = damage;
    }

    // Zero means no recorded wear; one is the top of this normalized scale, not a service interval.
    public double WearFraction { get; }
    public AirframeDamageState Damage { get; }
    public bool HasDamage => Damage != AirframeDamageState.None;
    // False is not an airworthiness/dispatch authorization. Other future assessments may also ground an airframe.
    public bool RequiresGrounding => Damage == AirframeDamageState.Grounding;
}
