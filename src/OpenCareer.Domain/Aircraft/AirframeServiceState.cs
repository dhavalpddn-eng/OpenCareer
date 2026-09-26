using System.Text.Json.Serialization;

namespace OpenCareer.Domain.Aircraft;

/// <summary>Provisional OpenCareer gameplay fallback, not an FAA/POH maintenance requirement.</summary>
public static class LightAircraftRoutineInspectionV1
{
    public const string ScheduleId = "LightAircraftRoutineInspection";
    public const int Version = 1;
    // Historical InitialMaintenancePrograms.LightAircraftFallback, commit 44781b5: 50 hours.
    public static TimeSpan IntervalAirborneTime { get; } = TimeSpan.FromHours(50);
}

public enum AirframeUsageOrigin { TrackingFromCreation = 1, TrackingFromMigrationBaseline = 2 }
public enum AirframeInspectionStatus { Current, InspectionDue }

/// <summary>Exact tracked usage and inspection markers, independent of wear and discrete damage.</summary>
public sealed record AirframeServiceState(
    [property: JsonRequired] AirframeId AirframeId,
    [property: JsonRequired] string ScheduleId,
    [property: JsonRequired] int ScheduleVersion,
    [property: JsonRequired] TimeSpan TotalTrackedAirborneTime,
    [property: JsonRequired] TimeSpan LastInspectionAtTrackedAirborneTime,
    [property: JsonRequired] TimeSpan NextInspectionDueAtTrackedAirborneTime,
    [property: JsonRequired] AirframeUsageOrigin UsageOrigin,
    [property: JsonRequired] long Revision,
    [property: JsonRequired] DateTimeOffset UpdatedAt,
    // Optional only for retained schema-17/v2 inspection JSON. Absence establishes the
    // schema-18 migration baseline rather than inventing complete historical cycle tracking.
    long TotalTrackedLandingCycles = 0,
    AirframeUsageOrigin LandingCycleOrigin = AirframeUsageOrigin.TrackingFromMigrationBaseline)
{
    [JsonIgnore] public AirframeInspectionStatus InspectionStatus => TotalTrackedAirborneTime >= NextInspectionDueAtTrackedAirborneTime
        ? AirframeInspectionStatus.InspectionDue : AirframeInspectionStatus.Current;
    [JsonIgnore] public TimeSpan TimeUntilInspection => TotalTrackedAirborneTime >= NextInspectionDueAtTrackedAirborneTime
        ? TimeSpan.Zero : NextInspectionDueAtTrackedAirborneTime - TotalTrackedAirborneTime;

    public static AirframeServiceState Initial(AirframeId id, DateTimeOffset at, AirframeUsageOrigin origin) =>
        new(id, LightAircraftRoutineInspectionV1.ScheduleId, LightAircraftRoutineInspectionV1.Version,
            TimeSpan.Zero, TimeSpan.Zero, LightAircraftRoutineInspectionV1.IntervalAirborneTime, origin, 1,
            at.ToUniversalTime(), 0, origin);

    public void Validate()
    {
        AirframeId.Validate();
        if (ScheduleId != LightAircraftRoutineInspectionV1.ScheduleId || ScheduleVersion != LightAircraftRoutineInspectionV1.Version)
            throw new NotSupportedException("Unsupported routine inspection schedule/version.");
        if (TotalTrackedAirborneTime < TimeSpan.Zero || LastInspectionAtTrackedAirborneTime < TimeSpan.Zero
            || LastInspectionAtTrackedAirborneTime > TotalTrackedAirborneTime
            || NextInspectionDueAtTrackedAirborneTime != LastInspectionAtTrackedAirborneTime + LightAircraftRoutineInspectionV1.IntervalAirborneTime
            || TotalTrackedLandingCycles < 0 || !Enum.IsDefined(UsageOrigin) || !Enum.IsDefined(LandingCycleOrigin)
            || Revision < 1 || UpdatedAt == default)
            throw new InvalidDataException("Invalid airframe usage/inspection state.");
    }

    public AirframeServiceState AddTrustedUsage(TimeSpan airborneTime, int landingCycles, DateTimeOffset at)
    {
        Validate();
        if (airborneTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(airborneTime));
        if (landingCycles < 0) throw new ArgumentOutOfRangeException(nameof(landingCycles));
        if (at < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(at), "Usage save time cannot move backwards.");
        var result = this with { TotalTrackedAirborneTime = TotalTrackedAirborneTime + airborneTime,
            TotalTrackedLandingCycles = checked(TotalTrackedLandingCycles + landingCycles),
            Revision = checked(Revision + 1), UpdatedAt = at.ToUniversalTime() };
        result.Validate();
        return result;
    }

    public AirframeServiceState AddTrustedUsage(TimeSpan airborneTime, DateTimeOffset at) =>
        AddTrustedUsage(airborneTime, 0, at);

    public AirframeServiceState Inspect(DateTimeOffset at)
    {
        Validate();
        if (InspectionStatus != AirframeInspectionStatus.InspectionDue) throw new InvalidOperationException("Routine inspection is not due.");
        if (at <= UpdatedAt) throw new ArgumentOutOfRangeException(nameof(at), "Inspection time must advance the service timestamp.");
        var result = this with { LastInspectionAtTrackedAirborneTime = TotalTrackedAirborneTime,
            NextInspectionDueAtTrackedAirborneTime = TotalTrackedAirborneTime + LightAircraftRoutineInspectionV1.IntervalAirborneTime,
            Revision = checked(Revision + 1), UpdatedAt = at.ToUniversalTime() };
        result.Validate();
        return result;
    }
}
