namespace OpenCareer.Application.Dashboard;

public sealed record OpportunityTierSignals(
    double Rarity,
    double QualificationIntensity,
    double OperationalComplexity,
    double Urgency,
    double ReputationValue,
    double RewardPremium)
{
    public void Validate()
    {
        ValidateSignal(Rarity, nameof(Rarity));
        ValidateSignal(QualificationIntensity, nameof(QualificationIntensity));
        ValidateSignal(OperationalComplexity, nameof(OperationalComplexity));
        ValidateSignal(Urgency, nameof(Urgency));
        ValidateSignal(ReputationValue, nameof(ReputationValue));
        ValidateSignal(RewardPremium, nameof(RewardPremium));
    }

    private static void ValidateSignal(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed class OpportunityTierPolicy
{
    public static OpportunityTierPolicy Default { get; } = new();

    public OpportunityTier Classify(OpportunityTierSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        signals.Validate();

        double score = CalculateScore(signals);

        bool legendaryGate =
            score >= 85 &&
            signals.Rarity >= 75 &&
            Math.Max(
                signals.QualificationIntensity,
                Math.Max(signals.OperationalComplexity, signals.Urgency)) >= 80;

        if (legendaryGate)
            return OpportunityTier.Legendary;

        if (score >= 70)
            return OpportunityTier.Elite;

        if (score >= 50)
            return OpportunityTier.Specialist;

        return OpportunityTier.Standard;
    }

    public double CalculateScore(OpportunityTierSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        signals.Validate();

        return
            (signals.Rarity * 0.25) +
            (signals.QualificationIntensity * 0.20) +
            (signals.OperationalComplexity * 0.20) +
            (signals.Urgency * 0.15) +
            (signals.ReputationValue * 0.10) +
            (signals.RewardPremium * 0.10);
    }
}
