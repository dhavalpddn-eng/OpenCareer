using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteAcceptedJobCancellationRecoverySourceTests
    : IAsyncLifetime
{
    private static readonly Guid ContractId =
        Guid.Parse("c5000000-0000-0000-0000-000000000001");
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenCareer.Tests",
        Guid.NewGuid().ToString("N"));

    private string DatabasePath =>
        Path.Combine(_directory, "cancellation-recovery.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the assertion result.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task FindsOnlyCancelledContractsWithPersistedPreflightArtifacts()
    {
        var options = new OpenCareerDatabaseOptions(DatabasePath);
        var contracts = new SqliteJobContractStore(
            options,
            NullLogger<SqliteJobContractStore>.Instance);
        JobContract offered = OfferedContract();
        await contracts.CreateJobContractAsync(offered);
        JobContract accepted = offered with
        {
            Status = ContractStatus.Accepted,
            AcceptedAt = offered.OfferedAt.AddMinutes(5)
        };
        accepted.Validate();
        await contracts.UpdateJobContractAsync(accepted, expectedVersion: 0);
        JobContract cancelled = accepted.Cancel();
        await contracts.UpdateJobContractAsync(cancelled, expectedVersion: 1);

        var source =
            new SqliteAcceptedJobCancellationRecoverySource(options);
        Assert.Empty(await source.ReadPendingContractIdsAsync());

        await ExecuteAsync(
            """
            INSERT INTO prepared_dispatch_plans (
                contract_id,
                payload_schema_version,
                prepared_at_utc_ticks,
                payload_json)
            VALUES ($contract_id, 1, 1, '{}');
            """);
        Assert.Equal(
            new[] { ContractId },
            await source.ReadPendingContractIdsAsync());

        await ExecuteAsync(
            "DELETE FROM prepared_dispatch_plans WHERE contract_id = $contract_id;");
        await ExecuteAsync(
            """
            INSERT INTO aircraft_availability (
                canonical_aircraft_id,
                status,
                reservation_id)
            VALUES ('aircraft-a', 1, $reservation_id);
            """,
            includeReservation: true);
        Assert.Equal(
            new[] { ContractId },
            await source.ReadPendingContractIdsAsync());

        await ExecuteAsync(
            "UPDATE aircraft_availability SET reservation_id = NULL, status = 0;");
        Assert.Empty(await source.ReadPendingContractIdsAsync());
    }

    private async Task ExecuteAsync(
        string sql,
        bool includeReservation = false)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (sql.Contains("$contract_id", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue(
                "$contract_id",
                ContractId.ToString("D"));
        }
        if (includeReservation)
        {
            command.Parameters.AddWithValue(
                "$reservation_id",
                JobAcceptanceFleetBridge.GetReservationId(ContractId));
        }

        await command.ExecuteNonQueryAsync();
    }

    private static JobContract OfferedContract()
    {
        DateTimeOffset offeredAt =
            new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var contract = new JobContract(
            ContractId,
            EmployerId: null,
            ContractKind.Ferry,
            ServiceTrack.CivilianEmployment,
            "KJFK",
            "KBOS",
            new ContractCompensation(
                CompensationModel.PilotWage,
                GrossCustomerRevenue: 1_000m,
                PilotCompensation: 350m,
                EmployerCoversFuel: true,
                EmployerCoversMaintenance: true,
                EmployerCoversAirportFees: true),
            offeredAt,
            offeredAt.AddHours(2),
            offeredAt.AddHours(5),
            new AircraftMissionRequirements());
        contract.Validate();
        return contract;
    }
}
