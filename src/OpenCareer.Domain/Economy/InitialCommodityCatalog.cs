namespace OpenCareer.Domain.Economy;

public static class InitialCommodityCatalog
{
    public static CommodityDefinition GreenCoffeeBeans { get; } =
        new(
            CommodityId: "coffee.green-beans",
            DisplayName: "Green coffee beans",
            Category: CargoCommodityCategory.GeneralFreight,
            Subcategory: "Coffee",
            UnitOfTrade: CargoUnitOfTrade.Pound,
            TypicalUnitMassPounds: 1,
            TypicalUnitVolumeCubicFeet: 0.023,
            ReferenceUnitValue: 4.50m,
            HandlingRequirements: CargoHandlingRequirement.None,
            MarketSegment: "food-beverage",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo);

    public static CommodityDefinition RoastedCoffee { get; } =
        new(
            CommodityId: "coffee.roasted",
            DisplayName: "Roasted coffee",
            Category: CargoCommodityCategory.GeneralFreight,
            Subcategory: "Coffee",
            UnitOfTrade: CargoUnitOfTrade.Pound,
            TypicalUnitMassPounds: 1,
            TypicalUnitVolumeCubicFeet: 0.025,
            ReferenceUnitValue: 7.40m,
            HandlingRequirements: CargoHandlingRequirement.None,
            MarketSegment: "food-beverage",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo);

    public static CommodityDefinition Smartphones { get; } =
        new(
            CommodityId: "electronics.smartphones",
            DisplayName: "Smartphones",
            Category: CargoCommodityCategory.ElectronicsHighValue,
            Subcategory: "Consumer electronics",
            UnitOfTrade: CargoUnitOfTrade.Unit,
            TypicalUnitMassPounds: 0.45,
            TypicalUnitVolumeCubicFeet: 0.01,
            ReferenceUnitValue: 500m,
            HandlingRequirements:
                CargoHandlingRequirement.Fragile
                | CargoHandlingRequirement.HighSecurity,
            MarketSegment: "consumer-electronics",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo,
            Fragility: 0.65,
            TheftRisk: 0.90);

    public static CommodityDefinition Televisions { get; } =
        new(
            CommodityId: "electronics.televisions",
            DisplayName: "Televisions",
            Category: CargoCommodityCategory.GeneralFreight,
            Subcategory: "Consumer electronics",
            UnitOfTrade: CargoUnitOfTrade.Unit,
            TypicalUnitMassPounds: 25,
            TypicalUnitVolumeCubicFeet: 4.5,
            ReferenceUnitValue: 350m,
            HandlingRequirements:
                CargoHandlingRequirement.Fragile,
            MarketSegment: "consumer-electronics",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo,
            Fragility: 0.85,
            TheftRisk: 0.35);

    public static CommodityDefinition Laptops { get; } =
        new(
            CommodityId: "electronics.laptops",
            DisplayName: "Laptops",
            Category: CargoCommodityCategory.ElectronicsHighValue,
            Subcategory: "Consumer electronics",
            UnitOfTrade: CargoUnitOfTrade.Unit,
            TypicalUnitMassPounds: 4,
            TypicalUnitVolumeCubicFeet: 0.12,
            ReferenceUnitValue: 800m,
            HandlingRequirements:
                CargoHandlingRequirement.Fragile
                | CargoHandlingRequirement.HighSecurity,
            MarketSegment: "consumer-electronics",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo,
            Fragility: 0.70,
            TheftRisk: 0.80);

    public static CommodityDefinition Semiconductors { get; } =
        new(
            CommodityId: "electronics.semiconductors",
            DisplayName: "Semiconductor components",
            Category: CargoCommodityCategory.ElectronicsHighValue,
            Subcategory: "Industrial electronics",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 20,
            TypicalUnitVolumeCubicFeet: 1.2,
            ReferenceUnitValue: 12_000m,
            HandlingRequirements:
                CargoHandlingRequirement.Fragile
                | CargoHandlingRequirement.HighSecurity,
            MarketSegment: "industrial-electronics",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo
                | CommodityMissionContext.Government,
            Fragility: 0.80,
            TheftRisk: 0.85);

    public static CommodityDefinition FreshBerries { get; } =
        new(
            CommodityId: "food.fresh-berries",
            DisplayName: "Fresh berries",
            Category: CargoCommodityCategory.Perishables,
            Subcategory: "Fresh produce",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 12,
            TypicalUnitVolumeCubicFeet: 0.7,
            ReferenceUnitValue: 60m,
            HandlingRequirements:
                CargoHandlingRequirement.Refrigerated
                | CargoHandlingRequirement.TimeSensitive
                | CargoHandlingRequirement.Fragile,
            MarketSegment: "food-produce",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo,
            ShelfLife: TimeSpan.FromDays(5),
            Fragility: 0.70);

    public static CommodityDefinition FrozenFood { get; } =
        new(
            CommodityId: "food.frozen",
            DisplayName: "Frozen food",
            Category: CargoCommodityCategory.Perishables,
            Subcategory: "Frozen food",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 30,
            TypicalUnitVolumeCubicFeet: 1.4,
            ReferenceUnitValue: 95m,
            HandlingRequirements:
                CargoHandlingRequirement.Frozen
                | CargoHandlingRequirement.TimeSensitive,
            MarketSegment: "food-cold-chain",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo,
            ShelfLife: TimeSpan.FromDays(120));

    public static CommodityDefinition ShelfStableFood { get; } =
        new(
            CommodityId: "food.shelf-stable",
            DisplayName: "Shelf-stable packaged food",
            Category: CargoCommodityCategory.GeneralFreight,
            Subcategory: "Packaged food",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 25,
            TypicalUnitVolumeCubicFeet: 1.1,
            ReferenceUnitValue: 55m,
            HandlingRequirements: CargoHandlingRequirement.None,
            MarketSegment: "food-packaged",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.Humanitarian
                | CommodityMissionContext.Government
                | CommodityMissionContext.MilitaryLogistics);

    public static CommodityDefinition CutFlowers { get; } =
        new(
            CommodityId: "horticulture.cut-flowers",
            DisplayName: "Cut flowers",
            Category: CargoCommodityCategory.Perishables,
            Subcategory: "Horticulture",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 18,
            TypicalUnitVolumeCubicFeet: 2.5,
            ReferenceUnitValue: 180m,
            HandlingRequirements:
                CargoHandlingRequirement.Refrigerated
                | CargoHandlingRequirement.TimeSensitive
                | CargoHandlingRequirement.Fragile,
            MarketSegment: "horticulture",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo,
            ShelfLife: TimeSpan.FromDays(7),
            Fragility: 0.90);

    public static CommodityDefinition NurseryPlants { get; } =
        new(
            CommodityId: "horticulture.nursery-plants",
            DisplayName: "Nursery plants",
            Category: CargoCommodityCategory.Perishables,
            Subcategory: "Horticulture",
            UnitOfTrade: CargoUnitOfTrade.Unit,
            TypicalUnitMassPounds: 8,
            TypicalUnitVolumeCubicFeet: 1.8,
            ReferenceUnitValue: 45m,
            HandlingRequirements:
                CargoHandlingRequirement.LiveOrganism
                | CargoHandlingRequirement.TimeSensitive
                | CargoHandlingRequirement.Fragile,
            MarketSegment: "horticulture",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo,
            ShelfLife: TimeSpan.FromDays(14),
            Fragility: 0.75);

    public static CommodityDefinition MedicalSupplies { get; } =
        new(
            CommodityId: "medical.supplies",
            DisplayName: "Medical supplies",
            Category: CargoCommodityCategory.MedicalSupplies,
            Subcategory: "Healthcare supplies",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 15,
            TypicalUnitVolumeCubicFeet: 1,
            ReferenceUnitValue: 2_500m,
            HandlingRequirements:
                CargoHandlingRequirement.HighSecurity
                | CargoHandlingRequirement.TimeSensitive,
            MarketSegment: "healthcare",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo
                | CommodityMissionContext.Humanitarian
                | CommodityMissionContext.Government
                | CommodityMissionContext.MilitaryLogistics,
            TheftRisk: 0.60);

    public static CommodityDefinition AircraftAogParts { get; } =
        new(
            CommodityId: "parts.aircraft-aog",
            DisplayName: "Aircraft AOG parts",
            Category: CargoCommodityCategory.AircraftAogParts,
            Subcategory: "Aircraft parts",
            UnitOfTrade: CargoUnitOfTrade.Crate,
            TypicalUnitMassPounds: 180,
            TypicalUnitVolumeCubicFeet: 8,
            ReferenceUnitValue: 18_000m,
            HandlingRequirements:
                CargoHandlingRequirement.TimeSensitive
                | CargoHandlingRequirement.HighSecurity,
            MarketSegment: "aviation-maintenance",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo
                | CommodityMissionContext.AogPartsDelivery
                | CommodityMissionContext.Government
                | CommodityMissionContext.MilitaryLogistics,
            TheftRisk: 0.55);

    public static CommodityDefinition MachineParts { get; } =
        new(
            CommodityId: "parts.machine",
            DisplayName: "Machine parts",
            Category: CargoCommodityCategory.IndustrialParts,
            Subcategory: "Industrial machinery",
            UnitOfTrade: CargoUnitOfTrade.Crate,
            TypicalUnitMassPounds: 250,
            TypicalUnitVolumeCubicFeet: 10,
            ReferenceUnitValue: 6_000m,
            HandlingRequirements: CargoHandlingRequirement.None,
            MarketSegment: "industrial-manufacturing",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.Government
                | CommodityMissionContext.MilitaryLogistics);

    public static CommodityDefinition Textiles { get; } =
        new(
            CommodityId: "textiles.finished-goods",
            DisplayName: "Finished textiles",
            Category: CargoCommodityCategory.GeneralFreight,
            Subcategory: "Textiles",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 40,
            TypicalUnitVolumeCubicFeet: 3,
            ReferenceUnitValue: 320m,
            HandlingRequirements: CargoHandlingRequirement.None,
            MarketSegment: "textiles-apparel",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo);

    public static CommodityDefinition ExpressParcels { get; } =
        new(
            CommodityId: "parcel.express",
            DisplayName: "Express parcels",
            Category: CargoCommodityCategory.ExpressParcel,
            Subcategory: "Parcel",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 20,
            TypicalUnitVolumeCubicFeet: 1.5,
            ReferenceUnitValue: 250m,
            HandlingRequirements:
                CargoHandlingRequirement.TimeSensitive,
            MarketSegment: "parcel-logistics",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo);

    public static CommodityDefinition Mail { get; } =
        new(
            CommodityId: "mail.general",
            DisplayName: "Mail",
            Category: CargoCommodityCategory.Mail,
            Subcategory: "Mail",
            UnitOfTrade: CargoUnitOfTrade.Case,
            TypicalUnitMassPounds: 25,
            TypicalUnitVolumeCubicFeet: 1.8,
            ReferenceUnitValue: 100m,
            HandlingRequirements:
                CargoHandlingRequirement.TimeSensitive,
            MarketSegment: "mail-logistics",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo
                | CommodityMissionContext.Government);
    public static CommodityCatalog Default { get; } =
        CommodityCatalog.Create(
        [
            GreenCoffeeBeans,
            RoastedCoffee,
            Smartphones,
            Televisions,
            Laptops,
            Semiconductors,
            FreshBerries,
            FrozenFood,
            ShelfStableFood,
            CutFlowers,
            NurseryPlants,
            MedicalSupplies,
            AircraftAogParts,
            MachineParts,
            Textiles,
            ExpressParcels,
            Mail
        ]);
}
