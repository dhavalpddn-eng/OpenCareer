using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class InitialCommodityCatalogTests
{
    [Fact]
    public void DefaultCatalogContainsStableNamedGoods()
    {
        CommodityCatalog catalog =
            InitialCommodityCatalog.Default;

        string[] ids =
            catalog.Definitions
                .Select(x => x.CommodityId)
                .ToArray();

        Assert.Equal(
            ids.OrderBy(
                x => x,
                StringComparer.Ordinal),
            ids);

        Assert.Contains(
            "coffee.green-beans",
            ids);
        Assert.Contains(
            "coffee.roasted",
            ids);
        Assert.Contains(
            "electronics.smartphones",
            ids);
        Assert.Contains(
            "electronics.televisions",
            ids);
        Assert.Contains(
            "food.fresh-berries",
            ids);
        Assert.Contains(
            "food.frozen",
            ids);
        Assert.Contains(
            "food.shelf-stable",
            ids);
        Assert.Contains(
            "horticulture.cut-flowers",
            ids);
        Assert.Contains(
            "horticulture.nursery-plants",
            ids);
        Assert.Contains(
            "medical.supplies",
            ids);
        Assert.Contains(
            "parts.aircraft-aog",
            ids);
        Assert.Contains(
            "parts.machine",
            ids);
        Assert.Contains(
            "textiles.finished-goods",
            ids);
        Assert.Contains(
            "parcel.express",
            ids);
        Assert.Contains(
            "mail.general",
            ids);
    }

    [Fact]
    public void EveryBuiltInDefinitionValidates()
    {
        Assert.All(
            InitialCommodityCatalog.Default.Definitions,
            definition =>
                definition.Validate());
    }

    [Fact]
    public void ElectronicsRemainHighValueAndHandlingSensitive()
    {
        CommodityDefinition phones =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "electronics.smartphones");

        CommodityDefinition televisions =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "electronics.televisions");

        Assert.Equal(
            CargoCommodityCategory.ElectronicsHighValue,
            phones.Category);
        Assert.True(
            phones.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.HighSecurity));
        Assert.True(
            phones.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.Fragile));

        Assert.True(
            televisions.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.Fragile));

        Assert.True(
            phones.ValuePerPound
            > televisions.ValuePerPound);
    }

    [Fact]
    public void FoodAndHorticulturePreserveDistinctHandlingProfiles()
    {
        CommodityDefinition berries =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "food.fresh-berries");

        CommodityDefinition frozen =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "food.frozen");

        CommodityDefinition shelfStable =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "food.shelf-stable");

        CommodityDefinition plants =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "horticulture.nursery-plants");

        Assert.True(berries.IsPerishable);
        Assert.True(
            berries.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.Refrigerated));

        Assert.True(frozen.IsPerishable);
        Assert.True(
            frozen.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.Frozen));

        Assert.False(
            shelfStable.IsPerishable);

        Assert.True(
            plants.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.LiveOrganism));
        Assert.True(
            plants.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.TimeSensitive));
    }

    [Fact]
    public void AogPartsAndMedicalSuppliesExposeSpecialMissionContexts()
    {
        CommodityDefinition aog =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "parts.aircraft-aog");

        CommodityDefinition medical =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "medical.supplies");

        Assert.True(
            aog.AllowedMissionContexts.HasFlag(
                CommodityMissionContext.AogPartsDelivery));

        Assert.True(
            aog.HandlingRequirements.HasFlag(
                CargoHandlingRequirement.TimeSensitive));

        Assert.True(
            medical.AllowedMissionContexts.HasFlag(
                CommodityMissionContext.Humanitarian));

        Assert.True(
            medical.AllowedMissionContexts.HasFlag(
                CommodityMissionContext.Government));
    }

    [Fact]
    public void DemandCategoriesHaveRepresentativeBuiltInGoods()
    {
        CommodityCatalog catalog =
            InitialCommodityCatalog.Default;

        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.GeneralFreight));
        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.ExpressParcel));
        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.Mail));
        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.Perishables));
        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.MedicalSupplies));
        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.AircraftAogParts));
        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.IndustrialParts));
        Assert.NotEmpty(
            catalog.ForCategory(
                CargoCommodityCategory.ElectronicsHighValue));
    }

    [Fact]
    public void BuiltInReferenceValuesRemainGameplayDataOnly()
    {
        CommodityDefinition coffee =
            InitialCommodityCatalog.Default
                .GetRequired(
                    "coffee.roasted");

        Assert.Equal(
            7.40m,
            coffee.ReferenceUnitValue);

        Assert.Equal(
            "food-beverage",
            coffee.MarketSegment);
    }
}
