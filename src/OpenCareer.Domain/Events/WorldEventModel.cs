using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Events;

public enum WorldEventTier
{
    LocalOperational = 0,
    RegionalDisruption = 1,
    IndustryShock = 2,
    NationalEmergency = 3,
    GlobalSystemic = 4,
    BlackSwan = 5
}

public enum WorldEventScope
{
    Airport,
    Region,
    FleetType,
    National,
    Global
}

public enum AirspaceRestriction
{
    Normal = 0,
    FlowRestricted = 1,
    AuthorizedOperationsOnly = 2,
    MilitaryAndEmergencyAuthorizedOnly = 3,
    Closed = 4
}

public enum NavigationAvailability
{
    Normal = 0,
    Degraded = 1,
    GpsUnavailable = 2,
    SeverelyDegraded = 3
}

[Flags]
public enum MissionOpportunity
{
    None = 0,
    Firefighting = 1 << 0,
    DisasterRelief = 1 << 1,
    EmergencyMedical = 1 << 2,
    Evacuation = 1 << 3,
    SearchAndRescue = 1 << 4,
    AogCourier = 1 << 5,
    Charter = 1 << 11,
    Cargo = 1 << 12,
    Reposition = 1 << 13,
    GovernmentCourier = 1 << 6,
    MilitaryIntercept = 1 << 7,
    MilitaryEscort = 1 << 8,
    MilitaryPatrol = 1 << 9,
    MilitaryTransport = 1 << 10
}

public sealed record WorldEventEffects(
    double DemandMultiplier = 1.0,
    double CapacityMultiplier = 1.0,
    double OperatingCostMultiplier = 1.0,
    double AircraftFailureHazardMultiplier = 1.0,
    double AirportServiceCapacityMultiplier = 1.0,
    double MaintenanceCapacityMultiplier = 1.0,
    double FinanceLiquidityMultiplier = 1.0,
    AirspaceRestriction AirspaceRestriction = AirspaceRestriction.Normal,
    NavigationAvailability NavigationAvailability = NavigationAvailability.Normal,
    MissionOpportunity MissionOpportunities = MissionOpportunity.None);

public sealed record WorldEventDefinition(
    string EventId,
    string Name,
    WorldEventTier Tier,
    WorldEventScope Scope,
    double AnnualOccurrenceRatePerEligibleScope,
    double MinimumDurationDays,
    double MaximumDurationDays,
    WorldEventEffects Effects,
    MarketSegment[]? AffectedMarketSegments = null);

public sealed record WorldEventInstance(
    string InstanceId,
    string DefinitionId,
    string Name,
    WorldEventTier Tier,
    WorldEventScope Scope,
    string? ScopeTarget,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    WorldEventEffects Effects,
    MarketSegment[]? AffectedMarketSegments = null)
{
    public bool IsActiveAt(DateTimeOffset time) =>
        StartsAt <= time && time < EndsAt;
}
