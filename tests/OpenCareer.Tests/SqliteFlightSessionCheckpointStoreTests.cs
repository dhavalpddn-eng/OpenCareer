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
            expected.EffectiveLegs,
            actual.EffectiveLegs);
        Assert.Equal(
            "msfs-title:Cessna 172 Skyhawk",
            actual.Plan?.ExpectedCanonicalAircraftId);
    }

    [Fact]
    public async Task CompletedLegStateSurvivesStoreReopen()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        FlightSession completed = CreateCompletedSession();

        await new SqliteFlightSessionCheckpointStore(databasePath)
            .SaveAsync(completed);

        FlightSession restored =
            Assert.IsType<FlightSession>(
                await new SqliteFlightSessionCheckpointStore(databasePath)
                    .LoadAsync());

        FlightLeg expectedLeg = Assert.Single(completed.EffectiveLegs);
        FlightLeg actualLeg = Assert.Single(restored.EffectiveLegs);
        Assert.Equal(expectedLeg, actualLeg);
        Assert.Equal(FlightLegStatus.Completed, actualLeg.Status);
        Assert.Equal(completed.Milestones.CompletedAt, actualLeg.CompletedAt);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task MultiLegCollectionSurvivesStoreReopen(
        int legCount,
        bool finalLegActive)
    {
        string databasePath = Path.Combine(_directory, "career.db");
        FlightSession expected =
            CreateMultiLegSession(legCount, finalLegActive);

        await new SqliteFlightSessionCheckpointStore(databasePath)
            .SaveAsync(expected);

        FlightSession actual =
            Assert.IsType<FlightSession>(
                await new SqliteFlightSessionCheckpointStore(databasePath)
                    .LoadAsync());

        AssertSessionEquivalent(expected, actual);
        Assert.Equal(expected.EffectiveLegs, actual.EffectiveLegs);
    }

    [Fact]
    public async Task TwoLegTimeLedgersSurviveStoreReopen()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        FlightSession session = FlightSession.Start(
            new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        FlightTimeInterval interval =
            new(
                TimeSpan.FromMinutes(1),
                1,
                ValidOperationalEvidence: true,
                Paused: false,
                SlewActive: false,
                CountsTowardBlockTime: true,
                CountsTowardFlightTime: true,
                Airborne: true,
                TaxiOut: false,
                TaxiIn: false,
                Night: false,
                ActualInstrument: false);
        session = FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    session.CreatedAt.AddMinutes(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true),
                TimeInterval: interval));
        session = session with
        {
            Legs =
            [
                session.EffectiveLegs[0].Complete(session.UpdatedAt)
            ]
        };
        session = FlightSessionEngine.StartNextLeg(
            session,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new FlightSessionPlan("KBOS", "KPHL"),
            session.UpdatedAt.AddMinutes(1));
        session = FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    session.UpdatedAt.AddMinutes(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true),
                TimeInterval: interval));

        await new SqliteFlightSessionCheckpointStore(databasePath).SaveAsync(session);
        FlightSession restored = Assert.IsType<FlightSession>(
            await new SqliteFlightSessionCheckpointStore(databasePath).LoadAsync());

        Assert.Equal(session.TimeLedger, restored.TimeLedger);
        Assert.Equal(session.EffectiveLegs[0].TimeLedger, restored.EffectiveLegs[0].TimeLedger);
        Assert.Equal(session.EffectiveLegs[1].TimeLedger, restored.EffectiveLegs[1].TimeLedger);
    }

    [Fact]
    public async Task DuplicateLegIdentityFailsClosed()
    {
        FlightSession session = CreateMultiLegSession(2, finalLegActive: true);
        FlightLeg[] legs = session.EffectiveLegs.ToArray();
        legs[1] = legs[1] with { LegId = legs[0].LegId };

        await AssertInvalidLegsAsync(session with { Legs = legs });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SequenceGapOrChronologicalOrderErrorFailsClosed(
        bool sequenceGap)
    {
        FlightSession session = CreateMultiLegSession(2, finalLegActive: true);
        FlightLeg[] legs = session.EffectiveLegs.ToArray();
        legs[1] =
            sequenceGap
                ? legs[1] with { Sequence = 3 }
                : legs[1] with
                {
                    StartedAt = legs[0].CompletedAt!.Value.AddMinutes(-1)
                };

        await AssertInvalidLegsAsync(session with { Legs = legs });
    }

    [Fact]
    public async Task CompletedLegAfterActiveLegFailsClosed()
    {
        FlightSession session = CreateMultiLegSession(2, finalLegActive: false);
        FlightLeg[] legs = session.EffectiveLegs.ToArray();
        legs[0] = legs[0] with
        {
            Status = FlightLegStatus.Active,
            CompletedAt = null
        };

        await AssertInvalidLegsAsync(session with { Legs = legs });
    }

    [Fact]
    public async Task MultipleActiveLegsFailClosed()
    {
        FlightSession session = CreateMultiLegSession(2, finalLegActive: true);
        FlightLeg[] legs = session.EffectiveLegs.ToArray();
        legs[0] = legs[0] with
        {
            Status = FlightLegStatus.Active,
            CompletedAt = null
        };

        await AssertInvalidLegsAsync(session with { Legs = legs });
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
            Assert.True(root.Remove("legs"));

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
        FlightLeg leg = Assert.Single(legacy.EffectiveLegs);
        Assert.Equal(legacy.SessionId, leg.LegId);
        Assert.Equal(legacy.CreatedAt, leg.StartedAt);
        Assert.Equal(legacy.Plan, leg.Plan);
        Assert.Equal(legacy.TimeLedger, leg.EffectiveTimeLedger);
        Assert.NotNull(legacy.Legs);
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
    public async Task ValidCurrentCheckpointWinsEvenWhenPreviousIsCorrupt()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteFlightSessionCheckpointStore(databasePath);
        FlightSession previous = CreateAirborneSession();
        FlightSession current = previous with
        {
            UpdatedAt = previous.UpdatedAt.AddSeconds(1)
        };

        await store.SaveAsync(previous);
        await store.SaveAsync(current);
        await ExecuteAsync(
            databasePath,
            "UPDATE flight_session_checkpoint_previous SET payload_json = '{';");

        FlightSession loaded = Assert.IsType<FlightSession>(await store.LoadAsync());
        AssertSessionEquivalent(current, loaded);
    }

    [Fact]
    public async Task CorruptCurrentCheckpointFallsBackToPreviousValidCheckpoint()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteFlightSessionCheckpointStore(databasePath);
        FlightSession previous = CreateAirborneSession();
        FlightSession current = previous with
        {
            UpdatedAt = previous.UpdatedAt.AddSeconds(1)
        };

        await store.SaveAsync(previous);
        await store.SaveAsync(current);
        await ExecuteAsync(
            databasePath,
            "UPDATE flight_session_checkpoint SET payload_json = '{';");

        FlightSession loaded = Assert.IsType<FlightSession>(await store.LoadAsync());
        AssertSessionEquivalent(previous, loaded);
    }

    [Fact]
    public async Task CorruptCurrentAndCorruptOrMissingPreviousFailClosed()
    {
        string corruptDatabasePath = Path.Combine(_directory, "corrupt.db");
        var corruptStore = new SqliteFlightSessionCheckpointStore(corruptDatabasePath);
        FlightSession first = CreateAirborneSession();
        FlightSession second = first with
        {
            UpdatedAt = first.UpdatedAt.AddSeconds(1)
        };

        await corruptStore.SaveAsync(first);
        await corruptStore.SaveAsync(second);
        await ExecuteAsync(
            corruptDatabasePath,
            """
            UPDATE flight_session_checkpoint SET payload_json = '{';
            UPDATE flight_session_checkpoint_previous SET payload_json = '{';
            """);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await corruptStore.LoadAsync());

        string missingDatabasePath = Path.Combine(_directory, "missing.db");
        var missingStore = new SqliteFlightSessionCheckpointStore(missingDatabasePath);
        await missingStore.SaveAsync(first);
        await ExecuteAsync(
            missingDatabasePath,
            "UPDATE flight_session_checkpoint SET payload_json = '{';");

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await missingStore.LoadAsync());
    }

    [Fact]
    public async Task FallbackNeverCrossesSessionIdentity()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteFlightSessionCheckpointStore(databasePath);
        FlightSession previous = CreateAirborneSession();
        FlightSession current = previous with
        {
            UpdatedAt = previous.UpdatedAt.AddSeconds(1)
        };

        await store.SaveAsync(previous);
        await store.SaveAsync(current);
        await ExecuteAsync(
            databasePath,
            $$"""
            UPDATE flight_session_checkpoint SET payload_json = '{';
            UPDATE flight_session_checkpoint_previous
            SET successor_session_id = '{{Guid.NewGuid():D}}';
            """);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await store.LoadAsync());
    }

    [Fact]
    public async Task SuccessfulSaveAfterCorruptionRestoresNormalCurrentRecovery()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteFlightSessionCheckpointStore(databasePath);
        FlightSession previous = CreateAirborneSession();
        FlightSession corrupt = previous with
        {
            UpdatedAt = previous.UpdatedAt.AddSeconds(1)
        };
        FlightSession restored = previous with
        {
            UpdatedAt = previous.UpdatedAt.AddSeconds(2)
        };

        await store.SaveAsync(previous);
        await store.SaveAsync(corrupt);
        await ExecuteAsync(
            databasePath,
            "UPDATE flight_session_checkpoint SET payload_json = '{';");

        await store.SaveAsync(restored);

        FlightSession loaded = Assert.IsType<FlightSession>(await store.LoadAsync());
        AssertSessionEquivalent(restored, loaded);
    }

    [Fact]
    public async Task MissingCurrentCheckpointDoesNotActivatePreviousCheckpoint()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteFlightSessionCheckpointStore(databasePath);
        FlightSession previous = CreateAirborneSession();
        FlightSession current = previous with
        {
            UpdatedAt = previous.UpdatedAt.AddSeconds(1)
        };

        await store.SaveAsync(previous);
        await store.SaveAsync(current);
        await ExecuteAsync(
            databasePath,
            "DELETE FROM flight_session_checkpoint;");

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task ClearRemovesRecoverableCheckpoint()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteFlightSessionCheckpointStore(
                databasePath);

        FlightSession first = FlightSession.Start(DateTimeOffset.UtcNow);
        await store.SaveAsync(first);
        await store.SaveAsync(
            first with
            {
                UpdatedAt = first.UpdatedAt.AddSeconds(1)
            });

        await store.ClearAsync();

        Assert.Null(
            await store.LoadAsync());

        await using var connection =
            new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM flight_session_checkpoint)
                + (SELECT COUNT(*) FROM flight_session_checkpoint_previous);
            """;
        Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync()));
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

    private static async Task ExecuteAsync(
        string databasePath,
        string sql)
    {
        await using var connection =
            new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task AssertInvalidLegsAsync(FlightSession session)
    {
        string databasePath = Path.Combine(_directory, "invalid.db");
        var store = new SqliteFlightSessionCheckpointStore(databasePath);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(session));
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
        Assert.Equal(expected.EffectiveLegs, actual.EffectiveLegs);

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

    private static FlightSession CreateCompletedSession()
    {
        FlightSession session = CreateAirborneSession();
        DateTimeOffset epoch = session.CreatedAt;

        session = Advance(session, epoch.AddSeconds(6), approach: true);
        session = Advance(session, epoch.AddSeconds(7), touchdown: true);
        session = Advance(session, epoch.AddSeconds(8), rollout: true);
        session = Advance(session, epoch.AddSeconds(9), parking: true);
        session = Advance(session, epoch.AddSeconds(10), shutdown: true);
        return Advance(session, epoch.AddSeconds(11), complete: true);
    }

    private static FlightSession CreateMultiLegSession(
        int legCount,
        bool finalLegActive)
    {
        DateTimeOffset epoch =
            new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        var firstPlan = new FlightSessionPlan("KJFK", "KBOS");
        FlightSession session =
            FlightSession.Start(
                epoch,
                sessionId:
                    Guid.Parse("11111111-2222-3333-4444-555555555555"),
                plan: firstPlan);

        var legs = new List<FlightLeg>(legCount);
        for (int index = 0; index < legCount; index++)
        {
            int sequence = index + 1;
            DateTimeOffset startedAt = epoch.AddHours(index * 2);
            bool active = finalLegActive && sequence == legCount;
            FlightSessionPlan plan =
                sequence == 1
                    ? firstPlan
                    : new FlightSessionPlan(
                        $"LEG{sequence - 1}",
                        $"LEG{sequence}");

            legs.Add(
                new FlightLeg(
                    sequence == 1
                        ? session.SessionId
                        : Guid.Parse($"00000000-0000-0000-0000-{sequence:D12}"),
                    sequence,
                    startedAt,
                    plan,
                    active
                        ? FlightLegStatus.Active
                        : FlightLegStatus.Completed,
                    active
                        ? null
                        : startedAt.AddHours(1)));
        }

        return session with
        {
            UpdatedAt = epoch.AddHours(legCount * 2),
            Legs = legs
        };
    }

    private static FlightSession Advance(
        FlightSession session,
        DateTimeOffset timestamp,
        bool approach = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false,
        bool shutdown = false,
        bool complete = false) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    timestamp,
                    Connected: true,
                    StableTelemetry: true,
                    ContinuityPlausible: true,
                    ApproachConfirmed: approach,
                    TouchdownConfirmed: touchdown,
                    LandingRolloutConfirmed: rollout,
                    ParkingConfirmed: parking,
                    OperationCompleteConfirmed: complete),
                ShutdownConfirmed: shutdown));
}
