using System.Globalization;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Events;

public static class ConflictRiskModel
{
    public static double EffectiveBaselineTension(
        ConflictRegionProfile region,
        ConflictSimulationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(policy);
        region.Validate();
        policy.Validate();

        var amplification = region.BaselineSource == ConflictBaselineSource.CuratedRealWorld
            ? policy.CuratedBaselineRiskAmplification
            : 1;

        return Math.Clamp(
            region.BaselineTension * amplification + region.InternalInstability * 0.20,
            0,
            1);
    }

    public static double ConnectionEscalationPressure(
        ConflictRegionConnection connection,
        RegionalConflictPosture a,
        RegionalConflictPosture b)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        connection.Validate();
        a.Validate();
        b.Validate();

        if (!connection.Connects(a.RegionId) || !connection.Connects(b.RegionId) || a.RegionId == b.RegionId)
            throw new ArgumentException("Postures must correspond to both ends of the connection.");

        var geographicExposure = Math.Max(connection.LandBorderExposure, connection.MaritimeExposure * 0.70);
        var tension = (a.Tension + b.Tension) / 2.0;
        var friction = connection.DisputeFriction * (0.35 + 0.65 * geographicExposure);
        var interdependenceDamping = 0.12 * connection.EconomicInterdependence;

        return Math.Clamp(0.62 * tension + 0.38 * friction - interdependenceDamping, 0, 1);
    }

    public static double DailyOutbreakProbability(double escalationPressure)
    {
        if (!double.IsFinite(escalationPressure) || escalationPressure is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(escalationPressure));

        return Math.Clamp(0.00005 + 0.025 * Math.Pow(escalationPressure, 5), 0, 0.05);
    }

    public static double BorderSurveillanceDemand(
        ConflictRegionProfile region,
        IEnumerable<ConflictRegionConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(connections);
        region.Validate();

        var borderExposure = connections
            .Where(connection => connection.Connects(region.RegionId))
            .Select(connection => connection.LandBorderExposure)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Clamp(Math.Max(region.BorderSecurityPressure, borderExposure * 0.60), 0, 1);
    }
}

public static class ConflictWorldEngine
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    public static ConflictWorldState Create(
        ConflictWorldProfile profile,
        ulong careerSeed,
        DateTimeOffset start,
        ConflictSimulationPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var effectivePolicy = policy ?? ConflictSimulationPolicy.Default;
        effectivePolicy.Validate();

        var checkpoint = StartOfUtcDay(start);
        var postures = profile.Regions
            .OrderBy(region => region.RegionId, StringComparer.Ordinal)
            .Select(region =>
            {
                var tension = ConflictRiskModel.EffectiveBaselineTension(region, effectivePolicy);
                var stage = tension switch
                {
                    >= 0.82 => ConflictEscalationStage.LogisticsBuildup,
                    >= 0.64 => ConflictEscalationStage.Surveillance,
                    _ => ConflictEscalationStage.Normal
                };
                return new RegionalConflictPosture(
                    region.RegionId,
                    stage,
                    tension,
                    SeverityForStage(stage, tension),
                    checkpoint,
                    checkpoint);
            })
            .ToArray();

        var campaigns = new List<ConflictCampaignState>();
        foreach (var seed in profile.EffectiveCuratedConflicts.OrderBy(seed => seed.CampaignId, StringComparer.Ordinal))
        {
            var random = DeterministicSeed.CreateStream(
                careerSeed,
                $"conflict:curated:{seed.CampaignId}:{checkpoint.UtcDateTime.Ticks}");
            campaigns.Add(new ConflictCampaignState(
                seed.CampaignId,
                seed.AffectedRegionIds.ToArray(),
                seed.SideAId,
                seed.SideBId,
                ConflictCampaignPhase.ActiveConflict,
                seed.Severity,
                0,
                SampleActiveDurationDays(random),
                checkpoint,
                checkpoint,
                Origin: ConflictBaselineSource.CuratedRealWorld,
                BaselineSourceReference: seed.SourceReference));
        }

        postures = SynchronizePostures(postures, campaigns, checkpoint);
        var state = new ConflictWorldState(
            ConflictWorldState.CurrentSchemaVersion,
            careerSeed,
            checkpoint,
            postures,
            campaigns.ToArray(),
            SystemicConflictState.Inactive);
        state.Validate(profile);
        return state;
    }

    public static ConflictWorldState Advance(
        ConflictWorldState state,
        ConflictWorldProfile profile,
        DateTimeOffset through,
        IReadOnlyList<ConflictPlayerContribution>? contributions = null,
        ConflictSimulationPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(profile);
        state.Validate(profile);

        var effectivePolicy = policy ?? ConflictSimulationPolicy.Default;
        effectivePolicy.Validate();

        if (through < state.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(through), "Conflict simulation cannot move backwards.");

        var target = StartOfUtcDay(through);
        var current = state;
        while (current.UpdatedAt < target)
        {
            var next = current.UpdatedAt + OneDay;
            var dailyContributions = (contributions ?? Array.Empty<ConflictPlayerContribution>())
                .Where(contribution => contribution.OccurredAt >= current.UpdatedAt
                    && contribution.OccurredAt < next)
                .ToArray();

            foreach (var contribution in dailyContributions)
                contribution.Validate();

            current = AdvanceOneDay(current, profile, next, dailyContributions, effectivePolicy);
        }

        return current;
    }

    public static RegionalSecurityState GetRegionalSecurityState(
        ConflictWorldState state,
        ConflictWorldProfile profile,
        string regionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(profile);
        state.Validate(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);

        var posture = state.Regions.Single(region =>
            string.Equals(region.RegionId, regionId, StringComparison.Ordinal));

        var phase = posture.Stage switch
        {
            ConflictEscalationStage.ActiveConflict => RegionalSecurityPhase.ActiveConflict,
            ConflictEscalationStage.Ceasefire => RegionalSecurityPhase.Ceasefire,
            ConflictEscalationStage.Recovery => RegionalSecurityPhase.Recovery,
            ConflictEscalationStage.Surveillance
                or ConflictEscalationStage.LogisticsBuildup
                or ConflictEscalationStage.Mobilization => RegionalSecurityPhase.ElevatedTension,
            _ => RegionalSecurityPhase.Normal
        };

        return new RegionalSecurityState(
            regionId,
            phase,
            posture.Severity,
            RegionalSecuritySource.Simulated,
            posture.StageStartedAt,
            posture.UpdatedAt);
    }

    public static ConflictAviationDemandProfile GetAviationDemand(
        ConflictWorldState state,
        ConflictWorldProfile profile,
        string regionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(profile);
        state.Validate(profile);

        var region = profile.Regions.Single(item =>
            string.Equals(item.RegionId, regionId, StringComparison.Ordinal));
        var posture = state.Regions.Single(item =>
            string.Equals(item.RegionId, regionId, StringComparison.Ordinal));

        var surveillanceBaseline = ConflictRiskModel.BorderSurveillanceDemand(region, profile.Connections);
        var severity = posture.Severity;

        var demand = posture.Stage switch
        {
            ConflictEscalationStage.Normal => new ConflictAviationDemandProfile(
                1 + 1.50 * surveillanceBaseline,
                1,
                1,
                1,
                1,
                1,
                1,
                1),

            ConflictEscalationStage.Surveillance => new ConflictAviationDemandProfile(
                1.70 + 1.40 * surveillanceBaseline,
                1.10,
                1.05,
                1.05,
                1,
                1,
                1,
                0.98),

            ConflictEscalationStage.LogisticsBuildup => new ConflictAviationDemandProfile(
                1.80 + surveillanceBaseline,
                1.55,
                1.50,
                1.15,
                1.10,
                1,
                1.05,
                0.95),

            ConflictEscalationStage.Mobilization => new ConflictAviationDemandProfile(
                2.00 + surveillanceBaseline,
                1.80,
                2.00,
                1.55,
                1.20,
                1.10,
                1.10,
                0.85),

            ConflictEscalationStage.ActiveConflict => new ConflictAviationDemandProfile(
                2.20 + 1.20 * severity,
                1.80 + 0.80 * severity,
                2.20 + 1.20 * severity,
                1.90 + 1.10 * severity,
                1.80 + 0.80 * severity,
                1.60 + 1.00 * severity,
                1.50 + 1.00 * severity,
                Math.Clamp(region.CivilAviationResilience * (1 - 0.75 * severity), 0.10, 0.75)),

            ConflictEscalationStage.Ceasefire => new ConflictAviationDemandProfile(
                1.35,
                1.80,
                1.30,
                0.80,
                1.75,
                1.65,
                2.10,
                Math.Clamp(0.45 + 0.25 * region.CivilAviationResilience, 0.45, 0.70)),

            ConflictEscalationStage.Recovery => new ConflictAviationDemandProfile(
                1.10,
                1.55,
                0.90,
                0.70,
                1.35,
                1.20,
                1.75,
                Math.Clamp(0.85 + 0.15 * region.CivilAviationResilience, 0.85, 1.0)),

            _ => throw new ArgumentOutOfRangeException(nameof(posture.Stage))
        };

        if (state.SystemicConflict.IsActive
            && state.SystemicConflict.InvolvedRegionIds.Contains(regionId, StringComparer.Ordinal)
            && posture.Stage != ConflictEscalationStage.ActiveConflict)
        {
            demand = demand with
            {
                SurveillanceMultiplier = demand.SurveillanceMultiplier * 1.20,
                CargoMultiplier = demand.CargoMultiplier * 1.35,
                MilitaryTransportMultiplier = demand.MilitaryTransportMultiplier * 1.50,
                FighterOperationsMultiplier = demand.FighterOperationsMultiplier * 1.25,
                PassengerContinuityMultiplier = Math.Max(demand.PassengerContinuityMultiplier, 0.70)
            };
        }

        demand.Validate();
        return demand;
    }

    private static ConflictWorldState AdvanceOneDay(
        ConflictWorldState state,
        ConflictWorldProfile profile,
        DateTimeOffset next,
        IReadOnlyList<ConflictPlayerContribution> contributions,
        ConflictSimulationPolicy policy)
    {
        var campaigns = state.Campaigns.ToList();
        for (var i = 0; i < campaigns.Count; i++)
            campaigns[i] = AdvanceCampaign(state, campaigns[i], next, contributions, policy);

        var postures = AdvancePostures(state, profile, campaigns, next, policy);
        campaigns.AddRange(StartNewCampaigns(state, profile, postures, campaigns, next, policy));

        var systemic = AdvanceSystemicConflict(
            state,
            profile,
            postures,
            campaigns,
            next,
            policy);

        postures = ApplySystemicMobilization(postures, profile, systemic, next);
        postures = SynchronizePostures(postures, campaigns, next);

        var result = new ConflictWorldState(
            state.SchemaVersion,
            state.CareerSeed,
            next,
            postures,
            campaigns.Select(campaign => campaign with { UpdatedAt = next }).ToArray(),
            systemic);

        result.Validate(profile);
        return result;
    }

    private static ConflictCampaignState AdvanceCampaign(
        ConflictWorldState world,
        ConflictCampaignState campaign,
        DateTimeOffset next,
        IReadOnlyList<ConflictPlayerContribution> contributions,
        ConflictSimulationPolicy policy)
    {
        if (campaign.Phase == ConflictCampaignPhase.Resolved)
            return campaign with { UpdatedAt = next };

        var random = DeterministicSeed.CreateStream(
            world.CareerSeed,
            $"conflict:campaign:{campaign.CampaignId}:{next.UtcDateTime.Ticks}");

        var balance = campaign.OutcomeBalance;
        var severity = campaign.Severity;
        var relevant = contributions.Where(contribution =>
            string.Equals(contribution.CampaignId, campaign.CampaignId, StringComparison.Ordinal));

        var sideDelta = 0.0;
        var humanitarianPoints = 0.0;
        foreach (var contribution in relevant)
        {
            var neutral = contribution.Kind is ConflictContributionKind.Medical
                or ConflictContributionKind.Evacuation
                or ConflictContributionKind.Humanitarian
                or ConflictContributionKind.Recovery;

            if (neutral)
            {
                humanitarianPoints += contribution.EffortPoints;
                continue;
            }

            var sign = string.Equals(contribution.SupportedSideId, campaign.SideAId, StringComparison.Ordinal)
                ? 1
                : string.Equals(contribution.SupportedSideId, campaign.SideBId, StringComparison.Ordinal)
                    ? -1
                    : 0;
            sideDelta += sign * Math.Log10(1 + contribution.EffortPoints) / 100.0;
        }

        sideDelta = Math.Clamp(
            sideDelta,
            -policy.MaxPlayerOutcomeShiftPerDay,
            policy.MaxPlayerOutcomeShiftPerDay);
        balance = Math.Clamp(
            balance + sideDelta,
            -policy.MaxPlayerOutcomeBias,
            policy.MaxPlayerOutcomeBias);

        if (humanitarianPoints > 0)
        {
            var reduction = Math.Min(
                policy.MaxHumanitarianSeverityReductionPerDay,
                Math.Log10(1 + humanitarianPoints) / 250.0);
            severity = Math.Max(0.05, severity - reduction);
        }

        if (campaign.Phase == ConflictCampaignPhase.ActiveConflict)
        {
            var ageDays = (next - campaign.StartedAt).TotalDays;
            var ratio = ageDays / campaign.ExpectedActiveDurationDays;
            var hazard = 0.001 + 0.008 * (1 - severity) + Math.Max(0, ratio - 0.60) * 0.03;
            var forceCeasefire = ageDays >= campaign.ExpectedActiveDurationDays * 3;
            if (forceCeasefire || random.Chance(Math.Clamp(hazard, 0, 0.35)))
            {
                var resolution = ResolveCampaign(balance, random);
                return campaign with
                {
                    Phase = ConflictCampaignPhase.Ceasefire,
                    Severity = severity,
                    OutcomeBalance = balance,
                    UpdatedAt = next,
                    PhaseEndsAt = next.AddDays(random.NextDouble(7, 30)),
                    Resolution = resolution
                };
            }

            var drift = random.NextNormal(0, 0.015);
            severity = Math.Clamp(severity + drift, 0.10, 1);
            return campaign with
            {
                Severity = severity,
                OutcomeBalance = balance,
                UpdatedAt = next
            };
        }

        if (campaign.Phase == ConflictCampaignPhase.Ceasefire
            && campaign.PhaseEndsAt is { } ceasefireEnd
            && next >= ceasefireEnd)
        {
            return campaign with
            {
                Phase = ConflictCampaignPhase.Recovery,
                Severity = Math.Clamp(severity * 0.65, 0.05, 0.65),
                UpdatedAt = next,
                PhaseEndsAt = next.AddDays(random.NextDouble(30, 180))
            };
        }

        if (campaign.Phase == ConflictCampaignPhase.Recovery
            && campaign.PhaseEndsAt is { } recoveryEnd
            && next >= recoveryEnd)
        {
            return campaign with
            {
                Phase = ConflictCampaignPhase.Resolved,
                Severity = 0,
                UpdatedAt = next,
                PhaseEndsAt = null
            };
        }

        return campaign with
        {
            Severity = severity,
            OutcomeBalance = balance,
            UpdatedAt = next
        };
    }

    private static RegionalConflictPosture[] AdvancePostures(
        ConflictWorldState state,
        ConflictWorldProfile profile,
        IReadOnlyList<ConflictCampaignState> campaigns,
        DateTimeOffset next,
        ConflictSimulationPolicy policy)
    {
        var profileById = profile.Regions.ToDictionary(region => region.RegionId, StringComparer.Ordinal);
        var previousById = state.Regions.ToDictionary(region => region.RegionId, StringComparer.Ordinal);

        var activeRegionIds = campaigns
            .Where(campaign => campaign.Phase == ConflictCampaignPhase.ActiveConflict)
            .SelectMany(campaign => campaign.AffectedRegionIds)
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<RegionalConflictPosture>(profile.Regions.Count);
        foreach (var region in profile.Regions.OrderBy(region => region.RegionId, StringComparer.Ordinal))
        {
            var previous = previousById[region.RegionId];
            if (campaigns.Any(campaign => campaign.IsOperational
                && campaign.AffectedRegionIds.Contains(region.RegionId, StringComparer.Ordinal)))
            {
                results.Add(previous with { UpdatedAt = next });
                continue;
            }

            var connections = profile.Connections.Where(connection => connection.Connects(region.RegionId)).ToArray();
            var neighborConflictExposure = connections
                .Where(connection => activeRegionIds.Contains(connection.Other(region.RegionId)))
                .Select(connection => Math.Max(connection.LandBorderExposure, connection.MaritimeExposure * 0.65))
                .DefaultIfEmpty(0)
                .Max();

            var maxDispute = connections.Select(connection => connection.DisputeFriction).DefaultIfEmpty(0).Max();
            var avgInterdependence = connections.Select(connection => connection.EconomicInterdependence).DefaultIfEmpty(0).Average();
            var target = ConflictRiskModel.EffectiveBaselineTension(region, policy)
                + policy.NeighborConflictTensionBoost * neighborConflictExposure
                + 0.12 * maxDispute
                - 0.05 * avgInterdependence;
            target = Math.Clamp(target, 0, 1);

            var random = DeterministicSeed.CreateStream(
                state.CareerSeed,
                $"conflict:tension:{region.RegionId}:{next.UtcDateTime.Ticks}");
            var tension = Math.Clamp(
                previous.Tension
                    + policy.TensionMeanReversionPerDay * (target - previous.Tension)
                    + random.NextNormal(0, policy.TensionNoiseStandardDeviation),
                0,
                1);

            var pressure = Math.Clamp(0.72 * tension + 0.28 * maxDispute, 0, 1);
            var stage = AdvanceEscalationStage(previous.Stage, pressure, random, policy);
            var stageStartedAt = stage == previous.Stage ? previous.StageStartedAt : next;

            results.Add(new RegionalConflictPosture(
                region.RegionId,
                stage,
                tension,
                SeverityForStage(stage, pressure),
                stageStartedAt,
                next));
        }

        return results.ToArray();
    }

    private static ConflictEscalationStage AdvanceEscalationStage(
        ConflictEscalationStage current,
        double pressure,
        DeterministicRandom random,
        ConflictSimulationPolicy policy)
    {
        if (current is ConflictEscalationStage.ActiveConflict
            or ConflictEscalationStage.Ceasefire
            or ConflictEscalationStage.Recovery)
        {
            return current;
        }

        var surpriseProbability = 1 - Math.Exp(
            -policy.SurpriseEscalationAnnualRate * Math.Pow(pressure, 4) / 365.2425);

        if (current is ConflictEscalationStage.Normal or ConflictEscalationStage.Surveillance
            && pressure >= 0.80
            && random.Chance(surpriseProbability))
        {
            return ConflictEscalationStage.Mobilization;
        }

        return current switch
        {
            ConflictEscalationStage.Normal =>
                random.Chance(Math.Clamp(0.002 + 0.003 * pressure * pressure, 0, 0.02))
                    ? ConflictEscalationStage.Surveillance
                    : current,

            ConflictEscalationStage.Surveillance =>
                ChooseUpOrDown(
                    current,
                    ConflictEscalationStage.LogisticsBuildup,
                    ConflictEscalationStage.Normal,
                    0.0002 + 0.006 * Math.Pow(pressure, 3),
                    0.01 + 0.04 * (1 - pressure),
                    random),

            ConflictEscalationStage.LogisticsBuildup =>
                ChooseUpOrDown(
                    current,
                    ConflictEscalationStage.Mobilization,
                    ConflictEscalationStage.Surveillance,
                    0.0001 + 0.012 * Math.Pow(pressure, 4),
                    0.005 + 0.03 * (1 - pressure),
                    random),

            ConflictEscalationStage.Mobilization =>
                random.Chance(0.002 + 0.02 * (1 - pressure))
                    ? ConflictEscalationStage.LogisticsBuildup
                    : current,

            _ => current
        };
    }

    private static ConflictEscalationStage ChooseUpOrDown(
        ConflictEscalationStage current,
        ConflictEscalationStage up,
        ConflictEscalationStage down,
        double upProbability,
        double downProbability,
        DeterministicRandom random)
    {
        var roll = random.NextDouble();
        if (roll < upProbability)
            return up;
        if (roll < upProbability + downProbability)
            return down;
        return current;
    }

    private static IEnumerable<ConflictCampaignState> StartNewCampaigns(
        ConflictWorldState state,
        ConflictWorldProfile profile,
        IReadOnlyList<RegionalConflictPosture> postures,
        IReadOnlyList<ConflictCampaignState> campaigns,
        DateTimeOffset next,
        ConflictSimulationPolicy policy)
    {
        var activeRegions = campaigns
            .Where(campaign => campaign.IsOperational)
            .SelectMany(campaign => campaign.AffectedRegionIds)
            .ToHashSet(StringComparer.Ordinal);
        var postureById = postures.ToDictionary(posture => posture.RegionId, StringComparer.Ordinal);
        var startedRegions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in profile.Connections.OrderBy(connection => connection.Key, StringComparer.Ordinal))
        {
            if (activeRegions.Contains(connection.RegionAId)
                || activeRegions.Contains(connection.RegionBId)
                || startedRegions.Contains(connection.RegionAId)
                || startedRegions.Contains(connection.RegionBId))
            {
                continue;
            }

            var a = postureById[connection.RegionAId];
            var b = postureById[connection.RegionBId];
            if (a.Stage != ConflictEscalationStage.Mobilization
                && b.Stage != ConflictEscalationStage.Mobilization)
            {
                continue;
            }

            var pressure = ConflictRiskModel.ConnectionEscalationPressure(connection, a, b);
            if (pressure < 0.60)
                continue;

            var random = DeterministicSeed.CreateStream(
                state.CareerSeed,
                $"conflict:outbreak:{connection.Key}:{next.UtcDateTime.Ticks}");
            if (!random.Chance(ConflictRiskModel.DailyOutbreakProbability(pressure)))
                continue;

            var campaign = CreateSimulatedCampaign(
                state.CareerSeed,
                next,
                [connection.RegionAId, connection.RegionBId],
                connection.RegionAId,
                connection.RegionBId,
                Math.Clamp(0.45 + 0.50 * pressure, 0.45, 0.95),
                $"border:{connection.Key}");

            startedRegions.Add(connection.RegionAId);
            startedRegions.Add(connection.RegionBId);
            yield return campaign;
        }

        foreach (var region in profile.Regions.OrderBy(region => region.RegionId, StringComparer.Ordinal))
        {
            if (activeRegions.Contains(region.RegionId) || startedRegions.Contains(region.RegionId))
                continue;

            var posture = postureById[region.RegionId];
            if (posture.Stage != ConflictEscalationStage.Mobilization)
                continue;

            var internalPressure = Math.Clamp(
                0.70 * posture.Tension + 0.30 * region.InternalInstability,
                0,
                1);
            if (internalPressure < 0.72)
                continue;

            var random = DeterministicSeed.CreateStream(
                state.CareerSeed,
                $"conflict:internal:{region.RegionId}:{next.UtcDateTime.Ticks}");
            var probability = 0.00002 + 0.015 * Math.Pow(internalPressure, 5);
            if (!random.Chance(probability))
                continue;

            yield return CreateSimulatedCampaign(
                state.CareerSeed,
                next,
                [region.RegionId],
                region.RegionId,
                $"{region.RegionId}:internal-opposition",
                Math.Clamp(0.40 + 0.50 * internalPressure, 0.40, 0.90),
                $"internal:{region.RegionId}");
        }
    }

    private static ConflictCampaignState CreateSimulatedCampaign(
        ulong careerSeed,
        DateTimeOffset startsAt,
        IReadOnlyList<string> affectedRegions,
        string sideA,
        string sideB,
        double severity,
        string key)
    {
        var random = DeterministicSeed.CreateStream(
            careerSeed,
            $"conflict:campaign-new:{key}:{startsAt.UtcDateTime.Ticks}");
        var suffix = random.NextUInt64().ToString("X12", CultureInfo.InvariantCulture);
        return new ConflictCampaignState(
            $"sim:{key}:{startsAt:yyyyMMdd}:{suffix}",
            affectedRegions.ToArray(),
            sideA,
            sideB,
            ConflictCampaignPhase.ActiveConflict,
            severity,
            0,
            SampleActiveDurationDays(random),
            startsAt,
            startsAt);
    }

    private static SystemicConflictState AdvanceSystemicConflict(
        ConflictWorldState previous,
        ConflictWorldProfile profile,
        IReadOnlyList<RegionalConflictPosture> postures,
        IReadOnlyList<ConflictCampaignState> campaigns,
        DateTimeOffset next,
        ConflictSimulationPolicy policy)
    {
        var activeCampaigns = campaigns
            .Where(campaign => campaign.Phase == ConflictCampaignPhase.ActiveConflict)
            .ToArray();

        if (previous.SystemicConflict.IsActive)
        {
            var started = previous.SystemicConflict.StartedAt!.Value;
            if (activeCampaigns.Length < policy.MinimumActiveCampaignsForSystemicConflict
                && (next - started).TotalDays >= policy.MinimumSystemicConflictDays)
            {
                var random = DeterministicSeed.CreateStream(
                    previous.CareerSeed,
                    $"conflict:systemic-end:{next.UtcDateTime.Ticks}");
                if (random.Chance(0.05))
                    return SystemicConflictState.Inactive;
            }

            var severity = activeCampaigns.Length == 0
                ? previous.SystemicConflict.Severity * 0.98
                : Math.Clamp(activeCampaigns.Average(campaign => campaign.Severity), 0.15, 1);
            return previous.SystemicConflict with { Severity = severity };
        }

        if (activeCampaigns.Length < policy.MinimumActiveCampaignsForSystemicConflict)
            return SystemicConflictState.Inactive;

        var activeRegions = activeCampaigns
            .SelectMany(campaign => campaign.AffectedRegionIds)
            .ToHashSet(StringComparer.Ordinal);

        var allianceExposure = profile.Connections
            .Where(connection => activeRegions.Contains(connection.RegionAId)
                || activeRegions.Contains(connection.RegionBId))
            .Select(connection => connection.AllianceStrength)
            .DefaultIfEmpty(0)
            .Average();

        var severityPressure = activeCampaigns.Average(campaign => campaign.Severity);
        var systemicPressure = Math.Clamp(0.65 * severityPressure + 0.35 * allianceExposure, 0, 1);
        var probability = 1 - Math.Exp(
            -policy.SystemicConflictAnnualRate * systemicPressure / 365.2425);
        var randomStart = DeterministicSeed.CreateStream(
            previous.CareerSeed,
            $"conflict:systemic-start:{next.UtcDateTime.Ticks}");

        if (!randomStart.Chance(probability))
            return SystemicConflictState.Inactive;

        var involved = new HashSet<string>(activeRegions, StringComparer.Ordinal);
        foreach (var connection in profile.Connections)
        {
            if (connection.AllianceStrength < 0.65)
                continue;
            if (activeRegions.Contains(connection.RegionAId))
                involved.Add(connection.RegionBId);
            if (activeRegions.Contains(connection.RegionBId))
                involved.Add(connection.RegionAId);
        }

        return new SystemicConflictState(
            true,
            Math.Clamp(systemicPressure, 0.25, 1),
            next,
            involved.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    private static RegionalConflictPosture[] ApplySystemicMobilization(
        IReadOnlyList<RegionalConflictPosture> postures,
        ConflictWorldProfile profile,
        SystemicConflictState systemic,
        DateTimeOffset next)
    {
        if (!systemic.IsActive)
            return postures.ToArray();

        return postures.Select(posture =>
        {
            if (!systemic.InvolvedRegionIds.Contains(posture.RegionId, StringComparer.Ordinal)
                || posture.Stage is ConflictEscalationStage.ActiveConflict
                    or ConflictEscalationStage.Ceasefire
                    or ConflictEscalationStage.Recovery)
            {
                return posture;
            }

            var desired = posture.Tension >= 0.78
                ? ConflictEscalationStage.Mobilization
                : ConflictEscalationStage.LogisticsBuildup;
            if ((int)posture.Stage >= (int)desired)
                return posture;

            return posture with
            {
                Stage = desired,
                Severity = SeverityForStage(desired, posture.Tension),
                StageStartedAt = next
            };
        }).ToArray();
    }

    private static RegionalConflictPosture[] SynchronizePostures(
        IReadOnlyList<RegionalConflictPosture> postures,
        IReadOnlyList<ConflictCampaignState> campaigns,
        DateTimeOffset now)
    {
        return postures.Select(posture =>
        {
            var relevant = campaigns
                .Where(campaign => campaign.IsOperational
                    && campaign.AffectedRegionIds.Contains(posture.RegionId, StringComparer.Ordinal))
                .OrderByDescending(campaign => campaign.Phase == ConflictCampaignPhase.ActiveConflict)
                .ThenByDescending(campaign => campaign.Severity)
                .ToArray();

            if (relevant.Length == 0)
            {
                if (posture.Stage is ConflictEscalationStage.ActiveConflict
                    or ConflictEscalationStage.Ceasefire
                    or ConflictEscalationStage.Recovery)
                {
                    return posture with
                    {
                        Stage = ConflictEscalationStage.Normal,
                        Severity = 0,
                        StageStartedAt = now,
                        UpdatedAt = now
                    };
                }

                return posture with { UpdatedAt = now };
            }

            var phase = relevant[0].Phase;
            var stage = phase switch
            {
                ConflictCampaignPhase.ActiveConflict => ConflictEscalationStage.ActiveConflict,
                ConflictCampaignPhase.Ceasefire => ConflictEscalationStage.Ceasefire,
                ConflictCampaignPhase.Recovery => ConflictEscalationStage.Recovery,
                _ => posture.Stage
            };
            var severity = relevant.Max(campaign => campaign.Severity);
            return posture with
            {
                Stage = stage,
                Severity = severity,
                StageStartedAt = stage == posture.Stage ? posture.StageStartedAt : now,
                UpdatedAt = now
            };
        }).ToArray();
    }

    private static ConflictResolutionKind ResolveCampaign(
        double outcomeBalance,
        DeterministicRandom random)
    {
        var score = random.NextNormal(0, 0.65) + outcomeBalance * 1.5;
        if (score >= 0.85)
            return ConflictResolutionKind.SideAAdvantage;
        if (score <= -0.85)
            return ConflictResolutionKind.SideBAdvantage;
        return random.Chance(0.65)
            ? ConflictResolutionKind.Negotiated
            : ConflictResolutionKind.Stalemate;
    }

    private static double SampleActiveDurationDays(DeterministicRandom random)
    {
        var roll = random.NextDouble();
        return roll switch
        {
            < 0.15 => random.NextDouble(3, 14),
            < 0.60 => random.NextDouble(14, 120),
            < 0.90 => random.NextDouble(120, 540),
            _ => random.NextDouble(540, 1825)
        };
    }

    private static double SeverityForStage(ConflictEscalationStage stage, double pressure) =>
        stage switch
        {
            ConflictEscalationStage.Normal => 0,
            ConflictEscalationStage.Surveillance => Math.Clamp(0.15 + 0.30 * pressure, 0.15, 0.45),
            ConflictEscalationStage.LogisticsBuildup => Math.Clamp(0.30 + 0.35 * pressure, 0.30, 0.65),
            ConflictEscalationStage.Mobilization => Math.Clamp(0.45 + 0.40 * pressure, 0.45, 0.85),
            ConflictEscalationStage.ActiveConflict => Math.Clamp(pressure, 0.40, 1),
            ConflictEscalationStage.Ceasefire => Math.Clamp(pressure, 0.15, 0.65),
            ConflictEscalationStage.Recovery => Math.Clamp(pressure, 0.05, 0.45),
            _ => 0
        };

    private static DateTimeOffset StartOfUtcDay(DateTimeOffset value)
    {
        var utc = value.UtcDateTime.Date;
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
