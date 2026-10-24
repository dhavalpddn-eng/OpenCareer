using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class CommodityMarketSnapshotTests
{
    [Fact]
    public void SnapshotCopiesAndOrdersCommodityAndEventData()
    {
        var phoneEvents =
            new List<string>
            {
                "weather.disruption",
                "consumer.launch"
            };

        var source =
            new List<CommodityMarketEntry>
            {
                Entry(
                    commodityId:
                        "electronics.smartphones",
                    eventIds:
                        phoneEvents),
                Entry(
                    commodityId:
                        "coffee.roasted",
                    eventIds:
                        [])
            };

        DateTimeOffset capturedAt =
            new(
                2026,
                9,
                21,
                18,
                0,
                0,
                TimeSpan.Zero);

        CommodityMarketSnapshot snapshot =
            CommodityMarketSnapshot.Create(
                CommodityMarketScope.Airport,
                "KDFW",
                capturedAt,
                source);

        source.Clear();
        phoneEvents.Add(
            "late.mutation");

        Assert.Equal(
            CommodityMarketScope.Airport,
            snapshot.Scope);
        Assert.Equal(
            "KDFW",
            snapshot.LocationId);
        Assert.Equal(
            capturedAt,
            snapshot.CapturedAt);

        Assert.Equal(
            [
                "coffee.roasted",
                "electronics.smartphones"
            ],
            snapshot.Commodities
                .Select(x => x.CommodityId)
                .ToArray());

        Assert.Equal(
            [
                "consumer.launch",
                "weather.disruption"
            ],
            snapshot
                .GetRequired(
                    "electronics.smartphones")
                .ActiveEventModifierIds);

        Assert.DoesNotContain(
            "late.mutation",
            snapshot
                .GetRequired(
                    "electronics.smartphones")
                .ActiveEventModifierIds);
    }

    [Fact]
    public void SnapshotRejectsDuplicateCommodityIds()
    {
        CommodityMarketEntry entry =
            Entry(
                commodityId:
                    "coffee.roasted",
                eventIds:
                    []);

        Assert.Throws<ArgumentException>(
            () =>
                CommodityMarketSnapshot.Create(
                    CommodityMarketScope.Airport,
                    "KDFW",
                    DateTimeOffset.UtcNow,
                    [
                        entry,
                        entry with
                        {
                            UnitPrice =
                                entry.UnitPrice + 1m
                        }
                    ]));
    }

    [Fact]
    public void EntryValidatesMarketSignalRanges()
    {
        CommodityMarketEntry valid =
            Entry(
                commodityId:
                    "coffee.roasted",
                eventIds:
                    []);

        valid.Validate();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                (valid with
                {
                    SupplyPressure = 4.01
                }).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                (valid with
                {
                    DemandPressure = -0.01
                }).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                (valid with
                {
                    UnitPrice = 0m
                }).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                (valid with
                {
                    Trend = 1.01
                }).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                (valid with
                {
                    AvailabilityConfidence = 1.01
                }).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                (valid with
                {
                    Balance =
                        (CommodityMarketBalance)999
                }).Validate());
    }

    [Fact]
    public void EntryRejectsDuplicateOrInvalidEventModifierIds()
    {
        CommodityMarketEntry duplicate =
            Entry(
                commodityId:
                    "coffee.roasted",
                eventIds:
                    [
                        "event.one",
                        "event.one"
                    ]);

        Assert.Throws<ArgumentException>(
            duplicate.Validate);

        CommodityMarketEntry blank =
            Entry(
                commodityId:
                    "coffee.roasted",
                eventIds:
                    [
                        "event.one",
                        ""
                    ]);

        Assert.Throws<ArgumentException>(
            blank.Validate);
    }

    [Fact]
    public void SnapshotSupportsAirportAndRegionScopes()
    {
        CommodityMarketSnapshot region =
            CommodityMarketSnapshot.Create(
                CommodityMarketScope.Region,
                "north-america",
                DateTimeOffset.UtcNow,
                []);

        Assert.Equal(
            CommodityMarketScope.Region,
            region.Scope);
        Assert.Equal(
            "north-america",
            region.LocationId);

        Assert.Throws<ArgumentException>(
            () =>
                CommodityMarketSnapshot.Create(
                    CommodityMarketScope.Airport,
                    "kdfw",
                    DateTimeOffset.UtcNow,
                    []));

        Assert.Throws<ArgumentException>(
            () =>
                CommodityMarketSnapshot.Create(
                    CommodityMarketScope.Region,
                    " north-america ",
                    DateTimeOffset.UtcNow,
                    []));
    }

    private static CommodityMarketEntry Entry(
        string commodityId,
        IReadOnlyList<string> eventIds) =>
        new(
            CommodityId:
                commodityId,
            SupplyPressure:
                1.2,
            DemandPressure:
                1.8,
            UnitPrice:
                12.50m,
            Trend:
                0.15,
            Balance:
                CommodityMarketBalance.Scarce,
            ActiveEventModifierIds:
                eventIds,
            AvailabilityConfidence:
                0.85);
}
