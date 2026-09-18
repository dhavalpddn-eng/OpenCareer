namespace OpenCareer.Domain.Maintenance;

public enum MaintenanceDataConfidence
{
    ConservativeFallback,
    AircraftConfiguration,
    VerifiedReference
}

public sealed record MaintenanceProgram(
    string Id,
    MaintenanceDataConfidence Confidence,
    double InspectionIntervalHours,
    double AirframeWearPercentPerHour,
    double EngineWearPercentPerHour,
    double GearWearPercentPerLanding,
    double OverspeedWearPercentPerMinute,
    double EngineStressWearPercentPerMinute,
    double HardLandingGearWearAtSeverityOne,
    double HardLandingDamageAtSeverityOne,
    double PositiveGLimit,
    double ExcessGDamagePercentPerSecondPerG,
    decimal BaseInspectionCost,
    decimal CostPerWearPercent,
    decimal CostPerDamagePercent,
    TimeSpan BaseDowntime,
    TimeSpan DowntimePerDamagePercent,
    double GroundingWearThresholdPercent,
    double GroundingDamageThresholdPercent)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);

        if (!Enum.IsDefined(Confidence)
            || !double.IsFinite(InspectionIntervalHours) || InspectionIntervalHours <= 0
            || !double.IsFinite(AirframeWearPercentPerHour) || AirframeWearPercentPerHour < 0
            || !double.IsFinite(EngineWearPercentPerHour) || EngineWearPercentPerHour < 0
            || !double.IsFinite(GearWearPercentPerLanding) || GearWearPercentPerLanding < 0
            || !double.IsFinite(OverspeedWearPercentPerMinute) || OverspeedWearPercentPerMinute < 0
            || !double.IsFinite(EngineStressWearPercentPerMinute) || EngineStressWearPercentPerMinute < 0
            || !double.IsFinite(HardLandingGearWearAtSeverityOne) || HardLandingGearWearAtSeverityOne < 0
            || !double.IsFinite(HardLandingDamageAtSeverityOne) || HardLandingDamageAtSeverityOne < 0
            || !double.IsFinite(PositiveGLimit) || PositiveGLimit <= 0
            || !double.IsFinite(ExcessGDamagePercentPerSecondPerG) || ExcessGDamagePercentPerSecondPerG < 0
            || BaseInspectionCost < 0 || CostPerWearPercent < 0 || CostPerDamagePercent < 0
            || BaseDowntime < TimeSpan.Zero || DowntimePerDamagePercent < TimeSpan.Zero
            || !double.IsFinite(GroundingWearThresholdPercent) || GroundingWearThresholdPercent is <= 0 or > 100
            || !double.IsFinite(GroundingDamageThresholdPercent) || GroundingDamageThresholdPercent is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MaintenanceProgram));
        }
    }
}

public sealed record MaintenanceUsage(
    double AirframeHours,
    double EngineHours,
    int LandingCycles,
    double OverspeedMinutes,
    double EngineStressMinutes,
    double HardLandingSeverity,
    double MaximumPositiveG,
    double ExcessGSeconds)
{
    public void Validate()
    {
        if (!double.IsFinite(AirframeHours) || AirframeHours < 0
            || !double.IsFinite(EngineHours) || EngineHours < 0
            || LandingCycles < 0
            || !double.IsFinite(OverspeedMinutes) || OverspeedMinutes < 0
            || !double.IsFinite(EngineStressMinutes) || EngineStressMinutes < 0
            || !double.IsFinite(HardLandingSeverity) || HardLandingSeverity is < 0 or > 1
            || !double.IsFinite(MaximumPositiveG) || MaximumPositiveG < 0
            || !double.IsFinite(ExcessGSeconds) || ExcessGSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaintenanceUsage));
        }
    }
}

public sealed record AircraftMaintenanceState(
    string OwnershipId,
    double TrackedAirframeHours,
    double TrackedEngineHours,
    int LandingCycles,
    double AirframeWearPercent,
    double EngineWearPercent,
    double GearWearPercent,
    double DamagePercent,
    double NextInspectionDueAtTrackedHours,
    MaintenanceDataConfidence Confidence,
    DateTimeOffset UpdatedAt)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        if (!double.IsFinite(TrackedAirframeHours) || TrackedAirframeHours < 0
            || !double.IsFinite(TrackedEngineHours) || TrackedEngineHours < 0
            || LandingCycles < 0
            || !ValidPercent(AirframeWearPercent)
            || !ValidPercent(EngineWearPercent)
            || !ValidPercent(GearWearPercent)
            || !ValidPercent(DamagePercent)
            || !double.IsFinite(NextInspectionDueAtTrackedHours) || NextInspectionDueAtTrackedHours < 0
            || !Enum.IsDefined(Confidence))
        {
            throw new ArgumentException("Invalid maintenance state.");
        }
    }

    public bool IsGrounded(MaintenanceProgram program)
    {
        program.Validate();
        Validate();
        return DamagePercent >= program.GroundingDamageThresholdPercent
            || AirframeWearPercent >= program.GroundingWearThresholdPercent
            || EngineWearPercent >= program.GroundingWearThresholdPercent
            || GearWearPercent >= program.GroundingWearThresholdPercent
            || TrackedAirframeHours >= NextInspectionDueAtTrackedHours;
    }

    private static bool ValidPercent(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 100;
}

public sealed record MaintenanceServiceQuote(
    string OwnershipId,
    decimal Cost,
    TimeSpan Downtime,
    bool InspectionDue,
    bool GroundingCondition,
    DateTimeOffset QuotedAt);

public static class AircraftMaintenanceEngine
{
    public static AircraftMaintenanceState CreateInitial(
        string ownershipId,
        decimal acquiredConditionPercent,
        MaintenanceProgram program,
        DateTimeOffset at)
    {
        program.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        if (acquiredConditionPercent is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(acquiredConditionPercent));

        var inheritedWear = 100d - (double)acquiredConditionPercent;
        var state = new AircraftMaintenanceState(
            ownershipId,
            0,
            0,
            0,
            inheritedWear,
            inheritedWear,
            inheritedWear,
            0,
            program.InspectionIntervalHours,
            program.Confidence,
            at);
        state.Validate();
        return state;
    }

    public static AircraftMaintenanceState ApplyUsage(
        AircraftMaintenanceState state,
        MaintenanceProgram program,
        MaintenanceUsage usage,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(usage);
        state.Validate();
        program.Validate();
        usage.Validate();

        if (at < state.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(at));

        var excessG = Math.Max(0, usage.MaximumPositiveG - program.PositiveGLimit);
        var gDamage = excessG * usage.ExcessGSeconds * program.ExcessGDamagePercentPerSecondPerG;

        var next = state with
        {
            TrackedAirframeHours = state.TrackedAirframeHours + usage.AirframeHours,
            TrackedEngineHours = state.TrackedEngineHours + usage.EngineHours,
            LandingCycles = checked(state.LandingCycles + usage.LandingCycles),
            AirframeWearPercent = Clamp(state.AirframeWearPercent
                + usage.AirframeHours * program.AirframeWearPercentPerHour
                + usage.OverspeedMinutes * program.OverspeedWearPercentPerMinute),
            EngineWearPercent = Clamp(state.EngineWearPercent
                + usage.EngineHours * program.EngineWearPercentPerHour
                + usage.EngineStressMinutes * program.EngineStressWearPercentPerMinute),
            GearWearPercent = Clamp(state.GearWearPercent
                + usage.LandingCycles * program.GearWearPercentPerLanding
                + usage.HardLandingSeverity * program.HardLandingGearWearAtSeverityOne),
            DamagePercent = Clamp(state.DamagePercent
                + usage.HardLandingSeverity * program.HardLandingDamageAtSeverityOne
                + gDamage),
            UpdatedAt = at
        };
        next.Validate();
        return next;
    }

    public static MaintenanceServiceQuote QuoteService(
        AircraftMaintenanceState state,
        MaintenanceProgram program,
        DateTimeOffset quotedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(program);
        state.Validate();
        program.Validate();

        var inspectionDue = state.TrackedAirframeHours >= state.NextInspectionDueAtTrackedHours;
        var wearTotal = state.AirframeWearPercent + state.EngineWearPercent + state.GearWearPercent;
        var cost = program.BaseInspectionCost
            + (decimal)wearTotal * program.CostPerWearPercent
            + (decimal)state.DamagePercent * program.CostPerDamagePercent;

        var downtime = program.BaseDowntime
            + Scale(program.DowntimePerDamagePercent, state.DamagePercent);

        return new MaintenanceServiceQuote(
            state.OwnershipId,
            decimal.Round(cost, 2),
            downtime,
            inspectionDue,
            state.IsGrounded(program),
            quotedAt);
    }

    public static AircraftMaintenanceState CompleteService(
        AircraftMaintenanceState state,
        MaintenanceProgram program,
        MaintenanceServiceQuote quote,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(quote);
        state.Validate();
        program.Validate();

        if (quote.OwnershipId != state.OwnershipId
            || quote.QuotedAt < state.UpdatedAt
            || completedAt < quote.QuotedAt
            || completedAt < state.UpdatedAt)
            throw new InvalidOperationException("Maintenance quote does not match current aircraft state.");

        var next = state with
        {
            AirframeWearPercent = 0,
            EngineWearPercent = 0,
            GearWearPercent = 0,
            DamagePercent = 0,
            NextInspectionDueAtTrackedHours = state.TrackedAirframeHours + program.InspectionIntervalHours,
            Confidence = program.Confidence,
            UpdatedAt = completedAt
        };
        next.Validate();
        return next;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);

    private static TimeSpan Scale(TimeSpan duration, double factor)
    {
        var ticks = duration.Ticks * factor;
        if (!double.IsFinite(ticks) || ticks > long.MaxValue)
            throw new OverflowException("Maintenance downtime exceeds TimeSpan capacity.");
        return TimeSpan.FromTicks((long)Math.Round(ticks, MidpointRounding.AwayFromZero));
    }
}

public static class InitialMaintenancePrograms
{
    // Conservative gameplay fallbacks only. Aircraft-specific verified programs should replace them when available.
    public static MaintenanceProgram LightAircraftFallback { get; } = new(
        "fallback-light",
        MaintenanceDataConfidence.ConservativeFallback,
        InspectionIntervalHours: 50,
        AirframeWearPercentPerHour: 0.08,
        EngineWearPercentPerHour: 0.12,
        GearWearPercentPerLanding: 0.05,
        OverspeedWearPercentPerMinute: 0.10,
        EngineStressWearPercentPerMinute: 0.15,
        HardLandingGearWearAtSeverityOne: 4.0,
        HardLandingDamageAtSeverityOne: 8.0,
        PositiveGLimit: 3.8,
        ExcessGDamagePercentPerSecondPerG: 0.03,
        BaseInspectionCost: 350m,
        CostPerWearPercent: 18m,
        CostPerDamagePercent: 75m,
        BaseDowntime: TimeSpan.FromHours(3),
        DowntimePerDamagePercent: TimeSpan.FromMinutes(12),
        GroundingWearThresholdPercent: 85,
        GroundingDamageThresholdPercent: 20);
}
