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
    double MaximumTouchdownApproachDescentFeetPerMinute = 0,
    int TakeoffCount = 0,
    int BounceCount = 0,
    DateTimeOffset? TouchdownAt = null,
    FlightSessionStatus? FinalizationStatus = null,
    int AcceptedObservationCount = 0,
    DateTimeOffset? LastAcceptedObservationAt = null);

public sealed record FlightAirframeDecision(
    string EvidenceReason,
    DateTimeOffset? DescentSampleAt,
    DateTimeOffset? CorrelatedTouchdownAt,
    double? CorrelationAgeSeconds,
    bool DescentWithinCorrelationWindow,
    double CorrelationWindowSeconds,
    double ElevatedThresholdFpm,
    double MinorDamageThresholdFpm,
    double MajorDamageThresholdFpm,
    double RoutineAirframeWearPercent,
    double RoutineEngineWearPercent,
    double LandingCycleWearPercent,
    double AdditionalLandingGearWearPercent,
    double TouchdownDamagePercent,
    double CrashDamagePercent);

public sealed record FlightAirframeApplication(
    DateTimeOffset AppliedAt,
    AircraftMaintenanceState? Before,
    AircraftMaintenanceState? After);

public sealed record FlightAirframeConsequence(
    FlightAirframeSummary Summary,
    FlightDamageSeverity Severity,
    MaintenanceUsage Usage,
    FlightAirframeDecision? Decision = null,
    FlightAirframeApplication? Application = null);

public static class FlightAirframeConsequenceCalculator
{
    // Descent is approach evidence, not measured impact force.
    public static FlightAirframeConsequence Calculate(
        FlightSession session,
        FlightAirframeCalibration? aircraftCalibration = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsTerminal || session.AircraftIdentity is null)
            throw new InvalidOperationException("Only finalized flights with known airframes can accrue consequences.");
        session.AircraftIdentity.Validate();
        FlightAirframeCalibration calibration = aircraftCalibration
            ?? FlightAirframeCalibration.Conservative;
        calibration.Validate();

        FlightSessionStatistics statistics = session.EffectiveStatistics;
        double descent = statistics.MaximumTouchdownApproachDescentFeetPerMinute;
        int landings = session.Tracking.LandingEpisodeCount;
        FlightDamageSeverity severity = session.Tracking.CrashReported
            ? FlightDamageSeverity.Severe
            : landings == 0 || descent < calibration.ElevatedDescentFpm
                ? FlightDamageSeverity.Normal
                : descent < calibration.MinorDamageDescentFpm
                    ? FlightDamageSeverity.ElevatedWear
                    : descent < calibration.MajorDamageDescentFpm
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
            descent,
            session.Tracking.TakeoffCount,
            session.Tracking.BounceCount,
            session.EffectiveLandingEpisodes.LastOrDefault()?.TouchdownAt,
            session.Status,
            statistics.AcceptedObservationCount,
            statistics.LastAcceptedObservationAt);

        var usage = new MaintenanceUsage(
            AirframeHours: Math.Max(0, summary.AirborneHours),
            EngineHours: Math.Max(0, summary.BlockHours),
            LandingCycles: landings,
            OverspeedMinutes: 0, EngineStressMinutes: 0,
            HardLandingSeverity: severity switch
            {
                FlightDamageSeverity.MinorDamage => calibration.MinorLandingWearSeverity,
                FlightDamageSeverity.MajorDamage => calibration.MajorLandingWearSeverity,
                _ => 0
            },
            MaximumPositiveG: 0, ExcessGSeconds: 0,
            AdditionalDamagePercent: severity == FlightDamageSeverity.Severe
                ? calibration.CrashDamagePercent : 0,
            AdditionalGearWearPercent: severity == FlightDamageSeverity.ElevatedWear
                ? calibration.ElevatedLandingWearSeverity
                    * InitialMaintenancePrograms.LightAircraftFallback.HardLandingGearWearAtSeverityOne
                : 0);
        usage.Validate();
        MaintenanceProgram program = InitialMaintenancePrograms.LightAircraftFallback;
        var decision = new FlightAirframeDecision(
            session.Tracking.CrashReported ? "Simulator crash report"
                : statistics.CorrelatedTouchdownAt is null && descent > 0
                    ? "Legacy touchdown correlation; sample timestamp unavailable"
                : statistics.CorrelatedTouchdownAt is null ? "No correlated touchdown descent"
                : "Near-ground descent correlated with touchdown; not measured impact force",
            statistics.TouchdownDescentSampleAt,
            statistics.CorrelatedTouchdownAt,
            statistics.TouchdownDescentSampleAt is { } sample
                && statistics.CorrelatedTouchdownAt is { } contact
                ? (contact - sample).TotalSeconds : null,
            statistics.TouchdownDescentSampleAt is { } correlatedSample
                && statistics.CorrelatedTouchdownAt is { } correlatedContact
                && correlatedContact >= correlatedSample
                && correlatedContact - correlatedSample <= FlightAirframeCalibration.TouchdownCorrelationWindow,
            FlightAirframeCalibration.TouchdownCorrelationWindow.TotalSeconds,
            calibration.ElevatedDescentFpm,
            calibration.MinorDamageDescentFpm,
            calibration.MajorDamageDescentFpm,
            usage.AirframeHours * program.AirframeWearPercentPerHour,
            usage.EngineHours * program.EngineWearPercentPerHour,
            usage.LandingCycles * program.GearWearPercentPerLanding,
            usage.HardLandingSeverity * program.HardLandingGearWearAtSeverityOne
                + usage.AdditionalGearWearPercent,
            usage.HardLandingSeverity * program.HardLandingDamageAtSeverityOne,
            usage.AdditionalDamagePercent);
        return new(summary, severity, usage, decision);
    }
}
