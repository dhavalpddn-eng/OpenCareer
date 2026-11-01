using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class AcceptedPassengerManifestTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptedPassengerContractProducesImmutableManifest()
    {
        JobContract contract = AcceptedContract();

        AcceptedPassengerManifest manifest =
            AcceptedPassengerManifest.Create(contract);

        Assert.Equal(contract.ContractId, manifest.ContractId);
        Assert.Equal(contract.AcceptedAt, manifest.AcceptedAt);
        Assert.Equal(4, manifest.PassengerCount);
        Assert.Equal(720, manifest.PassengerAndBaggageWeightPounds);
    }

    [Fact]
    public void InProgressContractRetainsAcceptedManifestAuthority()
    {
        JobContract contract = AcceptedContract() with
        {
            Status = ContractStatus.InProgress,
            StartedAt = OfferedAt.AddMinutes(20)
        };

        AcceptedPassengerManifest manifest =
            AcceptedPassengerManifest.Create(contract);

        Assert.Equal(contract.ContractId, manifest.ContractId);
        Assert.Equal(contract.AcceptedAt, manifest.AcceptedAt);
        manifest.ValidateForContract(contract);
    }

    [Fact]
    public void OfferedOrNonPassengerContractCannotCreateManifest()
    {
        Assert.Throws<InvalidOperationException>(
            () => AcceptedPassengerManifest.Create(BaseContract()));

        Assert.Throws<InvalidOperationException>(
            () => AcceptedPassengerManifest.Create(
                AcceptedContract() with
                {
                    Kind = ContractKind.Ferry
                }));
    }

    [Fact]
    public void MissingPassengerCountOrPayloadCannotCreateManifest()
    {
        JobContract contract = AcceptedContract();

        Assert.Throws<InvalidOperationException>(
            () => AcceptedPassengerManifest.Create(
                contract with
                {
                    AircraftRequirements =
                        contract.AircraftRequirements with
                        {
                            MinimumPassengerCapacity = 0
                        }
                }));

        Assert.Throws<InvalidOperationException>(
            () => AcceptedPassengerManifest.Create(
                contract with
                {
                    PlanningTerms =
                        contract.PlanningTerms! with
                        {
                            PayloadPounds = 0
                        }
                }));
    }

    [Fact]
    public void ManifestRejectsContractIdentityRouteCountOrPayloadDrift()
    {
        JobContract contract = AcceptedContract();
        AcceptedPassengerManifest manifest =
            AcceptedPassengerManifest.Create(contract);

        Assert.Throws<InvalidOperationException>(
            () => manifest.ValidateForContract(
                contract with { DestinationIcao = "KBUF" }));

        Assert.Throws<InvalidOperationException>(
            () => manifest.ValidateForContract(
                contract with
                {
                    AircraftRequirements =
                        contract.AircraftRequirements with
                        {
                            MinimumPassengerCapacity = 5
                        }
                }));

        Assert.Throws<InvalidOperationException>(
            () => manifest.ValidateForContract(
                contract with
                {
                    PlanningTerms =
                        contract.PlanningTerms! with
                        {
                            PayloadPounds = 900
                        }
                }));
    }

    [Fact]
    public void PassengerCapacityRequirementRequiresPassengerCapability()
    {
        AircraftMissionRequirements invalid =
            BaseContract().AircraftRequirements with
            {
                RequiredCapabilities = AircraftCapability.None
            };

        Assert.Throws<ArgumentException>(invalid.Validate);
    }

    private static JobContract AcceptedContract() =>
        BaseContract() with
        {
            Status = ContractStatus.Accepted,
            AcceptedAt = OfferedAt.AddMinutes(10)
        };

    private static JobContract BaseContract() =>
        new(
            ContractId: Guid.Parse("95000000-0000-0000-0000-000000000010"),
            EmployerId: null,
            Kind: ContractKind.Passenger,
            ServiceTrack: ServiceTrack.CivilianEmployment,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation: new(
                CompensationModel.PilotWage,
                GrossCustomerRevenue: 1_000m,
                PilotCompensation: 500m,
                EmployerCoversFuel: true,
                EmployerCoversMaintenance: true,
                EmployerCoversAirportFees: true),
            OfferedAt: OfferedAt,
            MustStartBy: OfferedAt.AddHours(2),
            MustCompleteBy: OfferedAt.AddHours(4),
            AircraftRequirements: new(
                RequiredCapabilities: AircraftCapability.Passenger,
                AllowedAccess: AircraftAccess.Civilian,
                MinimumPayloadPounds: 720,
                MinimumRangeNauticalMiles: 150,
                MinimumSeats: 1,
                MinimumPassengerCapacity: 4),
            PlanningTerms: new(
                RouteDistanceNauticalMiles: 150,
                PayloadPounds: 720));
}
