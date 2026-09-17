using System.Globalization;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Events;

public static class WorldEventEngine
{
    private const double DaysPerYear = 365.2425;

    public static bool ShouldStart(
        WorldEventDefinition definition,
        double elapsedDays,
        DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        ValidateDefinition(definition);

        if (!double.IsFinite(elapsedDays) || elapsedDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedDays));
        }

        if (definition.AnnualOccurrenceRatePerEligibleScope == 0)
        {
            return false;
        }

        var probability = 1.0 - Math.Exp(
            -definition.AnnualOccurrenceRatePerEligibleScope * elapsedDays / DaysPerYear);

        return random.Chance(Math.Clamp(probability, 0.0, 1.0));
    }

    public static WorldEventInstance Start(
        WorldEventDefinition definition,
        DateTimeOffset startsAt,
        string? scopeTarget,
        DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        ValidateDefinition(definition);

        if (definition.Scope == WorldEventScope.Global)
        {
            if (scopeTarget is not null) throw new ArgumentException("Global events have no target.", nameof(scopeTarget));
        }
        else ArgumentException.ThrowIfNullOrWhiteSpace(scopeTarget);

        var duration = definition.MinimumDurationDays == definition.MaximumDurationDays
            ? definition.MinimumDurationDays
            : random.NextDouble(
                definition.MinimumDurationDays,
                definition.MaximumDurationDays);

        var suffix = random.NextUInt64().ToString("X16", CultureInfo.InvariantCulture);
        var instanceId = string.Create(
            CultureInfo.InvariantCulture,
            $"{definition.EventId}:{startsAt.UtcDateTime.Ticks}:{suffix}");

        return new WorldEventInstance(
            instanceId,
            definition.EventId,
            definition.Name,
            definition.Tier,
            definition.Scope,
            scopeTarget,
            startsAt,
            startsAt.AddDays(duration),
            definition.Effects,
            definition.AffectedMarketSegments);
    }

    public static WorldEventEffects Aggregate(
        IEnumerable<WorldEventInstance> relevantEvents,
        DateTimeOffset time,
        WorldEventLocation? location = null,
        MarketSegment? segment = null)
    {
        ArgumentNullException.ThrowIfNull(relevantEvents);

        var demand = 1.0;
        var capacity = 1.0;
        var cost = 1.0;
        var failureHazard = 1.0;
        var airportServices = 1.0;
        var maintenance = 1.0;
        var finance = 1.0;
        var airspace = AirspaceRestriction.Normal;
        var navigation = NavigationAvailability.Normal;
        var missions = MissionOpportunity.None;

        foreach (var worldEvent in relevantEvents.OrderBy(e => e.InstanceId, StringComparer.Ordinal))
        {
            ValidateInstance(worldEvent);
            if (!worldEvent.IsActiveAt(time) || (location is not null && !location.Matches(worldEvent))
                || (segment is { } marketSegment && !MatchesSegment(worldEvent, marketSegment)))
            {
                continue;
            }

            var effects = worldEvent.Effects;
            demand *= effects.DemandMultiplier;
            capacity *= effects.CapacityMultiplier;
            cost *= effects.OperatingCostMultiplier;
            failureHazard *= effects.AircraftFailureHazardMultiplier;
            airportServices *= effects.AirportServiceCapacityMultiplier;
            maintenance *= effects.MaintenanceCapacityMultiplier;
            finance *= effects.FinanceLiquidityMultiplier;
            airspace = (AirspaceRestriction)Math.Max((int)airspace, (int)effects.AirspaceRestriction);
            navigation = (NavigationAvailability)Math.Max((int)navigation, (int)effects.NavigationAvailability);
            missions |= effects.MissionOpportunities;
        }

        return new WorldEventEffects(
            DemandMultiplier: Math.Clamp(demand, 0.0, 10.0),
            CapacityMultiplier: Math.Clamp(capacity, 0.0, 3.0),
            OperatingCostMultiplier: Math.Clamp(cost, 0.10, 10.0),
            AircraftFailureHazardMultiplier: Math.Clamp(failureHazard, 0.10, 50.0),
            AirportServiceCapacityMultiplier: Math.Clamp(airportServices, 0.0, 3.0),
            MaintenanceCapacityMultiplier: Math.Clamp(maintenance, 0.0, 3.0),
            FinanceLiquidityMultiplier: Math.Clamp(finance, 0.0, 3.0),
            AirspaceRestriction: airspace,
            NavigationAvailability: navigation,
            MissionOpportunities: missions);
    }

    public static bool MatchesSegment(WorldEventInstance instance, MarketSegment segment) =>
        instance.AffectedMarketSegments is null || instance.AffectedMarketSegments.Contains(segment);

    public static void ValidateEffects(WorldEventEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var nonnegative = new[] { effects.DemandMultiplier, effects.CapacityMultiplier,
            effects.AirportServiceCapacityMultiplier, effects.MaintenanceCapacityMultiplier,
            effects.FinanceLiquidityMultiplier };
        if (nonnegative.Any(v => !double.IsFinite(v) || v < 0 || v > 10)
            || !double.IsFinite(effects.OperatingCostMultiplier) || effects.OperatingCostMultiplier is <= 0 or > 10
            || !double.IsFinite(effects.AircraftFailureHazardMultiplier) || effects.AircraftFailureHazardMultiplier is <= 0 or > 50
            || !Enum.IsDefined(effects.AirspaceRestriction) || !Enum.IsDefined(effects.NavigationAvailability))
            throw new ArgumentOutOfRangeException(nameof(effects), "Event effects are invalid.");
    }

    public static void ValidateInstance(WorldEventInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance.DefinitionId);
        if (!Enum.IsDefined(instance.Scope) || !Enum.IsDefined(instance.Tier) || instance.EndsAt <= instance.StartsAt
            || (instance.Scope == WorldEventScope.Global ? instance.ScopeTarget is not null : string.IsNullOrWhiteSpace(instance.ScopeTarget)))
            throw new ArgumentException("Event instance or scope is invalid.", nameof(instance));
        ValidateEffects(instance.Effects);
        if (instance.AffectedMarketSegments is { } segments && (segments.Length == 0 || segments.Any(s => !Enum.IsDefined(s))))
            throw new ArgumentException("Invalid event segment filter.");
    }

    public static void ValidateDefinition(WorldEventDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateEffects(definition.Effects);
        if (definition.AffectedMarketSegments is { } segments && (segments.Length == 0 || segments.Any(s => !Enum.IsDefined(s))))
            throw new ArgumentException("Invalid event segment filter.");
        if (!Enum.IsDefined(definition.Scope) || !Enum.IsDefined(definition.Tier)
            || string.IsNullOrWhiteSpace(definition.EventId)
            || string.IsNullOrWhiteSpace(definition.Name)
            || !double.IsFinite(definition.AnnualOccurrenceRatePerEligibleScope)
            || !double.IsFinite(definition.MinimumDurationDays)
            || !double.IsFinite(definition.MaximumDurationDays)
            || definition.AnnualOccurrenceRatePerEligibleScope < 0
            || definition.MinimumDurationDays <= 0
            || definition.MaximumDurationDays < definition.MinimumDurationDays)
        {
            throw new ArgumentException("World event definition is invalid.", nameof(definition));
        }
    }
}
