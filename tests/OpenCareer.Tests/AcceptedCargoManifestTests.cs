using System.Collections.Immutable;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class AcceptedCargoManifestTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptedCargoContractProducesImmutableManifestSnapshot()
    {
        JobContract contract =
            AcceptedContract();

        AcceptedCargoLot coffee =
            Lot(
                Guid.Parse(
                    "94000000-0000-0000-0000-000000000001"),
                "coffee.roasted",
                "Roasted coffee",
                CargoCommodityCategory.GeneralFreight,
                quantity: 600m,
                massPounds: 600,
                declaredValue: 4_440m,
                originUnitValue: 7.40m,
                destinationUnitValue: 8.05m);

        AcceptedCargoLot phones =
            Lot(
                Guid.Parse(
                    "94000000-0000-0000-0000-000000000002"),
                "electronics.smartphones",
                "Smartphones",
                CargoCommodityCategory.ElectronicsHighValue,
                quantity: 100m,
                massPounds: 120,
                declaredValue: 50_000m,
                originUnitValue: 480m,
                destinationUnitValue: 520m,
                CargoHandlingRequirement.HighSecurity);

        var source =
            new List<AcceptedCargoLot>
            {
                coffee,
                phones
            };

        AcceptedCargoManifest manifest =
            AcceptedCargoManifest.Create(
                contract,
                source);

        source.Clear();

        Assert.Equal(
            contract.ContractId,
            manifest.ContractId);
        Assert.Equal(
            contract.AcceptedAt,
            manifest.AcceptedAt);
        Assert.Equal(
            2,
            manifest.Lots.Length);
        Assert.Equal(
            720d,
            manifest.TotalMassPounds);
        Assert.Equal(
            54_440m,
            manifest.TotalDeclaredValue);
        Assert.Equal(
            "Roasted coffee",
            manifest.Lots[0].CommodityName);
    }

    [Fact]
    public void ManifestMustMatchAcceptedContractIdentityAndRoute()
    {
        JobContract contract =
            AcceptedContract();

        AcceptedCargoManifest manifest =
            AcceptedCargoManifest.Create(
                contract,
                [DefaultLot()]);

        JobContract other =
            contract with
            {
                ContractId =
                    Guid.Parse(
                        "94000000-0000-0000-0000-000000000099")
            };

        Assert.Throws<InvalidOperationException>(
            () => manifest.ValidateForContract(
                other));

        JobContract rerouted =
            contract with
            {
                DestinationIcao = "KBUF"
            };

        Assert.Throws<InvalidOperationException>(
            () => manifest.ValidateForContract(
                rerouted));
    }

    [Fact]
    public void OfferedOrNonCargoContractCannotAcceptCargoManifest()
    {
        JobContract offered =
            BaseContract();

        Assert.Throws<InvalidOperationException>(
            () => AcceptedCargoManifest.Create(
                offered,
                [DefaultLot()]));

        JobContract passenger =
            AcceptedContract() with
            {
                Kind = ContractKind.Passenger
            };

        Assert.Throws<InvalidOperationException>(
            () => AcceptedCargoManifest.Create(
                passenger,
                [DefaultLot()]));
    }

    [Fact]
    public void ManifestRejectsDuplicateLotIdentities()
    {
        JobContract contract =
            AcceptedContract();

        AcceptedCargoLot lot =
            DefaultLot();

        Assert.Throws<ArgumentException>(
            () => AcceptedCargoManifest.Create(
                contract,
                [lot, lot]));
    }

    [Fact]
    public void LotRouteMustMatchManifestRoute()
    {
        JobContract contract =
            AcceptedContract();

        AcceptedCargoLot wrongRoute =
            DefaultLot() with
            {
                DestinationIcao = "KBUF"
            };

        Assert.Throws<ArgumentException>(
            () => AcceptedCargoManifest.Create(
                contract,
                [wrongRoute]));
    }

    [Fact]
    public void LotValidationPreservesMoneyAndHandlingInvariants()
    {
        AcceptedCargoLot lot =
            DefaultLot();

        AcceptedCargoLot invalidMoney =
            lot with
            {
                DeclaredValue = 100.001m
            };

        Assert.Throws<ArgumentException>(
            invalidMoney.Validate);

        AcceptedCargoLot invalidHandling =
            lot with
            {
                HandlingRequirements =
                    (CargoHandlingRequirement)(1 << 20)
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            invalidHandling.Validate);
    }

    [Fact]
    public void DefaultConstructedManifestCannotBypassLotInitialization()
    {
        var manifest =
            new AcceptedCargoManifest(
                Guid.Parse(
                    "94000000-0000-0000-0000-000000000010"),
                "KRME",
                "KSYR",
                OfferedAt.AddMinutes(10),
                ImmutableArray<AcceptedCargoLot>.Empty);

        Assert.Throws<ArgumentException>(
            manifest.Validate);
    }

    private static JobContract AcceptedContract() =>
        BaseContract() with
        {
            Status = ContractStatus.Accepted,
            AcceptedAt =
                OfferedAt.AddMinutes(10)
        };

    private static JobContract BaseContract() =>
        new(
            ContractId:
                Guid.Parse(
                    "94000000-0000-0000-0000-000000000010"),
            EmployerId: null,
            Kind: ContractKind.Cargo,
            ServiceTrack:
                ServiceTrack.CivilianEmployment,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation:
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 1_000m,
                    PilotCompensation: 500m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
            OfferedAt: OfferedAt,
            MustStartBy:
                OfferedAt.AddHours(2),
            MustCompleteBy:
                OfferedAt.AddHours(4),
            AircraftRequirements:
                new AircraftMissionRequirements(
                    RequiredCapabilities:
                        AircraftCapability.Cargo,
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumPayloadPounds: 500,
                    MinimumSeats: 0));

    private static AcceptedCargoLot DefaultLot() =>
        Lot(
            Guid.Parse(
                "94000000-0000-0000-0000-000000000001"),
            "coffee.roasted",
            "Roasted coffee",
            CargoCommodityCategory.GeneralFreight,
            quantity: 600m,
            massPounds: 600,
            declaredValue: 4_440m,
            originUnitValue: 7.40m,
            destinationUnitValue: 8.05m);

    private static AcceptedCargoLot Lot(
        Guid lotId,
        string commodityId,
        string commodityName,
        CargoCommodityCategory category,
        decimal quantity,
        double massPounds,
        decimal declaredValue,
        decimal? originUnitValue,
        decimal? destinationUnitValue,
        CargoHandlingRequirement handling =
            CargoHandlingRequirement.None) =>
        new(
            lotId,
            commodityId,
            commodityName,
            category,
            quantity,
            CargoUnitOfTrade.Pound,
            massPounds,
            VolumeCubicFeet: null,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Shipper: "OpenCareer Test Shipper",
            Consignee: "OpenCareer Test Consignee",
            declaredValue,
            originUnitValue,
            destinationUnitValue,
            handling);
}
