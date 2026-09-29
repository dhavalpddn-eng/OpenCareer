using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Flights;

namespace OpenCareer.Tests;

public sealed class SqliteFlightSessionCheckpointStoreTests :
    IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CheckpointSurvivesStoreReopen()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        FlightSession expected =
            CreateAirborneSession();

        var first =
            new SqliteFlightSessionCheckpointStore(
                databasePath);

        await first.SaveAsync(expected);

        var reopened =
            new SqliteFlightSessionCheckpointStore(
                databasePath);

        FlightSession? actual =
            await reopened.LoadAsync();

        Assert.NotNull(actual);
        AssertSessionEquivalent(expected, actual);
        Assert.Equal(
            "msfs-title:Cessna 172 Skyhawk",
            actual.Plan?.ExpectedCanonicalAircraftId);
    }

    [Fact]
    public async Task LegacyCheckpointWithoutExpectedAircraftIdentityStillLoadsWithoutFabrication()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteFlightSessionCheckpointStore(databasePath);
        FlightSession expected = CreateAirborneSession();
        await store.SaveAsync(expected);

        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var read = connection.CreateCommand();
            read.CommandText = "SELECT payload_json FROM flight_session_checkpoint WHERE slot_id = 1;";
            string payload = Assert.IsType<string>(await read.ExecuteScalarAsync());
            JsonObject root = Assert.IsType<JsonObject>(JsonNode.Parse(payload));
            JsonObject plan = Assert.IsType<JsonObject>(root["plan"]);
            Assert.True(plan.Remove("expectedCanonicalAircraftId"));

            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE flight_session_checkpoint SET payload_json = $payload WHERE slot_id = 1;";
            update.Parameters.AddWithValue("$payload", root.ToJsonString());
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        FlightSession? legacy = await new SqliteFlightSessionCheckpointStore(databasePath).LoadAsync();

        Assert.NotNull(legacy);
        Assert.Equal(expected.SessionId, legacy.SessionId);
        Assert.NotNull(legacy.Plan);
        Assert.Null(legacy.Plan.ExpectedCanonicalAircraftId);
    }

    [Fact]
    public async Task NewerCheckpointReplacesOlderCheckpoint()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteFlightSessionCheckpointStore(
                databasePath);

        FlightSession first =
            FlightSession.Start(
                new DateTimeOffset(
                    2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
                sessionId:
                    Guid.Parse(
                        "11111111-1111-1111-1111-111111111111"));

        await store.SaveAsync(first);

        FlightSession later =
            FlightSessionEngine.Advance(
                first,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        first.UpdatedAt.AddSeconds(1),
                        Connected: true,
                        StableTelemetry: true,
                        ValidLoadedAircraft: true,
                        ContinuityPlausible: true)));

        await store.SaveAsync(later);

        FlightSession? actual =
            await store.LoadAsync();

        Assert.Equal(
            FlightOperationState.ReadyForStart,
            actual?.OperationState);

        Assert.Equal(
            later.UpdatedAt,
            actual?.UpdatedAt);
    }

    [Fact]
    public async Task ClearRemovesRecoverableCheckpoint()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteFlightSessionCheckpointStore(
                databasePath);

        await store.SaveAsync(
            FlightSession.Start(
                DateTimeOffset.UtcNow));

        await store.ClearAsync();

        Assert.Null(
            await store.LoadAsync());
    }

    [Fact]
    public async Task UnsupportedSchemaIsRejectedBeforeWrite()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteFlightSessionCheckpointStore(
                databasePath);

        FlightSession unsupported =
            FlightSession.Start(
                DateTimeOffset.UtcNow)
            with
            {
                SchemaVersion = 99
            };

        await Assert.ThrowsAsync<NotSupportedException>(
            async () =>
                await store.SaveAsync(unsupported));
    }

    [Fact]
    public async Task ConcurrentSavesLeaveAValidCompleteCheckpoint()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteFlightSessionCheckpointStore(
                databasePath);

        FlightSession session =
            FlightSession.Start(
                new DateTimeOffset(
                    2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
                sessionId:
                    Guid.Parse(
                        "22222222-2222-2222-2222-222222222222"));

        FlightSession[] checkpoints =
            Enumerable
                .Range(1, 20)
                .Select(
                    seconds =>
                        session with
                        {
                            UpdatedAt =
                                session.CreatedAt
                                    .AddSeconds(seconds)
                        })
                .ToArray();

        await Task.WhenAll(
            checkpoints.Select(
                checkpoint =>
                    store.SaveAsync(checkpoint)));

        FlightSession? loaded =
            await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(
            session.SessionId,
            loaded.SessionId);

        Assert.Equal(
            checkpoints.Max(
                checkpoint =>
                    checkpoint.UpdatedAt),
            loaded.UpdatedAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(
                _directory,
                recursive: true);
        }
    }

    private static void AssertSessionEquivalent(
        FlightSession expected,
        FlightSession actual)
    {
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.ContractId, actual.ContractId);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.OperationState, actual.OperationState);
        Assert.Equal(expected.Tracking, actual.Tracking);
        Assert.Equal(expected.TimeLedger, actual.TimeLedger);
        Assert.Equal(expected.Milestones, actual.Milestones);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.ContinuityAnchor, actual.ContinuityAnchor);
        Assert.Equal(expected.Plan, actual.Plan);

        FlightSessionStatistics expectedStatistics =
            expected.EffectiveStatistics;

        FlightSessionStatistics actualStatistics =
            actual.EffectiveStatistics;

        Assert.Equal(
            expectedStatistics.DistanceNauticalMiles,
            actualStatistics.DistanceNauticalMiles);

        Assert.Equal(
            expectedStatistics.MaximumAltitudeMslFeet,
            actualStatistics.MaximumAltitudeMslFeet);

        Assert.Equal(
            expectedStatistics.MaximumIndicatedAirspeedKnots,
            actualStatistics.MaximumIndicatedAirspeedKnots);

        Assert.Equal(
            expectedStatistics.MaximumGroundSpeedKnots,
            actualStatistics.MaximumGroundSpeedKnots);

        Assert.Equal(
            expectedStatistics.StartFuelPounds,
            actualStatistics.StartFuelPounds);

        Assert.Equal(
            expectedStatistics.LastFuelPounds,
            actualStatistics.LastFuelPounds);

        Assert.Equal(
            expectedStatistics.FuelBurnedPounds,
            actualStatistics.FuelBurnedPounds);

        Assert.Equal(
            expectedStatistics.FuelAddedPounds,
            actualStatistics.FuelAddedPounds);

        Assert.Equal(
            expectedStatistics.StartPayloadPounds,
            actualStatistics.StartPayloadPounds);

        Assert.Equal(
            expectedStatistics.LastPayloadPounds,
            actualStatistics.LastPayloadPounds);

        Assert.Equal(
            expectedStatistics.RouteTrack,
            actualStatistics.RouteTrack);

        Assert.Equal(
            expected.EffectiveLandingEpisodes,
            actual.EffectiveLandingEpisodes);
    }

    private static FlightSession CreateAirborneSession()
    {
        DateTimeOffset epoch =
            new(
                2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        FlightSession session =
            FlightSession.Start(
                epoch,
                contractId:
                    Guid.Parse(
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                sessionId:
                    Guid.Parse(
                        "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                plan:
                    new FlightSessionPlan(
                        "KJFK",
                        "KJFK",
                        ExpectedCanonicalAircraftId:
                            "msfs-title:Cessna 172 Skyhawk"));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        epoch.AddSeconds(1),
                        Connected: true,
                        StableTelemetry: true,
                        ValidLoadedAircraft: true,
                        ContinuityPlausible: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        epoch.AddSeconds(2),
                        Connected: true,
                        ContinuityPlausible: true,
                        EngineStartObserved: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        epoch.AddSeconds(3),
                        Connected: true,
                        ContinuityPlausible: true,
                        SelfPoweredMovementForFlight: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        epoch.AddSeconds(4),
                        Connected: true,
                        ContinuityPlausible: true,
                        TakeoffCandidate: true)));

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    epoch.AddSeconds(5),
                    Connected: true,
                    ContinuityPlausible: true,
                    AirborneConfirmed: true)));
    }
}
