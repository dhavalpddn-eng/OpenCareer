namespace OpenCareer.Domain.Conflict;

public enum ConflictCampaignPhase
{
    Contested,
    FriendlyPressure,
    HostilePressure,
    FriendlySecured,
    HostileSecured
}

public enum ConflictCampaignOutcome
{
    Ongoing,
    Victory,
    Defeat,
    Stalemate,
    Ceasefire
}

public enum StrategicObjectiveKind
{
    GainSectorControl,
    ImproveIntelligence,
    RestoreFriendlyReadiness,
    ReduceHostileThreat
}

public sealed record ConflictStrategicObjective(
    string ObjectiveId,
    StrategicObjectiveKind Kind,
    string? SectorId,
    Guid? UnitId,
    Guid? ThreatId,
    double TargetValue,
    double Progress)
{
    public bool IsComplete => Progress >= 0.999999;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ObjectiveId);

        if (!double.IsFinite(TargetValue) || TargetValue is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(TargetValue));

        if (!double.IsFinite(Progress) || Progress is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Progress));

        switch (Kind)
        {
            case StrategicObjectiveKind.GainSectorControl:
            case StrategicObjectiveKind.ImproveIntelligence:
                if (string.IsNullOrWhiteSpace(SectorId))
                    throw new ArgumentException("Sector objective requires a sector ID.");
                break;

            case StrategicObjectiveKind.RestoreFriendlyReadiness:
                if (UnitId is null || UnitId == Guid.Empty)
                    throw new ArgumentException("Readiness objective requires a unit ID.");
                break;

            case StrategicObjectiveKind.ReduceHostileThreat:
                if (ThreatId is null || ThreatId == Guid.Empty)
                    throw new ArgumentException("Threat objective requires a threat ID.");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Kind));
        }
    }
}

public sealed record ConflictCampaignState(
    int SchemaVersion,
    string CampaignId,
    string TheaterId,
    ConflictCampaignPhase Phase,
    long EvaluationSequence,
    DateTimeOffset UpdatedAt,
    double FriendlyControlAverage,
    double FriendlyMomentum,
    ConflictStrategicObjective[] Objectives,
    ConflictCampaignIdentity? Identity = null)
{
    public const int CurrentSchemaVersion = 1;

    public ConflictCampaignOutcome Outcome { get; init; } =
        ConflictCampaignOutcome.Ongoing;

    public int StableEvaluationCount { get; init; }

    public int SecuredEvaluationCount { get; init; }

    public double FriendlyReplacementReserve { get; init; } = 0.22;

    public double HostileReplacementReserve { get; init; } = 0.22;

    public bool IsTerminal => Outcome != ConflictCampaignOutcome.Ongoing;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException($"Campaign-state schema {SchemaVersion} is not supported.");

        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(TheaterId);
        ArgumentNullException.ThrowIfNull(Objectives);

        if (EvaluationSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(EvaluationSequence));

        if (!double.IsFinite(FriendlyControlAverage)
            || FriendlyControlAverage is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(FriendlyControlAverage));
        }

        if (!double.IsFinite(FriendlyMomentum)
            || FriendlyMomentum is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(FriendlyMomentum));
        }

        if (StableEvaluationCount < 0)
            throw new ArgumentOutOfRangeException(nameof(StableEvaluationCount));

        if (SecuredEvaluationCount < 0)
            throw new ArgumentOutOfRangeException(nameof(SecuredEvaluationCount));

        ValidateReserve(
            FriendlyReplacementReserve,
            nameof(FriendlyReplacementReserve));

        ValidateReserve(
            HostileReplacementReserve,
            nameof(HostileReplacementReserve));

        Identity?.Validate();

        foreach (var objective in Objectives)
            objective.Validate();

        if (Objectives.Select(item => item.ObjectiveId)
            .Distinct(StringComparer.Ordinal)
            .Count() != Objectives.Length)
        {
            throw new ArgumentException("Strategic objective IDs must be unique.");
        }

        if (IsTerminal && Objectives.Length != 0)
        {
            throw new ArgumentException(
                "Terminal conflict campaigns cannot retain active strategic objectives.");
        }
    }

    private static void ValidateReserve(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class ConflictCampaignDirector
{
    public static ConflictCampaignState Create(
        string campaignId,
        ConflictWorldState world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ConflictValidation.Validate(world);

        double control = AverageFriendlyControl(world);

        var state = new ConflictCampaignState(
            ConflictCampaignState.CurrentSchemaVersion,
            campaignId,
            world.TheaterId,
            DeterminePhase(world, control),
            EvaluationSequence: 0,
            world.UpdatedAt,
            control,
            FriendlyMomentum: 0,
            BuildObjectives(world),
            ConflictCampaignIdentityGenerator.Create(campaignId, world));

        state.Validate();
        return state;
    }

    public static ConflictCampaignState Advance(
        ConflictCampaignState previous,
        ConflictWorldState world,
        bool evaluateCampaignOutcome = true)
    {
        ArgumentNullException.ThrowIfNull(previous);
        previous.Validate();
        ConflictValidation.Validate(world);

        if (previous.IsTerminal)
            throw new InvalidOperationException("Terminal conflict campaign cannot advance.");

        if (!string.Equals(previous.TheaterId, world.TheaterId, StringComparison.Ordinal))
            throw new InvalidOperationException("Campaign state belongs to another theater.");

        if (world.UpdatedAt < previous.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(world), "Campaign time cannot move backwards.");

        double control = AverageFriendlyControl(world);
        double delta = control - previous.FriendlyControlAverage;
        double momentum = Math.Clamp(
            previous.FriendlyMomentum * 0.65 + delta * 4.0,
            -1,
            1);

        ConflictCampaignPhase phase =
            DeterminePhase(world, control);

        int stableEvaluations = previous.StableEvaluationCount;
        int securedEvaluations = previous.SecuredEvaluationCount;
        long nextEvaluationSequence = previous.EvaluationSequence;
        ConflictCampaignOutcome outcome = previous.Outcome;

        if (evaluateCampaignOutcome)
        {
            stableEvaluations =
                IsStrategicallyStable(control, momentum, delta)
                    ? checked(previous.StableEvaluationCount + 1)
                    : 0;

            securedEvaluations =
                phase is ConflictCampaignPhase.FriendlySecured
                    or ConflictCampaignPhase.HostileSecured
                    ? phase == previous.Phase
                        ? checked(previous.SecuredEvaluationCount + 1)
                        : 1
                    : 0;

            nextEvaluationSequence =
                checked(previous.EvaluationSequence + 1);

            outcome = DetermineOutcome(
                world,
                phase,
                nextEvaluationSequence,
                stableEvaluations,
                securedEvaluations);
        }

        var updated = previous with
        {
            Phase = phase,
            EvaluationSequence = nextEvaluationSequence,
            UpdatedAt = world.UpdatedAt,
            FriendlyControlAverage = control,
            FriendlyMomentum = momentum,
            StableEvaluationCount = stableEvaluations,
            SecuredEvaluationCount = securedEvaluations,
            Outcome = outcome,
            Objectives = outcome == ConflictCampaignOutcome.Ongoing
                ? BuildObjectives(world)
                : Array.Empty<ConflictStrategicObjective>(),
            Identity = ConflictFactionPostureEvolution.Apply(
                previous.Identity
                    ?? ConflictCampaignIdentityGenerator.Create(
                        previous.CampaignId,
                        world),
                previous.Phase,
                phase)
        };

        updated.Validate();
        return updated;
    }

    private static ConflictCampaignOutcome DetermineOutcome(
        ConflictWorldState world,
        ConflictCampaignPhase phase,
        long evaluationSequence,
        int stableEvaluations,
        int securedEvaluations)
    {
        if (securedEvaluations >= 2)
        {
            if (phase == ConflictCampaignPhase.FriendlySecured)
                return ConflictCampaignOutcome.Victory;

            if (phase == ConflictCampaignPhase.HostileSecured)
                return ConflictCampaignOutcome.Defeat;
        }

        if (evaluationSequence >= 4 && IsMutuallyExhausted(world))
            return ConflictCampaignOutcome.Ceasefire;

        if (stableEvaluations >= 8)
            return ConflictCampaignOutcome.Stalemate;

        return ConflictCampaignOutcome.Ongoing;
    }

    private static bool IsStrategicallyStable(
        double friendlyControl,
        double momentum,
        double controlDelta) =>
        friendlyControl is >= 0.40 and <= 0.60
        && Math.Abs(momentum) <= 0.035
        && Math.Abs(controlDelta) <= 0.01;

    private static bool IsMutuallyExhausted(
        ConflictWorldState world)
    {
        double friendlyPower = SidePower(
            world,
            ConflictSide.Friendly);

        double hostilePower = SidePower(
            world,
            ConflictSide.Hostile);

        double control = AverageFriendlyControl(world);

        return control is >= 0.35 and <= 0.65
            && friendlyPower <= 0.30
            && hostilePower <= 0.30;
    }

    private static double SidePower(
        ConflictWorldState world,
        ConflictSide side) =>
        world.Units
            .Where(unit => unit.Side == side && unit.IsOperational)
            .Sum(unit => unit.Strength * unit.Readiness)
        + world.AirUnits
            .Where(unit => unit.Side == side && unit.IsOperational)
            .Sum(unit => unit.Strength * unit.Readiness * 0.50);

    private static ConflictCampaignPhase DeterminePhase(
        ConflictWorldState world,
        double friendlyControl)
    {
        double hostilePower = world.Units
            .Where(unit => unit.Side == ConflictSide.Hostile && unit.IsOperational)
            .Sum(unit => unit.Strength * unit.Readiness);

        double friendlyPower = world.Units
            .Where(unit => unit.Side == ConflictSide.Friendly && unit.IsOperational)
            .Sum(unit => unit.Strength * unit.Readiness);

        if (friendlyControl >= 0.78 && hostilePower <= Math.Max(0.20, friendlyPower * 0.20))
            return ConflictCampaignPhase.FriendlySecured;

        if (friendlyControl <= 0.22 && friendlyPower <= Math.Max(0.20, hostilePower * 0.20))
            return ConflictCampaignPhase.HostileSecured;

        if (friendlyControl >= 0.58)
            return ConflictCampaignPhase.FriendlyPressure;

        if (friendlyControl <= 0.42)
            return ConflictCampaignPhase.HostilePressure;

        return ConflictCampaignPhase.Contested;
    }

    private static ConflictStrategicObjective[] BuildObjectives(
        ConflictWorldState world)
    {
        var objectives = new List<ConflictStrategicObjective>(4);

        var controlSector = world.Sectors
            .Where(sector => sector.FriendlyControl < 0.65)
            .OrderBy(sector => Math.Abs(sector.FriendlyControl - 0.50))
            .ThenBy(sector => sector.SectorId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (controlSector is not null)
        {
            const double target = 0.65;
            objectives.Add(new(
                $"control:{controlSector.SectorId}",
                StrategicObjectiveKind.GainSectorControl,
                controlSector.SectorId,
                UnitId: null,
                ThreatId: null,
                target,
                ProgressToward(controlSector.FriendlyControl, 0.20, target)));
        }

        var intelligenceSector = world.Sectors
            .Where(sector => sector.IntelligenceConfidence < 0.70)
            .OrderBy(sector => sector.IntelligenceConfidence)
            .ThenBy(sector => sector.SectorId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (intelligenceSector is not null)
        {
            const double target = 0.70;
            objectives.Add(new(
                $"intel:{intelligenceSector.SectorId}",
                StrategicObjectiveKind.ImproveIntelligence,
                intelligenceSector.SectorId,
                UnitId: null,
                ThreatId: null,
                target,
                ProgressToward(intelligenceSector.IntelligenceConfidence, 0, target)));
        }

        var readinessUnit = world.Units
            .Where(unit => unit.Side == ConflictSide.Friendly
                && unit.IsOperational
                && unit.Readiness < 0.75)
            .OrderBy(unit => unit.Readiness)
            .ThenBy(unit => unit.UnitId)
            .FirstOrDefault();

        if (readinessUnit is not null)
        {
            const double target = 0.75;
            objectives.Add(new(
                $"readiness:{readinessUnit.UnitId:N}",
                StrategicObjectiveKind.RestoreFriendlyReadiness,
                SectorId: null,
                readinessUnit.UnitId,
                ThreatId: null,
                target,
                ProgressToward(readinessUnit.Readiness, 0, target)));
        }

        var threat = world.Threats
            .Where(item => item.Side == ConflictSide.Hostile
                && item.Active
                && item.Severity > 0.30)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.ThreatId)
            .FirstOrDefault();

        if (threat is not null)
        {
            const double targetSeverity = 0.30;
            objectives.Add(new(
                $"threat:{threat.ThreatId:N}",
                StrategicObjectiveKind.ReduceHostileThreat,
                SectorId: null,
                UnitId: null,
                threat.ThreatId,
                targetSeverity,
                Math.Clamp((1 - threat.Severity) / (1 - targetSeverity), 0, 1)));
        }

        return objectives.ToArray();
    }

    private static double AverageFriendlyControl(ConflictWorldState world) =>
        world.Sectors.Length == 0
            ? 0.5
            : world.Sectors.Average(sector => sector.FriendlyControl);

    private static double ProgressToward(
        double value,
        double baseline,
        double target)
    {
        if (target <= baseline)
            return value >= target ? 1 : 0;

        return Math.Clamp((value - baseline) / (target - baseline), 0, 1);
    }
}
