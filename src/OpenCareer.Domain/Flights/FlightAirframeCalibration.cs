namespace OpenCareer.Domain.Flights;

public sealed record FlightAirframeCalibration(
    double ElevatedDescentFpm,
    double MinorDamageDescentFpm,
    double MajorDamageDescentFpm,
    double ElevatedLandingWearSeverity,
    double MinorLandingWearSeverity,
    double MajorLandingWearSeverity,
    double CrashDamagePercent)
{
    // Common evidence-correlation bounds; aircraft calibration only changes the consequence bands.
    public const double NearGroundCeilingFeet = 100;
    public static TimeSpan TouchdownCorrelationWindow { get; } = TimeSpan.FromSeconds(20);

    public static FlightAirframeCalibration Conservative { get; } = new(
        ElevatedDescentFpm: 900,
        MinorDamageDescentFpm: 1300,
        MajorDamageDescentFpm: 1800,
        ElevatedLandingWearSeverity: 0.02,
        MinorLandingWearSeverity: 0.10,
        MajorLandingWearSeverity: 0.55,
        CrashDamagePercent: 45);

    public void Validate()
    {
        if (!double.IsFinite(ElevatedDescentFpm) || ElevatedDescentFpm <= 0
            || !double.IsFinite(MinorDamageDescentFpm) || MinorDamageDescentFpm <= ElevatedDescentFpm
            || !double.IsFinite(MajorDamageDescentFpm) || MajorDamageDescentFpm <= MinorDamageDescentFpm
            || !double.IsFinite(ElevatedLandingWearSeverity) || ElevatedLandingWearSeverity is < 0 or > 1
            || !double.IsFinite(MinorLandingWearSeverity) || MinorLandingWearSeverity is < 0 or > 1
            || !double.IsFinite(MajorLandingWearSeverity) || MajorLandingWearSeverity is < 0 or > 1
            || ElevatedLandingWearSeverity > MinorLandingWearSeverity
            || MinorLandingWearSeverity > MajorLandingWearSeverity
            || !double.IsFinite(CrashDamagePercent) || CrashDamagePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(FlightAirframeCalibration));
    }
}
