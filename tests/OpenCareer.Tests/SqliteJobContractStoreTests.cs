using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteJobContractStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private string _root = string.Empty;
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

        _databasePath =
            Path.Combine(_root, "opencareer.db");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Test cleanup should not mask the actual assertion result.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task OfferedContractRoundTripsAcrossStoreInstances()
    {
        JobContract expected =
            CreateContract();

        SqliteJobContractStore first =
            CreateStore();

        Assert.Equal(
            JobContractSaveResult.Created,
            await first.CreateJobContractAsync(expected));

        Assert.Equal(
            JobContractSaveResult.AlreadySaved,
            await first.CreateJobContractAsync(expected));

        SqliteJobContractStore second =
            CreateStore();

        PersistedJobContract? loaded =
            await second.ReadJobContractAsync(
                expected.ContractId);

        Assert.NotNull(loaded);
        Assert.Equal(0, loaded.Version);
        Assert.Equal(expected, loaded.Contract);
    }

    [Fact]
    public async Task LifecycleUpdateUsesOptimisticVersion()
    {
        JobContract offered =
            CreateContract();

        SqliteJobContractStore store =
            CreateStore();

        await store.CreateJobContractAsync(offered);

        JobContract accepted =
            offered with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = OfferedAt.AddMinutes(10)
            };
        accepted.Validate();

        Assert.Equal(
            JobContractSaveResult.Updated,
            await store.UpdateJobContractAsync(
                accepted,
                expectedVersion: 0));

        PersistedJobContract persisted =
            (await store.ReadJobContractAsync(
                offered.ContractId))!;

        Assert.Equal(1, persisted.Version);
        Assert.Equal(
            ContractStatus.Accepted,
            persisted.Contract.Status);

        JobContract inProgress =
            accepted with
            {
                Status = ContractStatus.InProgress,
                StartedAt = OfferedAt.AddMinutes(20)
            };
        inProgress.Validate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateJobContractAsync(
                inProgress,
                expectedVersion: 0));

        Assert.Equal(
            ContractStatus.Accepted,
            (await store.ReadJobContractAsync(
                offered.ContractId))!.Contract.Status);
    }

    [Fact]
    public async Task ImmutableTermsCannotBeRewritten()
    {
        JobContract offered =
            CreateContract();

        SqliteJobContractStore store =
            CreateStore();

        await store.CreateJobContractAsync(offered);

        JobContract tampered =
            offered with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = OfferedAt.AddMinutes(10),
                Compensation =
                    offered.Compensation with
                    {
                        PilotCompensation =
                            offered.Compensation.PilotCompensation
                            + 1m
                    }
            };
        tampered.Validate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateJobContractAsync(
                tampered,
                expectedVersion: 0));
    }

    [Fact]
    public async Task IllegalLifecycleJumpIsRejected()
    {
        JobContract offered =
            CreateContract();

        SqliteJobContractStore store =
            CreateStore();

        await store.CreateJobContractAsync(offered);

        JobContract fabricatedComplete =
            offered with
            {
                Status = ContractStatus.Completed,
                AcceptedAt = OfferedAt.AddMinutes(5),
                StartedAt = OfferedAt.AddMinutes(10),
                CompletedAt = OfferedAt.AddHours(1)
            };
        fabricatedComplete.Validate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateJobContractAsync(
                fabricatedComplete,
                expectedVersion: 0));
    }

    [Fact]
    public async Task DifferentContractCannotReuseIdentity()
    {
        JobContract first =
            CreateContract();

        SqliteJobContractStore store =
            CreateStore();

        await store.CreateJobContractAsync(first);

        JobContract different =
            first with
            {
                DestinationIcao = "KBUF"
            };
        different.Validate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateJobContractAsync(
                different));
    }

    private SqliteJobContractStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteJobContractStore>.Instance);

    private static JobContract CreateContract() =>
        new(
            ContractId:
                Guid.Parse(
                    "70000000-0000-0000-0000-000000000001"),
            EmployerId:
                Guid.Parse(
                    "70000000-0000-0000-0000-000000000002"),
            Kind: ContractKind.Cargo,
            ServiceTrack:
                ServiceTrack.CivilianEmployment,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation:
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 1_250m,
                    PilotCompensation: 450m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
            OfferedAt: OfferedAt,
            MustStartBy: OfferedAt.AddHours(2),
            MustCompleteBy: OfferedAt.AddHours(5),
            AircraftRequirements:
                new AircraftMissionRequirements(
                    RequiredCapabilities:
                        AircraftCapability.Cargo,
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumPayloadPounds: 500,
                    MinimumRangeNauticalMiles: 400,
                    MinimumSeats: 0),
            ReputationReward: 1.25,
            ReputationPenalty: 2.5,
            MarketId: "KRME:general-cargo",
            ProviderAircraft:
                new ProviderAircraftAssignment(
                    Guid.Parse(
                        "70000000-0000-0000-0000-000000000003"),
                    "provider-canonical-aircraft",
                    "Provider Aircraft",
                    "KRME"));
}
