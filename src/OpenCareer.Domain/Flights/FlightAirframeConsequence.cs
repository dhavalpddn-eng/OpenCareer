using OpenCareer.Domain.Maintenance;

namespace OpenCareer.Domain.Flights;

public enum FlightDamageSeverity
{
    Normal,
    ElevatedWear,
    MinorDamage,
    MajorDamage,
    Severe
}

public sealed record FlightAirframeSummary(
    Guid SessionId,
    Guid? ContractId,
    FlightSessionAircraftIdentity Aircraft,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string? PlannedOrigin,
    string? PlannedDestination,
    double AirborneHours,
    double BlockHours,
    int LandingCycles,
    double DistanceNauticalMiles,
    double FuelBurnedPounds,
    double MaximumAltitudeFeet,
    double MaximumIndicatedAirspeedKnots,
    double MaximumNearGroundDescentFeetPerMinute,
    bool CrashReported,
    double? FinalLatitudeDegrees = null,
    double? FinalLongitudeDegrees = null,
    double? StartFuelPounds = null,
    double? EndFuelPounds = null,
    double MaximumTouchdownApproachDescentFeetPerMinute = 0);

public sealed record FlightAirframeConsequence(
    FlightAirframeSummary Summary,
    FlightDamageSeverity Severity,
    MaintenanceUsage Usage);

public static class FlightAirframeConsequenceCalculator
{
    // An approach descent is a warning signal, not a measured touchdown impact.
    // The higher bands are deliberately conservative until impact evidence exists.
    public const double ElevatedDescentFpm = 900;
    public const double MinorDescentFpm = 1300;
    public const double MajorDescentFpm = 1800;

    public static FlightAirframeConsequence Calculate(FlightSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsTerminal || session.AircraftIdentity is null)
            throw new InvalidOperationException("Only finalized flights with known airframes can accrue consequences.");
        session.AircraftIdentity.Validate();

        FlightSessionStatistics statistics = session.EffectiveStatistics;
        double descent = statistics.MaximumTouchdownApproachDescentFeetPerMinute;
        int landings = session.Tracking.LandingEpisodeCount;
        FlightDamageSeverity severity = session.Tracking.CrashReported
            ? FlightDamageSeverity.Severe
            : landings == 0 || descent < ElevatedDescentFpm
                ? FlightDamageSeverity.Normal
                : descent < MinorDescentFpm
                    ? FlightDamageSeverity.ElevatedWear
                    : descent < MajorDescentFpm
                        ? FlightDamageSeverity.MinorDamage
                        : FlightDamageSeverity.MajorDamage;

        var summary = new FlightAirframeSummary(
            session.SessionId, session.ContractId, session.AircraftIdentity,
            session.CreatedAt, session.UpdatedAt,
            session.Plan?.PlannedOrigin, session.Plan?.PlannedDestination,
            session.TimeLedger.AirborneTime.TotalHours,
            session.TimeLedger.BlockTime.TotalHours,
            landings, statistics.DistanceNauticalMiles,
            statistics.FuelBurnedPounds, statistics.MaximumAltitudeMslFeet,
            statistics.MaximumIndicatedAirspeedKnots,
            statistics.MaximumNearGroundDescentFeetPerMinute,
            session.Tracking.CrashReported,
            session.ContinuityAnchor?.LatitudeDegrees,
            session.ContinuityAnchor?.LongitudeDegrees,
            statistics.StartFuelPounds, statistics.LastFuelPounds,
            descent);

        var usage = new MaintenanceUsage(
            AirframeHours: Math.Max(0, summary.AirborneHours),
            EngineHours: Math.Max(0, summary.BlockHours),
            LandingCycles: landings,
            OverspeedMinutes: 0, EngineStressMinutes: 0,
            HardLandingSeverity: severity switch
            {
                FlightDamageSeverity.ElevatedWear => 0.02,
                FlightDamageSeverity.MinorDamage => 0.10,
                FlightDamageSeverity.MajorDamage => 0.55,
                _ => 0
            },
            MaximumPositiveG: 0, ExcessGSeconds: 0,
            AdditionalDamagePercent: severity == FlightDamageSeverity.Severe ? 45 : 0);
        usage.Validate();
        return new(summary, severity, usage);
    }
}
