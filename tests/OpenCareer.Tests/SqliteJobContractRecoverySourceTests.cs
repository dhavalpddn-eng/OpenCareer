using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteJobContractRecoverySourceTests
    : IAsyncLifetime
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 15, 0, 0, TimeSpan.Zero);

    private string _root = string.Empty;
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        _databasePath =
            Path.Combine(
                _root,
                "opencareer.db");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(
                    _root,
                    recursive: true);
            }
        }
        catch
        {
            // Test cleanup must not hide the assertion result.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task RecoversAcceptedInProgressAndCompletedContractsAcrossRestart()
    {
        SqliteJobContractStore store =
            CreateContractStore();

        JobContract offered =
            CreateContract(
                Guid.Parse(
                    "a1000000-0000-0000-0000-000000000001"));
        await store.CreateJobContractAsync(offered);

        JobContract accepted =
            CreateContract(
                Guid.Parse(
                    "a1000000-0000-0000-0000-000000000002"))
            with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = OfferedAt.AddMinutes(10)
            };
        await PersistLifecycleAsync(
            store,
            accepted);

        JobContract inProgress =
            CreateContract(
                Guid.Parse(
                    "a1000000-0000-0000-0000-000000000003"))
            with
            {
                Status = ContractStatus.InProgress,
                AcceptedAt = OfferedAt.AddMinutes(11),
                StartedAt = OfferedAt.AddMinutes(20)
            };
        await PersistLifecycleAsync(
            store,
            inProgress);

        JobContract completed =
            CreateContract(
                Guid.Parse(
                    "a1000000-0000-0000-0000-000000000004"))
            with
            {
                Status = ContractStatus.Completed,
                AcceptedAt = OfferedAt.AddMinutes(12),
                StartedAt = OfferedAt.AddMinutes(22),
                CompletedAt = OfferedAt.AddMinutes(50)
            };
        await PersistLifecycleAsync(
            store,
            completed);

        JobContract failed =
            CreateContract(
                Guid.Parse(
                    "a1000000-0000-0000-0000-000000000005"))
            with
            {
                Status = ContractStatus.Failed,
                AcceptedAt = OfferedAt.AddMinutes(13)
            };
        await PersistLifecycleAsync(
            store,
            failed);

        var restarted =
            new SqliteJobContractRecoverySource(
                new OpenCareerDatabaseOptions(
                    _databasePath),
                NullLogger<SqliteJobContractRecoverySource>.Instance);

        IReadOnlyList<JobContractRecoveryCandidate> candidates =
            await restarted.ReadRecoveryCandidatesAsync();

        Assert.Equal(
            3,
            candidates.Count);

        Assert.Collection(
            candidates,
            candidate =>
            {
                Assert.Equal(
                    completed.ContractId,
                    candidate.ContractId);
                Assert.Equal(
                    ContractStatus.Completed,
                    candidate.Status);
                Assert.Equal(
                    3,
                    candidate.Version);
                Assert.Equal(
                    completed.CompletedAt!.Value,
                    candidate.UpdatedAt);
            },
            candidate =>
            {
                Assert.Equal(
                    inProgress.ContractId,
                    candidate.ContractId);
                Assert.Equal(
                    ContractStatus.InProgress,
                    candidate.Status);
                Assert.Equal(
                    2,
                    candidate.Version);
                Assert.Equal(
                    inProgress.StartedAt!.Value,
                    candidate.UpdatedAt);
            },
            candidate =>
            {
                Assert.Equal(
                    accepted.ContractId,
                    candidate.ContractId);
                Assert.Equal(
                    ContractStatus.Accepted,
                    candidate.Status);
                Assert.Equal(
                    1,
                    candidate.Version);
                Assert.Equal(
                    accepted.AcceptedAt!.Value,
                    candidate.UpdatedAt);
            });
    }

    [Fact]
    public async Task EmptyDatabaseReturnsNoRecoveryCandidates()
    {
        var source =
            new SqliteJobContractRecoverySource(
                new OpenCareerDatabaseOptions(
                    _databasePath),
                NullLogger<SqliteJobContractRecoverySource>.Instance);

        IReadOnlyList<JobContractRecoveryCandidate> candidates =
            await source.ReadRecoveryCandidatesAsync();

        Assert.Empty(candidates);
    }

    private SqliteJobContractStore CreateContractStore() =>
        new(
            new OpenCareerDatabaseOptions(
                _databasePath),
            NullLogger<SqliteJobContractStore>.Instance);

    private static async Task PersistLifecycleAsync(
        SqliteJobContractStore store,
        JobContract target)
    {
        JobContract offered =
            target with
            {
                Status = ContractStatus.Offered,
                AcceptedAt = null,
                StartedAt = null,
                CompletedAt = null
            };
        offered.Validate();

        await store.CreateJobContractAsync(
            offered);

        if (target.Status == ContractStatus.Offered)
            return;

        JobContract accepted =
            offered with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = target.AcceptedAt
            };
        accepted.Validate();

        await store.UpdateJobContractAsync(
            accepted,
            expectedVersion: 0);

        if (target.Status == ContractStatus.Accepted)
            return;

        if (target.Status == ContractStatus.Failed)
        {
            JobContract failed =
                accepted with
                {
                    Status = ContractStatus.Failed
                };
            failed.Validate();

            await store.UpdateJobContractAsync(
                failed,
                expectedVersion: 1);
            return;
        }

        JobContract inProgress =
            accepted with
            {
                Status = ContractStatus.InProgress,
                StartedAt = target.StartedAt
            };
        inProgress.Validate();

        await store.UpdateJobContractAsync(
            inProgress,
            expectedVersion: 1);

        if (target.Status == ContractStatus.InProgress)
            return;

        if (target.Status == ContractStatus.Completed)
        {
            JobContract completed =
                inProgress with
                {
                    Status = ContractStatus.Completed,
                    CompletedAt = target.CompletedAt
                };
            completed.Validate();

            await store.UpdateJobContractAsync(
                completed,
                expectedVersion: 2);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported test lifecycle target {target.Status}.");
    }

    private static JobContract CreateContract(
        Guid contractId) =>
        new(
            ContractId: contractId,
            EmployerId:
                Guid.Parse(
                    "a1000000-0000-0000-0000-000000000010"),
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
            MustStartBy:
                OfferedAt.AddHours(2),
            MustCompleteBy:
                OfferedAt.AddHours(5),
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
            MarketId: "KRME:general-cargo");
}
