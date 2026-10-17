using System.Collections.Immutable;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Domain.Economy;

public enum CargoUnitOfTrade
{
    Pound,
    Kilogram,
    Unit,
    Case,
    Pallet,
    Crate
}

[Flags]
public enum CargoHandlingRequirement
{
    None = 0,
    Fragile = 1 << 0,
    Refrigerated = 1 << 1,
    Frozen = 1 << 2,
    HighSecurity = 1 << 3,
    LiveOrganism = 1 << 4,
    TimeSensitive = 1 << 5,
    HazardousMaterials = 1 << 6
}

public sealed record AcceptedCargoLot(
    Guid LotId,
    string CommodityId,
    string CommodityName,
    CargoCommodityCategory Category,
    decimal Quantity,
    CargoUnitOfTrade Unit,
    double MassPounds,
    double? VolumeCubicFeet,
    string OriginIcao,
    string DestinationIcao,
    string Shipper,
    string Consignee,
    decimal DeclaredValue,
    decimal? OriginMarketUnitValue,
    decimal? DestinationMarketUnitValue,
    CargoHandlingRequirement HandlingRequirements,
    double AcceptedCondition = 1.0)
{
    public void Validate()
    {
        if (LotId == Guid.Empty)
        {
            throw new ArgumentException(
                "Cargo lot identity is required.",
                nameof(LotId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(CommodityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(CommodityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Shipper);
        ArgumentException.ThrowIfNullOrWhiteSpace(Consignee);

        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(DestinationIcao, nameof(DestinationIcao));

        if (!Enum.IsDefined(Category))
            throw new ArgumentOutOfRangeException(nameof(Category));

        if (!Enum.IsDefined(Unit))
            throw new ArgumentOutOfRangeException(nameof(Unit));

        if ((HandlingRequirements
            & ~AllHandlingRequirements) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HandlingRequirements));
        }

        if (Quantity <= 0m)
            throw new ArgumentOutOfRangeException(nameof(Quantity));

        if (!double.IsFinite(MassPounds)
            || MassPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MassPounds));
        }

        if (VolumeCubicFeet is { } volume
            && (!double.IsFinite(volume) || volume <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(VolumeCubicFeet));
        }

        ValidateMoney(
            DeclaredValue,
            nameof(DeclaredValue));

        ValidateOptionalMoney(
            OriginMarketUnitValue,
            nameof(OriginMarketUnitValue));

        ValidateOptionalMoney(
            DestinationMarketUnitValue,
            nameof(DestinationMarketUnitValue));

        if (!double.IsFinite(AcceptedCondition)
            || AcceptedCondition is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AcceptedCondition));
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

    private static void ValidateOptionalMoney(
        decimal? value,
        string name)
    {
        if (value is { } amount)
            ValidateMoney(amount, name);
    }

    private static void ValidateMoney(
        decimal value,
        string name)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(name);

        if (decimal.Round(
                value,
                2,
                MidpointRounding.AwayFromZero)
            != value)
        {
            throw new ArgumentException(
                "Money values must be expressed to whole cents.",
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

public sealed record AcceptedCargoManifest(
    Guid ContractId,
    string OriginIcao,
    string DestinationIcao,
    DateTimeOffset AcceptedAt,
    ImmutableArray<AcceptedCargoLot> Lots)
{
    public double TotalMassPounds =>
        Lots.Sum(lot => lot.MassPounds);

    public decimal TotalDeclaredValue =>
        Lots.Sum(lot => lot.DeclaredValue);

    public static AcceptedCargoManifest Create(
        JobContract contract,
        IEnumerable<AcceptedCargoLot> lots)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(lots);

        contract.Validate();

        if (contract.Status != ContractStatus.Accepted
            || contract.AcceptedAt is not { } acceptedAt)
        {
            throw new InvalidOperationException(
                "Cargo manifest can only be accepted for an accepted job contract.");
        }

        if (!IsCargoContract(contract.Kind))
        {
            throw new InvalidOperationException(
                "Cargo manifest requires a cargo contract kind.");
        }

        ImmutableArray<AcceptedCargoLot> snapshot =
            lots.ToImmutableArray();

        var manifest =
            new AcceptedCargoManifest(
                contract.ContractId,
                contract.OriginIcao,
                contract.DestinationIcao,
                acceptedAt,
                snapshot);

        manifest.ValidateForContract(contract);
        return manifest;
    }

    public void Validate()
    {
        if (ContractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract identity is required.",
                nameof(ContractId));
        }

        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(DestinationIcao, nameof(DestinationIcao));

        if (Lots.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Accepted cargo manifest requires at least one lot.",
                nameof(Lots));
        }

        var lotIds = new HashSet<Guid>();

        foreach (AcceptedCargoLot lot in Lots)
        {
            ArgumentNullException.ThrowIfNull(lot);
            lot.Validate();

            if (!lotIds.Add(lot.LotId))
            {
                throw new ArgumentException(
                    "Cargo lot identities must be unique.",
                    nameof(Lots));
            }

            if (!string.Equals(
                    lot.OriginIcao,
                    OriginIcao,
                    StringComparison.Ordinal)
                || !string.Equals(
                    lot.DestinationIcao,
                    DestinationIcao,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Cargo lot route must match the accepted manifest route.",
                    nameof(Lots));
            }
        }

        if (!double.IsFinite(TotalMassPounds)
            || TotalMassPounds <= 0)
        {
            throw new ArgumentException(
                "Accepted cargo manifest mass is invalid.",
                nameof(Lots));
        }

        if (TotalDeclaredValue < 0m)
        {
            throw new ArgumentException(
                "Accepted cargo manifest declared value is invalid.",
                nameof(Lots));
        }
    }

    public void ValidateForContract(
        JobContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        Validate();
        contract.Validate();

        if (contract.Status != ContractStatus.Accepted
            || contract.AcceptedAt is not { } acceptedAt)
        {
            throw new InvalidOperationException(
                "Cargo manifest requires an accepted job contract.");
        }

        if (!IsCargoContract(contract.Kind)
            || contract.ContractId != ContractId
            || !string.Equals(
                contract.OriginIcao,
                OriginIcao,
                StringComparison.Ordinal)
            || !string.Equals(
                contract.DestinationIcao,
                DestinationIcao,
                StringComparison.Ordinal)
            || acceptedAt != AcceptedAt)
        {
            throw new InvalidOperationException(
                "Cargo manifest does not match the accepted job contract.");
        }
    }

    private static bool IsCargoContract(
        ContractKind kind) =>
        kind is
            ContractKind.Cargo
            or ContractKind.ExpressCargo
            or ContractKind.AogPartsDelivery;

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
