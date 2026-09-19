using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class PersistedContractSettlementTests : IDisposable
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.PersistedContractSettlementTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompletionAndMoneyPostAtomicallyExactlyOnce()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await new CareerEconomyBootstrapService(store)
            .InitializeAsync(
                Guid.NewGuid(),
                1_000m,
                OfferedAt.AddHours(-1));

        JobContract inProgress =
            await PersistInProgressAsync(store);

        PersistedJobContract before =
            (await store.ReadJobContractAsync(
                inProgress.ContractId))!;

        Assert.Equal(2, before.Version);
        Assert.Equal(
            ContractStatus.InProgress,
            before.Contract.Status);

        DateTimeOffset completedAt =
            OfferedAt.AddHours(2);

        var service =
            new PersistedEconomySettlementService(store);

        PersistedEconomySettlementResult first =
            await service.CompleteAndSettleAsync(
                inProgress.ContractId,
                expectedContractVersion: 2,
                completedAt,
                flightCompletionVerified: true,
                new ContractSettlementCosts(
                    FuelCost: 0m,
                    MaintenanceReserveCost: 0m,
                    AirportFees: 0m,
                    OtherOperatingCosts: 10m),
                settledAt:
                    completedAt.AddMinutes(1));

        Assert.True(first.WasNewlyPosted);
        Assert.Equal(
            ContractStatus.Completed,
            first.Contract.Contract.Status);
        Assert.Equal(3, first.Contract.Version);
        Assert.Equal(
            completedAt,
            first.Contract.Contract.CompletedAt);

        decimal expectedCash =
            1_000m
            + first.Settlement.NetCashChange;
        Assert.Equal(
            expectedCash,
            first.CashBalanceAfter);

        PersistedEconomySettlementResult retry =
            await service.CompleteAndSettleAsync(
                inProgress.ContractId,
                expectedContractVersion: 2,
                completedAt,
                flightCompletionVerified: true,
                new ContractSettlementCosts(
                    FuelCost: 0m,
                    MaintenanceReserveCost: 0m,
                    AirportFees: 0m,
                    OtherOperatingCosts: 10m),
                settledAt:
                    completedAt.AddMinutes(1));

        Assert.False(retry.WasNewlyPosted);
        Assert.Equal(
            first.CashBalanceAfter,
            retry.CashBalanceAfter);
        Assert.Equal(3, retry.Contract.Version);

        IReadOnlyList<EconomyLedgerTransaction> recent =
            await store.ReadRecentAsync(10);

        Assert.Equal(2, recent.Count);
        Assert.Single(
            recent,
            transaction =>
                transaction.ReferenceType
                    == nameof(JobContract));
    }

    [Fact]
    public async Task RetryWithDifferentFinancialActualsIsRejected()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        JobContract inProgress =
            await PersistInProgressAsync(store);

        DateTimeOffset completedAt =
            OfferedAt.AddHours(2);
        var service =
            new PersistedEconomySettlementService(store);

        await service.CompleteAndSettleAsync(
            inProgress.ContractId,
            2,
            completedAt,
            true,
            new ContractSettlementCosts(
                0m,
                0m,
                0m,
                OtherOperatingCosts: 10m),
            completedAt.AddMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.CompleteAndSettleAsync(
                    inProgress.ContractId,
                    2,
                    completedAt,
                    true,
                    new ContractSettlementCosts(
                        0m,
                        0m,
                        0m,
                        OtherOperatingCosts: 20m),
                    completedAt.AddMinutes(1)));
    }

    [Fact]
    public async Task StaleContractVersionCannotCreateSettlement()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        JobContract inProgress =
            await PersistInProgressAsync(store);

        var service =
            new PersistedEconomySettlementService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.CompleteAndSettleAsync(
                    inProgress.ContractId,
                    expectedContractVersion: 1,
                    completedAt:
                        OfferedAt.AddHours(2),
                    flightCompletionVerified: true,
                    new ContractSettlementCosts(
                        0m,
                        0m,
                        0m),
                    settledAt:
                        OfferedAt.AddHours(2)
                            .AddMinutes(1)));

        Assert.Null(
            await store.FindByIdempotencyKeyAsync(
                $"contract:{inProgress.ContractId:D}:settlement-v1"));

        Assert.Equal(
            ContractStatus.InProgress,
            (await store.ReadJobContractAsync(
                inProgress.ContractId))!.Contract.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static async Task<JobContract> PersistInProgressAsync(
        SqliteEconomyLedgerStore store)
    {
        JobContract offered =
            CreateContract();

        Assert.Equal(
            JobContractSaveResult.Created,
            await store.CreateJobContractAsync(
                offered));

        JobContract accepted =
            offered with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt =
                    OfferedAt.AddMinutes(5)
            };
        accepted.Validate();

        Assert.Equal(
            JobContractSaveResult.Updated,
            await store.UpdateJobContractAsync(
                accepted,
                expectedVersion: 0));

        JobContract inProgress =
            accepted with
            {
                Status = ContractStatus.InProgress,
                StartedAt =
                    OfferedAt.AddMinutes(15)
            };
        inProgress.Validate();

        Assert.Equal(
            JobContractSaveResult.Updated,
            await store.UpdateJobContractAsync(
                inProgress,
                expectedVersion: 1));

        return inProgress;
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
                ExpiresAt:
                    OfferedAt.AddHours(1),
                IsLockedPreview: false,
                RouteStrength: 0.20,
                RelationshipStrength: 0.30,
                MarketSelectionWeight: 1);

        return JobContractFactory.Create(
            new JobContractCreationRequest(
                offer,
                QuoteTime:
                    OfferedAt.AddMinutes(1),
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
                MustStartBy:
                    OfferedAt.AddHours(2),
                MustCompleteBy:
                    OfferedAt.AddHours(4)));
    }
}
