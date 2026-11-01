using System.Collections.Immutable;

namespace OpenCareer.Domain.Economy;

[Flags]
public enum CommodityMissionContext
{
    None = 0,
    StandardCargo = 1 << 0,
    ExpressCargo = 1 << 1,
    AogPartsDelivery = 1 << 2,
    Humanitarian = 1 << 3,
    Government = 1 << 4,
    MilitaryLogistics = 1 << 5,
    All = StandardCargo
        | ExpressCargo
        | AogPartsDelivery
        | Humanitarian
        | Government
        | MilitaryLogistics
}

public sealed record CommodityDefinition(
    string CommodityId,
    string DisplayName,
    CargoCommodityCategory Category,
    string Subcategory,
    CargoUnitOfTrade UnitOfTrade,
    double TypicalUnitMassPounds,
    double? TypicalUnitVolumeCubicFeet,
    decimal ReferenceUnitValue,
    CargoHandlingRequirement HandlingRequirements,
    string MarketSegment,
    CommodityMissionContext AllowedMissionContexts,
    TimeSpan? ShelfLife = null,
    double Fragility = 0,
    double TheftRisk = 0,
    string? HazardClassification = null)
{
    public bool IsPerishable =>
        ShelfLife is not null;

    public double ValuePerPound =>
        TypicalUnitMassPounds <= 0
            ? 0
            : (double)ReferenceUnitValue
                / TypicalUnitMassPounds;

    public void Validate()
    {
        ValidateCommodityId(CommodityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Subcategory);
        ArgumentException.ThrowIfNullOrWhiteSpace(MarketSegment);

        if (!Enum.IsDefined(Category))
            throw new ArgumentOutOfRangeException(nameof(Category));

        if (!Enum.IsDefined(UnitOfTrade))
            throw new ArgumentOutOfRangeException(nameof(UnitOfTrade));

        if (!double.IsFinite(TypicalUnitMassPounds)
            || TypicalUnitMassPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TypicalUnitMassPounds));
        }

        if (TypicalUnitVolumeCubicFeet is { } volume
            && (!double.IsFinite(volume) || volume <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(TypicalUnitVolumeCubicFeet));
        }

        LedgerMoney.Validate(
            ReferenceUnitValue,
            nameof(ReferenceUnitValue));

        ValidateUnit(
            Fragility,
            nameof(Fragility));

        ValidateUnit(
            TheftRisk,
            nameof(TheftRisk));

        if ((HandlingRequirements
            & ~AllHandlingRequirements) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HandlingRequirements));
        }

        if ((AllowedMissionContexts
            & ~CommodityMissionContext.All) != 0
            || AllowedMissionContexts == CommodityMissionContext.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AllowedMissionContexts));
        }

        if (ShelfLife is { } shelfLife
            && shelfLife <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShelfLife));
        }

        if (ShelfLife is not null
            && !HandlingRequirements.HasFlag(
                CargoHandlingRequirement.TimeSensitive))
        {
            throw new ArgumentException(
                "Perishable commodities with a shelf life must be marked time-sensitive.",
                nameof(HandlingRequirements));
        }

        bool hazardous =
            HandlingRequirements.HasFlag(
                CargoHandlingRequirement.HazardousMaterials);

        if (hazardous
            && string.IsNullOrWhiteSpace(
                HazardClassification))
        {
            throw new ArgumentException(
                "Hazardous commodities require a hazard classification.",
                nameof(HazardClassification));
        }

        if (!hazardous
            && !string.IsNullOrWhiteSpace(
                HazardClassification))
        {
            throw new ArgumentException(
                "Hazard classification requires hazardous-material handling.",
                nameof(HazardClassification));
        }
    }

    private const CargoHandlingRequirement AllHandlingRequirements =
        CargoHandlingRequirement.Fragile
        | CargoHandlingRequirement.Refrigerated
        | CargoHandlingRequirement.Frozen
        | CargoHandlingRequirement.HighSecurity
        | CargoHandlingRequirement.LiveOrganism
        | CargoHandlingRequirement.TimeSensitive
        | CargoHandlingRequirement.HazardousMaterials;

    private static void ValidateCommodityId(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > 100
            || value[0] is '.' or '-'
            || value[^1] is '.' or '-'
            || value.Any(
                c =>
                    !(c is >= 'a' and <= 'z'
                        or >= '0' and <= '9'
                        or '.'
                        or '-')))
        {
            throw new ArgumentException(
                "Commodity IDs must use stable lowercase ASCII letters, digits, dots or hyphens.",
                nameof(CommodityId));
        }
    }

    private static void ValidateUnit(
        double value,
        string name)
    {
        if (!double.IsFinite(value)
            || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public sealed class CommodityCatalog
{
    private readonly ImmutableDictionary<string, CommodityDefinition>
        _byId;

    private CommodityCatalog(
        ImmutableArray<CommodityDefinition> definitions,
        ImmutableDictionary<string, CommodityDefinition> byId)
    {
        Definitions = definitions;
        _byId = byId;
    }

    public ImmutableArray<CommodityDefinition> Definitions { get; }

    public static CommodityCatalog Create(
        IEnumerable<CommodityDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        CommodityDefinition[] snapshot =
            definitions.ToArray();

        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "Commodity catalog requires at least one definition.",
                nameof(definitions));
        }

        var byId =
            ImmutableDictionary.CreateBuilder<
                string,
                CommodityDefinition>(
                    StringComparer.Ordinal);

        foreach (CommodityDefinition definition in snapshot)
        {
            ArgumentNullException.ThrowIfNull(definition);
            definition.Validate();

            if (!byId.TryAdd(
                    definition.CommodityId,
                    definition))
            {
                throw new ArgumentException(
                    $"Duplicate commodity ID '{definition.CommodityId}'.",
                    nameof(definitions));
            }
        }

        ImmutableArray<CommodityDefinition> ordered =
            snapshot
                .OrderBy(
                    definition =>
                        definition.CommodityId,
                    StringComparer.Ordinal)
                .ToImmutableArray();

        return new CommodityCatalog(
            ordered,
            byId.ToImmutable());
    }

    public bool TryGet(
        string commodityId,
        out CommodityDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commodityId);

        return _byId.TryGetValue(
            commodityId,
            out definition);
    }

    public CommodityDefinition GetRequired(
        string commodityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commodityId);

        return _byId.TryGetValue(
                commodityId,
                out CommodityDefinition? definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Commodity '{commodityId}' is not present in the catalog.");
    }

    public ImmutableArray<CommodityDefinition> ForCategory(
        CargoCommodityCategory category)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));

        return Definitions
            .Where(
                definition =>
                    definition.Category == category)
            .ToImmutableArray();
    }

    public ImmutableArray<CommodityDefinition> ForMissionContext(
        CommodityMissionContext context)
    {
        if (context == CommodityMissionContext.None
            || (context & ~CommodityMissionContext.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        return Definitions
            .Where(
                definition =>
                    (definition.AllowedMissionContexts
                        & context) != 0)
            .ToImmutableArray();
    }
}
