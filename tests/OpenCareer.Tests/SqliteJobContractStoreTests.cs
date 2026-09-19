using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class SqliteJobContractStoreTests : IDisposable
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.SqliteJobContractStoreTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ContractEconomicSnapshotSurvivesRestartExactly()
    {
        string path =
            Path.Combine(_directory, "career.db");
        JobContract contract =
            CreateContract();

        var store =
            new SqliteEconomyLedgerStore(path);

        Assert.Equal(
            JobContractSaveResult.Created,
            await store.CreateJobContractAsync(contract));
        Assert.Equal(
            JobContractSaveResult.AlreadySaved,
            await store.CreateJobContractAsync(contract));

        var reopened =
            new SqliteEconomyLedgerStore(path);

        PersistedJobContract persisted =
            (await reopened.ReadJobContractAsync(
                contract.ContractId))!;

        Assert.Equal(0, persisted.Version);
        Assert.Equal(contract, persisted.Contract);
        Assert.Equal(
            contract.EconomicSnapshot,
            persisted.Contract.EconomicSnapshot);
    }

    [Fact]
    public async Task LifecycleUpdateUsesOptimisticVersionAndPreservesTerms()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        JobContract offered =
            CreateContract();

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
        Assert.Equal(
            offered.EconomicSnapshot,
            persisted.Contract.EconomicSnapshot);

        JobContract inProgress =
            accepted with
            {
                Status = ContractStatus.InProgress,
                StartedAt = OfferedAt.AddMinutes(20)
            };
        inProgress.Validate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await store.UpdateJobContractAsync(
                    inProgress,
                    expectedVersion: 0));

        Assert.Equal(
            ContractStatus.Accepted,
            (await store.ReadJobContractAsync(
                offered.ContractId))!.Contract.Status);
    }

    [Fact]
    public async Task PersistedEconomicTermsCannotBeRewritten()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        JobContract offered =
            CreateContract();
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

        // The domain catches compensation/snapshot drift before persistence.
        Assert.Throws<ArgumentException>(
            tampered.Validate);
    }

    [Fact]
    public async Task StoreRejectsIllegalLifecycleJump()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        JobContract offered =
            CreateContract();
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
            async () =>
                await store.UpdateJobContractAsync(
                    fabricatedComplete,
                    expectedVersion: 0));
    }

    [Fact]
    public async Task DifferentContractCannotReuseContractIdentity()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        JobContract first =
            CreateContract();

        await store.CreateJobContractAsync(first);

        JobContract different =
            first with
            {
                DestinationIcao = "KBUF"
            };
        different.Validate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await store.CreateJobContractAsync(
                    different));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static JobContract CreateContract()
    {
        var offer =
            new JobMarketOfferDraft(
                Guid.NewGuid(),
                ServiceTrack.CivilianEmployment,
                ContractKind.Cargo,
                JobScenarioKind.Standard,
                "KRME",
                "KSYR",
                DistanceNm: 100,
                EstimatedFlightHours: 1,
                OfferedAt,
                ExpiresAt: OfferedAt.AddHours(2),
                IsLockedPreview: false,
                RouteStrength: 0.20,
                RelationshipStrength: 0.30,
                MarketSelectionWeight: 1);

        return JobContractFactory.Create(
            new JobContractCreationRequest(
                offer,
                QuoteTime: OfferedAt.AddMinutes(5),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumPayloadPounds: 100,
                        MinimumRangeNauticalMiles: 100,
                        MinimumSeats: 0),
                EstimatedFlightHours: 1,
                PayloadPounds: 100,
                DemandAttractiveness: 1,
                Urgency: 0.10,
                Difficulty: 0.10,
                EstimatedPlayerOperatingCosts: 0m,
                MustStartBy: OfferedAt.AddHours(3),
                MustCompleteBy: OfferedAt.AddHours(5)));
    }
}
