namespace OpenCareer.Domain.Economy;

public sealed record OfflineLiabilityPolicy(
    TimeSpan GracePeriod,
    TimeSpan SoloAccrualCap,
    TimeSpan StaffedAccrualCap)
{
    public static OfflineLiabilityPolicy Default { get; } = new(
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero);

    public OfflineLiabilityAssessment Assess(
        DateTimeOffset lastActiveAt,
        DateTimeOffset currentTime,
        decimal monthlyFixedLiabilities,
        bool hasPassiveOperations)
    {
        if (GracePeriod < TimeSpan.Zero || SoloAccrualCap < TimeSpan.Zero || StaffedAccrualCap < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(GracePeriod));
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
        var accrued = monthlyFixedLiabilities * (decimal)billable.Ticks / (30m * TimeSpan.TicksPerDay);
        var protectedTime = elapsed - billable;
        if (protectedTime < TimeSpan.Zero) protectedTime = TimeSpan.Zero;

        return new OfflineLiabilityAssessment(elapsed, billable, protectedTime, decimal.Round(accrued, 2));
    }
}

public sealed record OfflineLiabilityAssessment(
    TimeSpan Elapsed,
    TimeSpan BillableDuration,
    TimeSpan ProtectedDuration,
    decimal AccruedFixedLiabilities);
