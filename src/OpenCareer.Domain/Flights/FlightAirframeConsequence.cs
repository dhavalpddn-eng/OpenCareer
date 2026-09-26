using System.Collections.Immutable;
using System.Text.Json.Serialization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Flights;

public enum FlightDamageSeverity { Normal, ElevatedWear, MinorDamage, MajorDamage, Severe }

public sealed record FlightAirframeCalibration(
    [property: JsonRequired] string Version,
    [property: JsonRequired] double ElevatedDescentFpm,
    [property: JsonRequired] double MinorDamageDescentFpm,
    [property: JsonRequired] double MajorDamageDescentFpm,
    [property: JsonRequired] double StructuralWearFractionPerAirborneHour)
{
    // Gameplay values, NOT structural/impact-force limits. Descent bands: 44781b5,
    // FlightAirframeCalibration.Conservative. Structural rate: 720e93d,
    // InitialMaintenancePrograms.LightAircraftFallback: 0.08 percent/hour / 100.
    public static FlightAirframeCalibration Conservative { get; } = new("contact-evidence-v1", 900, 1300, 1800, 0.0008);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        if (!double.IsFinite(ElevatedDescentFpm) || ElevatedDescentFpm <= 0
            || !double.IsFinite(MinorDamageDescentFpm) || MinorDamageDescentFpm <= ElevatedDescentFpm
            || !double.IsFinite(MajorDamageDescentFpm) || MajorDamageDescentFpm <= MinorDamageDescentFpm
            || !double.IsFinite(StructuralWearFractionPerAirborneHour) || StructuralWearFractionPerAirborneHour < 0)
            throw new ArgumentOutOfRangeException(nameof(FlightAirframeCalibration));
    }
}

public sealed record FlightAirframeSummary(
    [property: JsonRequired] Guid SessionId,
    [property: JsonRequired] Guid? ContractId,
    [property: JsonRequired] AirframeId AirframeId,
    [property: JsonRequired] string CanonicalAircraftId,
    [property: JsonRequired] FlightSessionStatus TerminalStatus,
    [property: JsonRequired] DateTimeOffset StartedAt,
    [property: JsonRequired] DateTimeOffset EndedAt,
    [property: JsonRequired] TimeSpan AirborneTime,
    [property: JsonRequired] TimeSpan BlockTime,
    [property: JsonRequired] int LandingEpisodeCount,
    [property: JsonRequired] int BounceCount,
    [property: JsonRequired] bool CrashReported,
    [property: JsonRequired] ImmutableList<FlightSessionLandingEpisode> LandingEpisodes)
{
    public void Validate()
    {
        _ = new FlightSessionAircraftIdentity(CanonicalAircraftId, AirframeId);
        if (SessionId == Guid.Empty || ContractId == Guid.Empty || StartedAt == default || EndedAt < StartedAt
            || AirborneTime < TimeSpan.Zero || BlockTime < TimeSpan.Zero || LandingEpisodeCount < 0 || BounceCount < 0)
            throw new ArgumentException("Invalid terminal airframe consequence identity, time or counters.");
        if (TerminalStatus is not (FlightSessionStatus.Completed or FlightSessionStatus.Cancelled or FlightSessionStatus.Interrupted))
            throw new InvalidOperationException("Airframe consequences require a terminal FlightSession.");
        ArgumentNullException.ThrowIfNull(LandingEpisodes);
        if (LandingEpisodes.Count > LandingEpisodeCount || LandingEpisodes.Sum(e => (long)e.BounceCount) > BounceCount)
            throw new ArgumentException("Landing evidence exceeds the authoritative flight counters.");
        int previousEpisode = 0;
        DateTimeOffset? previousContact = null;
        foreach (var episode in LandingEpisodes)
        {
            episode.Validate();
            if (episode.EpisodeNumber <= previousEpisode || episode.EpisodeNumber > LandingEpisodeCount
                || episode.TouchdownAt < StartedAt || episode.TouchdownAt > EndedAt || episode.CompletedAt > EndedAt)
                throw new ArgumentException("Invalid confirmed landing episode.");
            foreach (var contact in episode.EffectiveContacts)
            {
                if (contact.Timestamp < StartedAt || contact.Timestamp > EndedAt
                    || previousContact is { } previous && contact.Timestamp <= previous)
                    throw new ArgumentException("Landing contacts must be ordered within this flight.");
                previousContact = contact.Timestamp;
            }
            previousEpisode = episode.EpisodeNumber;
        }
    }
}

/// <summary>Frozen evidence and decision. Null severity means unassessed, not a fabricated normal landing.</summary>
public sealed record FlightAirframeConsequence(
    [property: JsonRequired] FlightAirframeSummary Summary,
    [property: JsonRequired] FlightAirframeCalibration Calibration,
    [property: JsonRequired] FlightLandingContactEvidence? StrongestContact,
    [property: JsonRequired] FlightDamageSeverity? Severity,
    [property: JsonRequired] bool ApplyCondition,
    [property: JsonRequired] double RoutineStructuralWearFraction,
    [property: JsonRequired] string Rationale)
{
    public void Validate()
    {
        var expected = FlightAirframeConsequenceCalculator.Calculate(Summary, Calibration);
        if (StrongestContact != expected.StrongestContact || Severity != expected.Severity
            || ApplyCondition != expected.ApplyCondition || RoutineStructuralWearFraction != expected.RoutineStructuralWearFraction
            || Rationale != expected.Rationale)
            throw new ArgumentException("Airframe consequence decision does not match its retained evidence/calibration.");
    }

    public AirframeCondition ApplyTo(AirframeCondition before)
    {
        ArgumentNullException.ThrowIfNull(before);
        Validate();
        if (!ApplyCondition) return before;
        AirframeDamageState minimum = Severity switch
        {
            FlightDamageSeverity.Severe => AirframeDamageState.Grounding,
            FlightDamageSeverity.MinorDamage or FlightDamageSeverity.MajorDamage => AirframeDamageState.Recorded,
            _ => AirframeDamageState.None
        };
        return new(Math.Clamp(before.WearFraction + RoutineStructuralWearFraction, 0, 1),
            (AirframeDamageState)Math.Max((int)before.Damage, (int)minimum));
    }
}

public static class FlightAirframeConsequenceCalculator
{
    public static FlightAirframeConsequence Calculate(FlightSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var identity = session.AircraftIdentity;
        if (identity?.PhysicalAirframeId is not { } airframeId)
            throw new InvalidOperationException("No explicit physical airframe is assigned to this FlightSession.");
        return Calculate(new FlightAirframeSummary(session.SessionId, session.ContractId, airframeId,
            identity.CanonicalAircraftId, session.Status, session.CreatedAt, session.UpdatedAt,
            session.TimeLedger.AirborneTime, session.TimeLedger.BlockTime, session.Tracking.LandingEpisodeCount,
            session.Tracking.BounceCount, session.Tracking.CrashReported, session.EffectiveLandingEpisodes.ToImmutableList()),
            FlightAirframeCalibration.Conservative);
    }

    public static FlightAirframeConsequence Calculate(FlightAirframeSummary summary, FlightAirframeCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(calibration);
        summary.Validate();
        calibration.Validate();
        // Only persisted reducer-confirmed episodes supply contacts. G is retained for diagnostics,
        // never scored. Equal descent keeps the earliest contact deterministically.
        var strongest = summary.LandingEpisodes.SelectMany(e => e.EffectiveContacts)
            .OrderByDescending(c => Math.Max(0, -c.VerticalSpeedFeetPerMinute)).ThenBy(c => c.Timestamp).FirstOrDefault();
        bool apply = summary.TerminalStatus != FlightSessionStatus.Interrupted || summary.CrashReported;
        double descent = strongest is null ? 0 : Math.Max(0, -strongest.VerticalSpeedFeetPerMinute);
        FlightDamageSeverity? severity = !apply ? null
            : summary.CrashReported ? FlightDamageSeverity.Severe
            : strongest is null ? null
            : descent < calibration.ElevatedDescentFpm ? FlightDamageSeverity.Normal
            : descent < calibration.MinorDamageDescentFpm ? FlightDamageSeverity.ElevatedWear
            : descent < calibration.MajorDamageDescentFpm ? FlightDamageSeverity.MinorDamage
            : FlightDamageSeverity.MajorDamage;
        string reason = !apply ? "Interrupted without crash evidence: condition unchanged because continuity is uncertain."
            : summary.CrashReported ? "Simulator crash reported: severe discrete damage."
            : strongest is null ? "No confirmed landing contact: landing severity unknown; trusted accumulated airborne usage only."
            : "Strongest confirmed contact descent; gameplay bands pending live calibration, not measured impact force or structural limits. G is diagnostic only.";
        return new(summary, calibration, strongest, severity, apply,
            apply ? summary.AirborneTime.TotalHours * calibration.StructuralWearFractionPerAirborneHour : 0, reason);
    }
}
