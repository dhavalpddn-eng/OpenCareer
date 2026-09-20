namespace OpenCareer.Domain.Careers;

public static class CareerSessionPolicy
{
    public static TimeSpan PreferredMinimum => TimeSpan.FromHours(1);
    public static TimeSpan PreferredMaximum => TimeSpan.FromHours(3);
    public static TimeSpan MaximumOfferedDuration => TimeSpan.FromHours(6);

    public static bool FitsJobDuration(TimeSpan duration) =>
        duration > TimeSpan.Zero && duration <= MaximumOfferedDuration;

    // Short local utility work remains possible; preferences rank jobs rather than forbidding them.
    public static bool IsPreferredDuration(TimeSpan duration) =>
        duration >= PreferredMinimum && duration <= PreferredMaximum;
}
