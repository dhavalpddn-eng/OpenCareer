namespace OpenCareer.Domain.Economy;

public sealed record OfflineLiabilityPolicy(
    TimeSpan GracePeriod,
    TimeSpan SoloAccrualCap,
    TimeSpan StaffedAccrualCap)
{
    public static OfflineLiabilityPolicy Default { get; } = new(
        TimeSpan.FromDays(3),
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(90));

    public OfflineLiabilityAssessment Assess(
        DateTimeOffset lastActiveAt,
        DateTimeOffset currentTime,
        decimal monthlyFixedLiabilities,
        bool hasPassiveOperations)
    {
        if (currentTime < lastActiveAt)
            throw new ArgumentOutOfRangeException(nameof(currentTime));
        if (monthlyFixedLiabilities < 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyFixedLiabilities));

        var elapsed = currentTime - lastActiveAt;
        var cap = hasPassiveOperations ? StaffedAccrualCap : SoloAccrualCap;
        var billable = elapsed <= GracePeriod
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Min((elapsed - GracePeriod).Ticks, cap.Ticks));

        // A 30-day accounting month keeps offline settlement deterministic.
        var accrued = monthlyFixedLiabilities * (decimal)(billable.TotalDays / 30d);
        var protectedTime = elapsed - GracePeriod - billable;
        if (protectedTime < TimeSpan.Zero) protectedTime = TimeSpan.Zero;

        return new OfflineLiabilityAssessment(elapsed, billable, protectedTime, decimal.Round(accrued, 2));
    }
}

public sealed record OfflineLiabilityAssessment(
    TimeSpan Elapsed,
    TimeSpan BillableDuration,
    TimeSpan ProtectedDuration,
    decimal AccruedFixedLiabilities);
