using OpenCareer.Domain.Simulation;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Domain.Conflict;

public sealed record ThreatExposure(
    Guid ThreatId,
    double DistanceNauticalMiles,
    double Severity);

public sealed record ThreatResolution(
    ConflictWorldState State,
    ThreatEngagementResult Result);

public static class ThreatExposureEvaluator
{
    public static IReadOnlyList<ThreatExposure> Evaluate(
        ConflictWorldState state,
        AircraftTelemetrySnapshot telemetry)
    {
        ConflictValidation.Validate(state);
        ArgumentNullException.ThrowIfNull(telemetry);

        if (telemetry.Paused || telemetry.SlewActive || telemetry.OnGround)
            return Array.Empty<ThreatExposure>();

        var position = new GeoPoint(
            telemetry.LatitudeDegrees,
            telemetry.LongitudeDegrees);

        return state.Threats
            .Where(threat => threat.Active && threat.Side == ConflictSide.Hostile)
            .Select(threat => new ThreatExposure(
                threat.ThreatId,
                ConflictGeometry.DistanceNauticalMiles(position, threat.Center),
                threat.Severity))
            .Where(exposure =>
            {
                var threat = state.Threats.Single(item => item.ThreatId == exposure.ThreatId);
                return exposure.DistanceNauticalMiles <= threat.RadiusNauticalMiles;
            })
            .OrderByDescending(exposure => exposure.Severity)
            .ThenBy(exposure => exposure.DistanceNauticalMiles)
            .ToArray();
    }
}

public static class ThreatEngagementResolver
{
    public static ThreatResolution Resolve(
        ConflictWorldState state,
        ThreatEngagementRequest request,
        PlayerCombatState playerState)
    {
        ConflictValidation.Validate(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(playerState);
        request.Validate();
        playerState.Validate();

        if (state.ProcessedEventIds.Contains(request.EngagementId, StringComparer.Ordinal))
        {
            return new ThreatResolution(
                state,
                new ThreatEngagementResult(
                    request.EngagementId,
                    ThreatEngagementOutcome.DuplicateIgnored,
                    SimulatedDamageLevel.None,
                    playerState,
                    "Threat engagement already resolved."));
        }

        var threat = state.Threats.FirstOrDefault(item => item.ThreatId == request.ThreatId);
        if (threat is null || !threat.Active || threat.Side != ConflictSide.Hostile)
        {
            return new ThreatResolution(
                MarkProcessed(state, request.EngagementId),
                new ThreatEngagementResult(
                    request.EngagementId,
                    ThreatEngagementOutcome.NoEngagement,
                    SimulatedDamageLevel.None,
                    playerState,
                    "Threat is inactive or unavailable."));
        }

        var exposure = Math.Clamp(request.ExposureDuration.TotalSeconds / 45d, 0, 1);
        var responseMitigation = 0.70 * request.DefensiveResponseQuality;
        var engagementChance = Math.Clamp(
            threat.Severity * exposure * (1 - responseMitigation),
            0,
            0.85);

        var random = DeterministicSeed.CreateStream(
            state.TheaterSeed,
            $"conflict-threat:{state.TheaterId}:{request.EngagementId}");

        var roll = random.NextDouble();
        ThreatEngagementOutcome outcome;
        SimulatedDamageLevel damageLevel;

        if (roll >= engagementChance)
        {
            outcome = request.DefensiveResponseQuality >= 0.55
                ? ThreatEngagementOutcome.Defeated
                : ThreatEngagementOutcome.NearMiss;
            damageLevel = SimulatedDamageLevel.None;
        }
        else
        {
            outcome = ThreatEngagementOutcome.Hit;
            damageLevel = SelectDamageLevel(random.NextDouble(), threat.Severity);
        }

        var newPlayerState = ApplyDamage(playerState, damageLevel, random);
        var updatedState = MarkProcessed(state, request.EngagementId);

        return new ThreatResolution(
            updatedState,
            new ThreatEngagementResult(
                request.EngagementId,
                outcome,
                damageLevel,
                newPlayerState,
                null));
    }

    private static SimulatedDamageLevel SelectDamageLevel(
        double roll,
        double severity)
    {
        var weighted = Math.Clamp(roll + (1 - severity) * 0.25, 0, 1);

        if (weighted < 0.10)
            return SimulatedDamageLevel.MissionKill;

        if (weighted < 0.28)
            return SimulatedDamageLevel.Severe;

        if (weighted < 0.58)
            return SimulatedDamageLevel.Moderate;

        return SimulatedDamageLevel.Light;
    }

    private static PlayerCombatState ApplyDamage(
        PlayerCombatState current,
        SimulatedDamageLevel level,
        DeterministicRandom random)
    {
        var magnitude = level switch
        {
            SimulatedDamageLevel.None => 0d,
            SimulatedDamageLevel.Light => 0.12,
            SimulatedDamageLevel.Moderate => 0.28,
            SimulatedDamageLevel.Severe => 0.48,
            SimulatedDamageLevel.MissionKill => 0.80,
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };

        if (magnitude == 0)
            return current;

        var airframeShare = 0.45 + random.NextDouble() * 0.30;
        var propulsionShare = 0.25 + random.NextDouble() * 0.35;
        var systemsShare = 0.30 + random.NextDouble() * 0.35;

        return new PlayerCombatState(
            Math.Clamp(current.AirframeDamage + magnitude * airframeShare, 0, 1),
            Math.Clamp(current.PropulsionDamage + magnitude * propulsionShare, 0, 1),
            Math.Clamp(current.SystemsDamage + magnitude * systemsShare, 0, 1));
    }

    private static ConflictWorldState MarkProcessed(
        ConflictWorldState state,
        string eventId) =>
        state with
        {
            ProcessedEventIds = state.ProcessedEventIds
                .Append(eventId)
                .ToArray()
        };
}
