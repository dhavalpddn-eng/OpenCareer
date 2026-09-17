using System.Globalization;
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
            definition.Effects);
    }

    public static WorldEventEffects Aggregate(
        IEnumerable<WorldEventInstance> relevantEvents,
        DateTimeOffset time)
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

        foreach (var worldEvent in relevantEvents)
        {
            if (!worldEvent.IsActiveAt(time))
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

    private static void ValidateDefinition(WorldEventDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.EventId)
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
