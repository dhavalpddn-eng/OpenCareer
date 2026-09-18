namespace OpenCareer.Domain.Careers;

/// <summary>
/// Durable career evidence used to derive a meta-progression level. The level summarizes experience;
/// it never replaces licenses, ratings, aircraft capability, employer trust, authorization or money.
/// </summary>
public sealed record CareerProgressEvidence(
    double VerifiedFlightHours,
    int CompletedContracts,
    int EstablishedRouteMilestones,
    int EmployerTrustMilestones,
    int EarnedQualifications)
{
    public void Validate()
    {
        if (!double.IsFinite(VerifiedFlightHours) || VerifiedFlightHours < 0)
            throw new ArgumentOutOfRangeException(nameof(VerifiedFlightHours));
        if (CompletedContracts < 0)
            throw new ArgumentOutOfRangeException(nameof(CompletedContracts));
        if (EstablishedRouteMilestones < 0)
            throw new ArgumentOutOfRangeException(nameof(EstablishedRouteMilestones));
        if (EmployerTrustMilestones < 0)
            throw new ArgumentOutOfRangeException(nameof(EmployerTrustMilestones));
        if (EarnedQualifications < 0)
            throw new ArgumentOutOfRangeException(nameof(EarnedQualifications));
    }
}

public sealed record CareerLevelSnapshot(int Level, long MeritPoints, long NextLevelAt)
{
    public static CareerLevelSnapshot Starting { get; } = new(1, 0, 80);
}

/// <summary>
/// Meaningful meta-progression. Points are derived from verified career evidence rather than arbitrary
/// button presses, and level only controls convenience/visibility features such as job-board breadth.
/// </summary>
public sealed record CareerLevelPolicy(
    int MaximumLevel,
    double PointsPerFlightHour,
    long PointsPerCompletedContract,
    long PointsPerEstablishedRouteMilestone,
    long PointsPerEmployerTrustMilestone,
    long PointsPerEarnedQualification,
    double LevelCurveCoefficient)
{
    public static CareerLevelPolicy Default { get; } = new(
        MaximumLevel: 50,
        PointsPerFlightHour: 100,
        PointsPerCompletedContract: 75,
        PointsPerEstablishedRouteMilestone: 200,
        PointsPerEmployerTrustMilestone: 250,
        PointsPerEarnedQualification: 300,
        LevelCurveCoefficient: 80);

    public CareerLevelSnapshot Evaluate(CareerProgressEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Validate();
        evidence.Validate();

        var flightPoints = evidence.VerifiedFlightHours * PointsPerFlightHour;
        if (!double.IsFinite(flightPoints) || flightPoints > long.MaxValue)
            throw new OverflowException("Career flight-hour merit exceeds the supported range.");

        var points = checked(
            (long)Math.Floor(flightPoints)
            + (long)evidence.CompletedContracts * PointsPerCompletedContract
            + (long)evidence.EstablishedRouteMilestones * PointsPerEstablishedRouteMilestone
            + (long)evidence.EmployerTrustMilestones * PointsPerEmployerTrustMilestone
            + (long)evidence.EarnedQualifications * PointsPerEarnedQualification);

        var rawLevel = 1 + (int)Math.Floor(Math.Sqrt(points / LevelCurveCoefficient));
        var level = Math.Clamp(rawLevel, 1, MaximumLevel);
        var nextLevelAt = level >= MaximumLevel ? long.MaxValue : ThresholdForLevel(level + 1);
        return new CareerLevelSnapshot(level, points, nextLevelAt);
    }

    public long ThresholdForLevel(int level)
    {
        Validate();
        if (level < 1 || level > MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));

        var n = level - 1.0;
        var threshold = LevelCurveCoefficient * n * n;
        if (!double.IsFinite(threshold) || threshold > long.MaxValue)
            throw new OverflowException("Career level threshold exceeds the supported range.");
        return (long)Math.Ceiling(threshold);
    }

    public void Validate()
    {
        if (MaximumLevel is < 2 or > 500)
            throw new ArgumentOutOfRangeException(nameof(MaximumLevel));
        if (!double.IsFinite(PointsPerFlightHour) || PointsPerFlightHour <= 0)
            throw new ArgumentOutOfRangeException(nameof(PointsPerFlightHour));
        if (PointsPerCompletedContract < 0 || PointsPerEstablishedRouteMilestone < 0
            || PointsPerEmployerTrustMilestone < 0 || PointsPerEarnedQualification < 0)
            throw new ArgumentOutOfRangeException(nameof(PointsPerCompletedContract));
        if (!double.IsFinite(LevelCurveCoefficient) || LevelCurveCoefficient <= 0)
            throw new ArgumentOutOfRangeException(nameof(LevelCurveCoefficient));
    }
}
