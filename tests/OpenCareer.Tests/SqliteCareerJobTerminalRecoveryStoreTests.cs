using Microsoft.Data.Sqlite;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Logbook;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteCareerJobTerminalRecoveryStoreTests : IAsyncLifetime
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    private string DatabasePath =>
        Path.Combine(_directory, "terminal-recovery.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task RequestSurvivesStoreRecreationAndClearsByIdentity()
    {
        CareerJobPlayableCompletionRequest request = Request();
        var first =
            new SqliteCareerJobTerminalRecoveryStore(
                new OpenCareerDatabaseOptions(DatabasePath));

        await first.SaveAsync(request);

        var restarted =
            new SqliteCareerJobTerminalRecoveryStore(
                new OpenCareerDatabaseOptions(DatabasePath));
        CareerJobPlayableCompletionRequest recovered =
            Assert.IsType<CareerJobPlayableCompletionRequest>(
                await restarted.ReadAsync());

        Assert.Equal(request.ContractId, recovered.ContractId);
        Assert.Equal(request.ActualCosts, recovered.ActualCosts);
        Assert.Equal(request.LogbookCommittedAt, recovered.LogbookCommittedAt);
        Assert.Equal(
            request.LogbookContext.Aircraft,
            recovered.LogbookContext.Aircraft);
        Assert.Equal(
            Assert.Single(request.LogbookContext.Events!),
            Assert.Single(recovered.LogbookContext.Events!));

        await restarted.SaveAsync(recovered);
        await restarted.ClearAsync(request.ContractId);

        Assert.Null(await restarted.ReadAsync());
    }

    [Fact]
    public async Task CorruptContractIdentityFailsClosed()
    {
        CareerJobPlayableCompletionRequest request = Request();
        var store =
            new SqliteCareerJobTerminalRecoveryStore(
                new OpenCareerDatabaseOptions(DatabasePath));
        await store.SaveAsync(request);

        await using var connection =
            new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE career_terminal_recovery SET contract_id = $contract_id WHERE slot_id = 1;";
        command.Parameters.AddWithValue(
            "$contract_id",
            Guid.NewGuid().ToString("D"));
        await command.ExecuteNonQueryAsync();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.ReadAsync());
    }

    private static CareerJobPlayableCompletionRequest Request()
    {
        DateTimeOffset completedAt =
            new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        return new(
            Guid.Parse("ac000000-0000-0000-0000-000000000001"),
            completedAt,
            MissionConditionsVerified: true,
            PostFlightTasksVerified: true,
            new ContractSettlementCosts(10m, 5m, 2m),
            completedAt.AddMinutes(1),
            new SettledJobLogbookContext(
                new AircraftDebrief("Cessna 172 Skyhawk"),
                ActualDeparture: "KJFK",
                ActualArrival: "KJFK",
                DiversionLocation: null,
                new PayloadDebrief(
                    PassengerCount: null,
                    CargoMassPounds: 0,
                    CargoDescription: null,
                    Outcome: "Completed",
                    EvidenceQuality.DerivedHighConfidence),
                FlightSafetyOutcome.CompletedNormally,
                MissionOutcome.Succeeded,
                ReputationDelta: 0,
                Events:
                [
                    new FlightDebriefEvent(
                        Guid.Parse("ac000000-0000-0000-0000-000000000002"),
                        completedAt,
                        "terminal",
                        DebriefEventSeverity.Information,
                        "Verified completion",
                        EvidenceQuality.Observed)
                ]),
            completedAt.AddMinutes(2),
            completedAt.AddMinutes(3));
    }
}
