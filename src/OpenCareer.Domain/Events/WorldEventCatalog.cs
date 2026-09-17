using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Events;

public static class WorldEventCatalog
{
    public static IReadOnlyList<WorldEventDefinition> Definitions { get; } = Array.AsReadOnly(
        new WorldEventDefinition[]
        {
            // Provisional opportunity cadence, measured in career time per eligible scope.
            new("local-charter-demand", "Local Charter Demand", WorldEventTier.LocalOperational,
                WorldEventScope.Airport, 18, 0.5, 2,
                new WorldEventEffects(DemandMultiplier: 1.12, MissionOpportunities: MissionOpportunity.Charter),
                new[] { MarketSegment.GeneralPassenger, MarketSegment.BusinessCharter, MarketSegment.LeisureCharter }),
            new("regional-cargo-surge", "Regional Cargo Surge", WorldEventTier.LocalOperational,
                WorldEventScope.Region, 12, 1, 3,
                new WorldEventEffects(DemandMultiplier: 1.15, MissionOpportunities: MissionOpportunity.Cargo),
                new[] { MarketSegment.GeneralCargo, MarketSegment.ExpressCargo }),
            new("operator-reposition-request", "Operator Repositioning Request", WorldEventTier.LocalOperational,
                WorldEventScope.Airport, 24, 0.25, 1,
                new WorldEventEffects(MissionOpportunities: MissionOpportunity.Reposition)),
            new(
                "market-crash",
                "Global Market Crash",
                WorldEventTier.GlobalSystemic,
                WorldEventScope.Global,
                0.08,
                30,
                180,
                new WorldEventEffects(
                    DemandMultiplier: 0.72,
                    OperatingCostMultiplier: 0.95,
                    FinanceLiquidityMultiplier: 0.35)),

            new(
                "fuel-supply-crisis",
                "Regional Aviation Fuel Supply Crisis",
                WorldEventTier.RegionalDisruption,
                WorldEventScope.Region,
                0.18,
                7,
                45,
                new WorldEventEffects(
                    DemandMultiplier: 0.95,
                    CapacityMultiplier: 0.90,
                    OperatingCostMultiplier: 1.55,
                    AirportServiceCapacityMultiplier: 0.90,
                    MissionOpportunities: MissionOpportunity.AogCourier)),

            new(
                "fleet-engine-grounding",
                "Fleet Engine Emergency Grounding",
                WorldEventTier.IndustryShock,
                WorldEventScope.FleetType,
                0.05,
                14,
                120,
                new WorldEventEffects(
                    CapacityMultiplier: 0.65,
                    OperatingCostMultiplier: 1.12,
                    MaintenanceCapacityMultiplier: 0.55,
                    MissionOpportunities: MissionOpportunity.AogCourier)),

            new(
                "gps-regional-outage",
                "Regional GPS Outage",
                WorldEventTier.RegionalDisruption,
                WorldEventScope.Region,
                0.15,
                1,
                7,
                new WorldEventEffects(
                    CapacityMultiplier: 0.88,
                    NavigationAvailability: NavigationAvailability.GpsUnavailable,
                    MissionOpportunities: MissionOpportunity.GovernmentCourier)),

            new(
                "national-airspace-security-emergency",
                "National Airspace Security Emergency",
                WorldEventTier.BlackSwan,
                WorldEventScope.National,
                0.01,
                1,
                4,
                new WorldEventEffects(
                    DemandMultiplier: 0.35,
                    CapacityMultiplier: 0.03,
                    OperatingCostMultiplier: 1.20,
                    AirspaceRestriction: AirspaceRestriction.MilitaryAndEmergencyAuthorizedOnly,
                    MissionOpportunities: MissionOpportunity.EmergencyMedical
                        | MissionOpportunity.GovernmentCourier
                        | MissionOpportunity.MilitaryIntercept
                        | MissionOpportunity.MilitaryEscort
                        | MissionOpportunity.MilitaryPatrol)),

            new(
                "nuclear-warning",
                "Strategic Nuclear Warning",
                WorldEventTier.BlackSwan,
                WorldEventScope.National,
                0.003,
                0.25,
                2,
                new WorldEventEffects(
                    DemandMultiplier: 0.45,
                    CapacityMultiplier: 0.02,
                    OperatingCostMultiplier: 1.25,
                    AirspaceRestriction: AirspaceRestriction.MilitaryAndEmergencyAuthorizedOnly,
                    MissionOpportunities: MissionOpportunity.EmergencyMedical
                        | MissionOpportunity.Evacuation
                        | MissionOpportunity.GovernmentCourier
                        | MissionOpportunity.MilitaryIntercept
                        | MissionOpportunity.MilitaryEscort
                        | MissionOpportunity.MilitaryTransport)),

            new(
                "major-wildfire-complex",
                "Major Wildfire Complex",
                WorldEventTier.RegionalDisruption,
                WorldEventScope.Region,
                0.60,
                3,
                30,
                new WorldEventEffects(
                    DemandMultiplier: 1.20,
                    CapacityMultiplier: 0.95,
                    OperatingCostMultiplier: 1.08,
                    AirportServiceCapacityMultiplier: 0.85,
                    MissionOpportunities: MissionOpportunity.Firefighting
                        | MissionOpportunity.DisasterRelief
                        | MissionOpportunity.Evacuation
                        | MissionOpportunity.SearchAndRescue)),

            new(
                "hurricane-landfall",
                "Major Hurricane Landfall",
                WorldEventTier.RegionalDisruption,
                WorldEventScope.Region,
                0.25,
                3,
                14,
                new WorldEventEffects(
                    DemandMultiplier: 1.40,
                    CapacityMultiplier: 0.55,
                    OperatingCostMultiplier: 1.20,
                    AirportServiceCapacityMultiplier: 0.40,
                    MissionOpportunities: MissionOpportunity.DisasterRelief
                        | MissionOpportunity.Evacuation
                        | MissionOpportunity.EmergencyMedical
                        | MissionOpportunity.SearchAndRescue)),

            new(
                "solar-superstorm",
                "Severe Geomagnetic Storm",
                WorldEventTier.GlobalSystemic,
                WorldEventScope.Global,
                0.025,
                1,
                5,
                new WorldEventEffects(
                    CapacityMultiplier: 0.80,
                    NavigationAvailability: NavigationAvailability.GpsUnavailable,
                    MissionOpportunities: MissionOpportunity.GovernmentCourier
                        | MissionOpportunity.MilitaryPatrol)),

            new(
                "airport-ground-services-failure",
                "Airport Ground Services Failure",
                WorldEventTier.LocalOperational,
                WorldEventScope.Airport,
                0.70,
                0.2,
                2,
                new WorldEventEffects(
                    CapacityMultiplier: 0.65,
                    AirportServiceCapacityMultiplier: 0.35)),

            new(
                "mro-parts-shortage",
                "Aircraft Parts and MRO Shortage",
                WorldEventTier.IndustryShock,
                WorldEventScope.National,
                0.30,
                14,
                90,
                new WorldEventEffects(
                    OperatingCostMultiplier: 1.15,
                    MaintenanceCapacityMultiplier: 0.50,
                    MissionOpportunities: MissionOpportunity.AogCourier))
        });
}
