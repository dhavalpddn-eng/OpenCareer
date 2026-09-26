using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class CommodityCatalogTests
{
    [Fact]
    public void CatalogSnapshotsDefinitionsAndUsesStableIdOrdering()
    {
        var source =
            new List<CommodityDefinition>
            {
                Smartphones(),
                RoastedCoffee()
            };

        CommodityCatalog catalog =
            CommodityCatalog.Create(source);

        source.Clear();

        Assert.Equal(
            [
                "coffee.roasted",
                "electronics.smartphones"
            ],
            catalog.Definitions
                .Select(x => x.CommodityId)
                .ToArray());

        Assert.Equal(
            "Roasted coffee",
            catalog
                .GetRequired("coffee.roasted")
                .DisplayName);

        Assert.True(
            catalog.TryGet(
                "electronics.smartphones",
                out CommodityDefinition? phones));

        Assert.Equal(
            CargoCommodityCategory.ElectronicsHighValue,
            phones!.Category);
    }

    [Fact]
    public void DuplicateCommodityIdsAreRejected()
    {
        CommodityDefinition coffee =
            RoastedCoffee();

        Assert.Throws<ArgumentException>(
            () => CommodityCatalog.Create(
                [
                    coffee,
                    coffee with
                    {
                        DisplayName =
                            "Duplicate coffee"
                    }
                ]));
    }

    [Fact]
    public void CommodityIdsMustBeStableLowercaseKeys()
    {
        CommodityDefinition invalid =
            RoastedCoffee() with
            {
                CommodityId =
                    "Coffee Roasted"
            };

        Assert.Throws<ArgumentException>(
            invalid.Validate);
    }

    [Fact]
    public void PerishableCommodityRequiresTimeSensitiveHandling()
    {
        CommodityDefinition berries =
            RoastedCoffee() with
            {
                CommodityId =
                    "food.fresh-berries",
                DisplayName =
                    "Fresh berries",
                Category =
                    CargoCommodityCategory.Perishables,
                Subcategory =
                    "Fresh produce",
                ShelfLife =
                    TimeSpan.FromDays(5),
                HandlingRequirements =
                    CargoHandlingRequirement.Refrigerated
            };

        Assert.Throws<ArgumentException>(
            berries.Validate);

        berries =
            berries with
            {
                HandlingRequirements =
                    CargoHandlingRequirement.Refrigerated
                    | CargoHandlingRequirement.TimeSensitive
            };

        berries.Validate();
        Assert.True(berries.IsPerishable);
    }

    [Fact]
    public void HazardClassificationMatchesHazardHandling()
    {
        CommodityDefinition hazardous =
            RoastedCoffee() with
            {
                CommodityId =
                    "industrial.test-hazard",
                DisplayName =
                    "Test hazardous freight",
                Category =
                    CargoCommodityCategory.IndustrialParts,
                Subcategory =
                    "Regulated test freight",
                HandlingRequirements =
                    CargoHandlingRequirement.HazardousMaterials,
                HazardClassification = null
            };

        Assert.Throws<ArgumentException>(
            hazardous.Validate);

        hazardous =
            hazardous with
            {
                HazardClassification =
                    "Regulated cargo"
            };

        hazardous.Validate();
    }

    [Fact]
    public void CatalogFiltersByCategoryAndMissionContextDeterministically()
    {
        CommodityCatalog catalog =
            CommodityCatalog.Create(
                [
                    RoastedCoffee(),
                    Smartphones()
                ]);

        Assert.Equal(
            ["electronics.smartphones"],
            catalog
                .ForCategory(
                    CargoCommodityCategory.ElectronicsHighValue)
                .Select(x => x.CommodityId)
                .ToArray());

        Assert.Equal(
            ["electronics.smartphones"],
            catalog
                .ForMissionContext(
                    CommodityMissionContext.ExpressCargo)
                .Select(x => x.CommodityId)
                .ToArray());
    }

    [Fact]
    public void PhysicalAndReferenceValueMetadataSupportsValueDensity()
    {
        CommodityDefinition coffee =
            RoastedCoffee();

        CommodityDefinition phones =
            Smartphones();

        coffee.Validate();
        phones.Validate();

        Assert.True(
            phones.ValuePerPound
            > coffee.ValuePerPound);

        Assert.Equal(
            7.40m,
            coffee.ReferenceUnitValue);

        Assert.Equal(
            CargoUnitOfTrade.Pound,
            coffee.UnitOfTrade);

        Assert.True(
            phones.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.HighSecurity));
    }

    private static CommodityDefinition RoastedCoffee() =>
        new(
            CommodityId:
                "coffee.roasted",
            DisplayName:
                "Roasted coffee",
            Category:
                CargoCommodityCategory.GeneralFreight,
            Subcategory:
                "Coffee",
            UnitOfTrade:
                CargoUnitOfTrade.Pound,
            TypicalUnitMassPounds:
                1,
            TypicalUnitVolumeCubicFeet:
                0.025,
            ReferenceUnitValue:
                7.40m,
            HandlingRequirements:
                CargoHandlingRequirement.None,
            MarketSegment:
                "food-beverage",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo);

    private static CommodityDefinition Smartphones() =>
        new(
            CommodityId:
                "electronics.smartphones",
            DisplayName:
                "Smartphones",
            Category:
                CargoCommodityCategory.ElectronicsHighValue,
            Subcategory:
                "Consumer electronics",
            UnitOfTrade:
                CargoUnitOfTrade.Unit,
            TypicalUnitMassPounds:
                0.45,
            TypicalUnitVolumeCubicFeet:
                0.01,
            ReferenceUnitValue:
                500m,
            HandlingRequirements:
                CargoHandlingRequirement.HighSecurity
                | CargoHandlingRequirement.Fragile,
            MarketSegment:
                "consumer-electronics",
            AllowedMissionContexts:
                CommodityMissionContext.StandardCargo
                | CommodityMissionContext.ExpressCargo,
            Fragility:
                0.65,
            TheftRisk:
                0.90);
}
