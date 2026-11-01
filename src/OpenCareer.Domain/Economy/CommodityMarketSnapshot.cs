using System.Collections.Immutable;

namespace OpenCareer.Domain.Economy;

public enum CommodityMarketScope
{
    Airport,
    Region
}

public enum CommodityMarketBalance
{
    Surplus,
    Balanced,
    Scarce
}

public sealed record CommodityMarketEntry(
    string CommodityId,
    double SupplyPressure,
    double DemandPressure,
    decimal UnitPrice,
    double Trend,
    CommodityMarketBalance Balance,
    IReadOnlyList<string> ActiveEventModifierIds,
    double AvailabilityConfidence)
{
    public void Validate()
    {
        ValidateCommodityId(CommodityId);

        ValidatePressure(
            SupplyPressure,
            nameof(SupplyPressure));

        ValidatePressure(
            DemandPressure,
            nameof(DemandPressure));

        if (UnitPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UnitPrice));
        }

        if (!double.IsFinite(Trend)
            || Trend is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Trend));
        }

        if (!Enum.IsDefined(Balance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Balance));
        }

        ArgumentNullException.ThrowIfNull(
            ActiveEventModifierIds);

        if (ActiveEventModifierIds.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Active event modifier IDs cannot be empty.",
                nameof(ActiveEventModifierIds));
        }

        if (ActiveEventModifierIds
            .Distinct(StringComparer.Ordinal)
            .Count()
            != ActiveEventModifierIds.Count)
        {
            throw new ArgumentException(
                "Active event modifier IDs must be unique.",
                nameof(ActiveEventModifierIds));
        }

        if (!double.IsFinite(AvailabilityConfidence)
            || AvailabilityConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AvailabilityConfidence));
        }
    }

    private static void ValidatePressure(
        double value,
        string name)
    {
        if (!double.IsFinite(value)
            || value is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

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
}

public sealed class CommodityMarketSnapshot
{
    private readonly ImmutableDictionary<string, CommodityMarketEntry>
        _byCommodityId;

    private CommodityMarketSnapshot(
        CommodityMarketScope scope,
        string locationId,
        DateTimeOffset capturedAt,
        ImmutableArray<CommodityMarketEntry> commodities,
        ImmutableDictionary<string, CommodityMarketEntry> byCommodityId)
    {
        Scope = scope;
        LocationId = locationId;
        CapturedAt = capturedAt;
        Commodities = commodities;
        _byCommodityId = byCommodityId;
    }

    public CommodityMarketScope Scope { get; }

    public string LocationId { get; }

    public DateTimeOffset CapturedAt { get; }

    public ImmutableArray<CommodityMarketEntry> Commodities { get; }

    public static CommodityMarketSnapshot Create(
        CommodityMarketScope scope,
        string locationId,
        DateTimeOffset capturedAt,
        IEnumerable<CommodityMarketEntry> commodities)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        ValidateLocationId(
            scope,
            locationId);

        ArgumentNullException.ThrowIfNull(commodities);

        CommodityMarketEntry[] source =
            commodities.ToArray();

        var byCommodityId =
            ImmutableDictionary.CreateBuilder<
                string,
                CommodityMarketEntry>(
                    StringComparer.Ordinal);

        foreach (CommodityMarketEntry entry in source)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();

            ImmutableArray<string> eventModifiers =
                entry.ActiveEventModifierIds
                    .OrderBy(
                        id => id,
                        StringComparer.Ordinal)
                    .ToImmutableArray();

            CommodityMarketEntry snapshotEntry =
                entry with
                {
                    ActiveEventModifierIds =
                        eventModifiers
                };

            if (!byCommodityId.TryAdd(
                    snapshotEntry.CommodityId,
                    snapshotEntry))
            {
                throw new ArgumentException(
                    $"Duplicate commodity ID '{snapshotEntry.CommodityId}'.",
                    nameof(commodities));
            }
        }

        ImmutableArray<CommodityMarketEntry> ordered =
            byCommodityId.Values
                .OrderBy(
                    entry =>
                        entry.CommodityId,
                    StringComparer.Ordinal)
                .ToImmutableArray();

        return new CommodityMarketSnapshot(
            scope,
            locationId,
            capturedAt,
            ordered,
            byCommodityId.ToImmutable());
    }

    public bool TryGet(
        string commodityId,
        out CommodityMarketEntry? entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commodityId);

        return _byCommodityId.TryGetValue(
            commodityId,
            out entry);
    }

    public CommodityMarketEntry GetRequired(
        string commodityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commodityId);

        return _byCommodityId.TryGetValue(
                commodityId,
                out CommodityMarketEntry? entry)
            ? entry
            : throw new KeyNotFoundException(
                $"Commodity '{commodityId}' is not present in this market snapshot.");
    }

    private static void ValidateLocationId(
        CommodityMarketScope scope,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (scope == CommodityMarketScope.Airport)
        {
            if (value.Length != 4
                || value.Any(
                    c =>
                        c is < 'A' or > 'Z'))
            {
                throw new ArgumentException(
                    "Airport market snapshots require a normalized four-letter ICAO identifier.",
                    nameof(value));
            }

            return;
        }

        if (value.Length > 100
            || !string.Equals(
                value,
                value.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Region market identifiers must be stable non-empty values without surrounding whitespace.",
                nameof(value));
        }
    }
}
