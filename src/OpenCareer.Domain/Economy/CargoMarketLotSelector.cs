using System.Buffers.Binary;
using System.Globalization;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Economy;

public sealed record CargoMarketLotSelectionRequest(
    ulong GenerationSeed,
    ContractKind ContractKind,
    string OriginIcao,
    string DestinationIcao,
    double MaximumPayloadPounds,
    string Shipper,
    string Consignee,
    CommodityCatalog Catalog,
    CommodityMarketSnapshot OriginMarket,
    CommodityMarketSnapshot DestinationMarket)
{
    public void Validate()
    {
        if (ContractKind is not (
                ContractKind.Cargo
                or ContractKind.ExpressCargo
                or ContractKind.AogPartsDelivery))
        {
            throw new ArgumentOutOfRangeException(nameof(ContractKind));
        }

        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(DestinationIcao, nameof(DestinationIcao));
        ArgumentException.ThrowIfNullOrWhiteSpace(Shipper);
        ArgumentException.ThrowIfNullOrWhiteSpace(Consignee);
        ArgumentNullException.ThrowIfNull(Catalog);
        ArgumentNullException.ThrowIfNull(OriginMarket);
        ArgumentNullException.ThrowIfNull(DestinationMarket);

        if (!double.IsFinite(MaximumPayloadPounds)
            || MaximumPayloadPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumPayloadPounds));
        }

        ValidateAirportMarket(
            OriginMarket,
            OriginIcao,
            nameof(OriginMarket));
        ValidateAirportMarket(
            DestinationMarket,
            DestinationIcao,
            nameof(DestinationMarket));
    }

    private static void ValidateAirportMarket(
        CommodityMarketSnapshot market,
        string expectedIcao,
        string name)
    {
        if (market.Scope != CommodityMarketScope.Airport
            || !string.Equals(
                market.LocationId,
                expectedIcao,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Cargo lot selection requires an airport market matching the route.",
                name);
        }
    }

    private static void ValidateIcao(
        string value,
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != 4
            || value.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A normalized four-letter ICAO identifier is required.",
                name);
        }
    }
}

public static class CargoMarketLotSelector
{
    public static AcceptedCargoLot? Select(
        CargoMarketLotSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        CommodityMissionContext missionContext =
            MissionContext(request.ContractKind);

        CommodityDefinition[] candidates =
            request.Catalog.Definitions
                .Where(
                    definition =>
                        definition.AllowedMissionContexts.HasFlag(
                            missionContext)
                        && FitsContractKind(
                            request.ContractKind,
                            definition)
                        && definition.TypicalUnitMassPounds
                            <= request.MaximumPayloadPounds
                        && IsSupportedLane(
                            request,
                            definition.CommodityId))
                .OrderBy(
                    definition => definition.CommodityId,
                    StringComparer.Ordinal)
                .ToArray();

        if (candidates.Length == 0)
            return null;

        DeterministicRandom random =
            DeterministicSeed.CreateStream(
                request.GenerationSeed,
                StreamKey(request));

        CommodityDefinition selected =
            candidates[random.NextInt(0, candidates.Length)];

        CommodityMarketEntry origin =
            request.OriginMarket.GetRequired(selected.CommodityId);
        CommodityMarketEntry destination =
            request.DestinationMarket.GetRequired(selected.CommodityId);

        decimal quantity =
            decimal.Truncate(
                (decimal)(request.MaximumPayloadPounds
                    / selected.TypicalUnitMassPounds));

        double massPounds =
            (double)quantity
            * selected.TypicalUnitMassPounds;

        double? volumeCubicFeet =
            selected.TypicalUnitVolumeCubicFeet is { } unitVolume
                ? (double)quantity * unitVolume
                : null;

        decimal originUnitValue = RoundMoney(origin.UnitPrice);
        decimal destinationUnitValue = RoundMoney(destination.UnitPrice);
        decimal declaredValue =
            RoundMoney(destinationUnitValue * quantity);

        var lot = new AcceptedCargoLot(
            CreateGuid(random),
            selected.CommodityId,
            selected.DisplayName,
            selected.Category,
            quantity,
            selected.UnitOfTrade,
            massPounds,
            volumeCubicFeet,
            request.OriginIcao,
            request.DestinationIcao,
            request.Shipper,
            request.Consignee,
            declaredValue,
            originUnitValue,
            destinationUnitValue,
            selected.HandlingRequirements);

        lot.Validate();
        return lot;
    }

    private static bool IsSupportedLane(
        CargoMarketLotSelectionRequest request,
        string commodityId)
    {
        if (!request.OriginMarket.TryGet(
                commodityId,
                out CommodityMarketEntry? origin)
            || !request.DestinationMarket.TryGet(
                commodityId,
                out CommodityMarketEntry? destination))
        {
            return false;
        }

        return origin is not null
            && destination is not null
            && origin.AvailabilityConfidence > 0
            && destination.AvailabilityConfidence > 0
            && origin.SupplyPressure > origin.DemandPressure
            && destination.DemandPressure > destination.SupplyPressure
            && destination.UnitPrice > origin.UnitPrice;
    }

    private static bool FitsContractKind(
        ContractKind contractKind,
        CommodityDefinition definition) =>
        contractKind != ContractKind.AogPartsDelivery
        || definition.Category
            == CargoCommodityCategory.AircraftAogParts;

    private static CommodityMissionContext MissionContext(
        ContractKind contractKind) =>
        contractKind switch
        {
            ContractKind.Cargo =>
                CommodityMissionContext.StandardCargo,
            ContractKind.ExpressCargo =>
                CommodityMissionContext.ExpressCargo,
            ContractKind.AogPartsDelivery =>
                CommodityMissionContext.AogPartsDelivery,
            _ => throw new ArgumentOutOfRangeException(
                nameof(contractKind))
        };

    private static string StreamKey(
        CargoMarketLotSelectionRequest request) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"cargo-market-lot:{request.ContractKind}:{request.OriginIcao}:{request.DestinationIcao}:{request.OriginMarket.CapturedAt.UtcTicks}:{request.DestinationMarket.CapturedAt.UtcTicks}");

    private static Guid CreateGuid(
        DeterministicRandom random)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes,
            random.NextUInt64());
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes[8..],
            random.NextUInt64());
        return new Guid(bytes);
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
}
