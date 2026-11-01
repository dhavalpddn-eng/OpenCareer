using Microsoft.Data.Sqlite;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Planning;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqlitePreparedDispatchPlanStoreTests : IAsyncLifetime
{
    private static readonly Guid ContractId =
        Guid.Parse("a2000000-0000-0000-0000-000000000001");

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenCareer.Tests",
        Guid.NewGuid().ToString("N"));

    private string DatabasePath =>
        Path.Combine(_directory, "prepared-dispatch.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PlanRoundTripsReplacesAndClearsAcrossStoreInstances()
    {
        PreparedDispatchPlan first = Plan();
        var store = CreateStore();

        Assert.Equal(
            PreparedDispatchPlanSaveResult.Created,
            await store.SaveAsync(first));
        Assert.Equal(
            PreparedDispatchPlanSaveResult.AlreadySaved,
            await store.SaveAsync(first));

        PreparedDispatchPlan restarted = Assert.IsType<PreparedDispatchPlan>(
            await CreateStore().ReadAsync(ContractId));
        Assert.Equal(first, restarted);

        PreparedDispatchPlan replacement = first with
        {
            PreparedAt = first.PreparedAt.AddMinutes(1),
            OriginRunwayIdentifier = "13R"
        };
        replacement.Validate();

        Assert.Equal(
            PreparedDispatchPlanSaveResult.Replaced,
            await store.SaveAsync(replacement));
        Assert.Equal(
            replacement,
            await CreateStore().ReadAsync(ContractId));

        await CreateStore().ClearAsync(ContractId);
        Assert.Null(await store.ReadAsync(ContractId));
    }

    [Fact]
    public async Task CorruptIndexedIdentityFailsClosed()
    {
        PreparedDispatchPlan plan = Plan();
        var store = CreateStore();
        await store.SaveAsync(plan);

        await using var connection = new SqliteConnection(
            $"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE prepared_dispatch_plans SET prepared_at_utc_ticks = prepared_at_utc_ticks + 1 WHERE contract_id = $contract_id;";
        command.Parameters.AddWithValue(
            "$contract_id",
            ContractId.ToString("D"));
        await command.ExecuteNonQueryAsync();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.ReadAsync(ContractId));
    }

    private SqlitePreparedDispatchPlanStore CreateStore() =>
        new(new OpenCareerDatabaseOptions(DatabasePath));

    private static PreparedDispatchPlan Plan()
    {
        var fuel = new RouteFuelWeightPlan(
            FuelTypeIndex: 0,
            FuelDensityPoundsPerGallon: 6,
            RequiredFuelGallons: 30,
            RequiredFuelPounds: 180,
            MaximumFuelGallons: 50,
            MaximumFuelPounds: 300);
        var requirements = fuel.ApplyTo(new(
            PayloadPounds: 250,
            RequiredRangeNauticalMiles: 300,
            RangeSafetyMarginPercent: 10,
            RunwayLengthSafetyMarginPercent: 15));

        var plan = new PreparedDispatchPlan(
            ContractId,
            ContractVersion: 1,
            CanonicalAircraftId: "aircraft-a",
            ReservationId: "contract:a2000000000000000000000000000001:aircraft-v1",
            OriginIcao: "KJFK",
            DestinationIcao: "KBOS",
            Cruise: new(0, 0, 6000, 0, 12000),
            RouteFuel: new(300, 260, 10, 8, 3, 2, new(5, 30, 1)),
            Requirements: requirements,
            FuelWeightPlan: fuel,
            OriginRunwayIdentifier: "04L",
            DestinationRunwayIdentifier: "22R",
            PreparedAt: new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        plan.Validate();
        return plan;
    }
}
