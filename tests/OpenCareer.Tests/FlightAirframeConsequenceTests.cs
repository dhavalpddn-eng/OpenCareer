using Microsoft.Data.Sqlite;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Ownership;

namespace OpenCareer.Tests;

public sealed class FlightAirframeConsequenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(400, false, FlightDamageSeverity.Normal)]
    [InlineData(1050, false, FlightDamageSeverity.ElevatedWear)]
    [InlineData(1450, false, FlightDamageSeverity.MinorDamage)]
    [InlineData(2000, false, FlightDamageSeverity.MajorDamage)]
    [InlineData(400, true, FlightDamageSeverity.Severe)]
    public void SeverityUsesAcceptedNearGroundEvidenceAndCrash(
        double descent, bool crashed, FlightDamageSeverity expected)
    {
        FlightAirframeConsequence result = FlightAirframeConsequenceCalculator.Calculate(
            Finalized(Owned("O1"), descent, crashed));
        Assert.Equal(expected, result.Severity);
        Assert.Equal(1, result.Summary.LandingCycles);
        Assert.Equal(1, result.Summary.AirborneHours);
        Assert.Equal(crashed, result.Summary.CrashReported);
        if (expected == FlightDamageSeverity.Normal)
        {
            Assert.Equal(0, result.Usage.AdditionalDamagePercent);
            Assert.Equal(0, result.Usage.HardLandingSeverity);
        }
    }

    [Fact]
    public void OldApproachDescentCannotDamageLaterNormalLanding()
    {
        FlightSessionStatistics statistics = FlightSessionStatistics.Empty.Observe(
            new FlightSessionObservation(Start, 43, -76, 100, 100, 100, 100, 0,
                false, NearGroundDescentFeetPerMinute: 2200), null);
        statistics = statistics.Observe(
            new FlightSessionObservation(Start.AddMinutes(5), 43, -76, 100, 90, 90,
                95, 0, false, TouchdownConfirmed: true), null);
        Assert.Equal(2200, statistics.MaximumNearGroundDescentFeetPerMinute);
        Assert.Equal(0, statistics.MaximumTouchdownApproachDescentFeetPerMinute);

        var session = Finalized(Owned("O1"), 0) with { Statistics = statistics };
        Assert.Equal(FlightDamageSeverity.Normal,
            FlightAirframeConsequenceCalculator.Calculate(session).Severity);
    }

    [Fact]
    public async Task OwnedSameModelAppliesOnceToExactOwnershipAndRecoversSummary()
    {
        string path = TemporaryPath();
        try
        {
            var store = new SqliteOwnershipStore(path);
            await SeedOwnedAsync(store, path);
            FlightSession session = Finalized(Owned("O1"), 400);
            var checkpoints = new SqliteFlightSessionCheckpointStore(path);
            await checkpoints.SaveAsync(session);

            var recovered = new FlightSessionPersistenceService(
                new FlightSessionCoordinator(), new SqliteFlightSessionCheckpointStore(path),
                consequences: new SqliteOwnershipStore(path));
            FlightSession? restored = await recovered.RecoverAsync();
            Assert.Equal(session.AircraftIdentity, restored?.AircraftIdentity);
            Assert.Equal(session.SessionId, restored?.SessionId);
            await store.ApplyAsync(FlightAirframeConsequenceCalculator.Calculate(session));

            var snapshot = await store.LoadSnapshotAsync("career");
            var o1 = Assert.Single(snapshot.MaintenanceStates, x => x.OwnershipId == "O1");
            var o2 = Assert.Single(snapshot.MaintenanceStates, x => x.OwnershipId == "O2");
            Assert.Equal(1, o1.TrackedAirframeHours);
            Assert.Equal(1, o1.LandingCycles);
            Assert.True(o1.AirframeWearPercent > 0);
            Assert.Equal(0, o1.DamagePercent);
            Assert.Equal(0, o2.TrackedAirframeHours);
            Assert.Equal(0, o2.LandingCycles);
            Assert.Equal(0, o2.DamagePercent);
            Assert.Equal(session.SessionId, (await store.ReadAsync(session.SessionId))?.Summary.SessionId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(
                FlightAirframeConsequenceCalculator.Calculate(session with { AircraftIdentity = Owned("O2") })));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task AcceptedSessionTelemetryFinalizationAppliesOnceAfterRestart()
    {
        string path = TemporaryPath();
        try
        {
            var ownership = new SqliteOwnershipStore(path);
            await SeedOwnedAsync(ownership, path);
            var coordinator = new FlightSessionCoordinator();
            var persistence = new FlightSessionPersistenceService(
                coordinator, new SqliteFlightSessionCheckpointStore(path), consequences: ownership);
            await persistence.StartAsync(Start, sessionId: Guid.NewGuid(), aircraftIdentity: Owned("O1"));
            async Task Advance(int second, bool stable = false, bool movement = false,
                bool roll = false, bool airborne = false, bool touchdown = false,
                bool rollout = false, bool park = false, bool shutdown = false,
                FlightTimeInterval? interval = null, FlightSessionObservation? observation = null)
            {
                await persistence.AdvanceAsync(new FlightSessionAdvance(
                    new FlightStateEvidence(Start.AddSeconds(second), Connected: true,
                        StableTelemetry: stable, ValidLoadedAircraft: stable,
                        ContinuityPlausible: true, SelfPoweredMovementForFlight: movement,
                        TakeoffCandidate: roll, AirborneConfirmed: airborne,
                        TouchdownConfirmed: touchdown, LandingRolloutConfirmed: rollout,
                        ParkingConfirmed: park),
                    TimeInterval: interval, ShutdownConfirmed: shutdown,
                    Observation: observation));
            }

            await Advance(1, stable: true);
            await Advance(2, movement: true);
            await Advance(3, roll: true);
            await Advance(4, airborne: true);
            await Advance(3605, observation: new FlightSessionObservation(
                Start.AddSeconds(3605), 43.0, -76.0, 100, 110, 100, 100, 0, false,
                NearGroundDescentFeetPerMinute: 400),
                interval: new FlightTimeInterval(TimeSpan.FromHours(1), 1, true, false, false,
                    true, true, true, false, false, false, false));
            await Advance(3606, touchdown: true,
                observation: new FlightSessionObservation(Start.AddSeconds(3606),
                    43.0, -76.0, 100, 60, 50, 99, 0, false,
                    TouchdownConfirmed: true));
            await Advance(3607, rollout: true);
            await Advance(3608, park: true, shutdown: true);

            var completed = await new FlightSessionCompletionService(coordinator, persistence)
                .CompleteAsync(new FlightSessionCompletionRequest(Start.AddSeconds(3609), true, true));
            Assert.Equal(FlightSessionStatus.Completed, completed.Status);
            var before = Assert.Single((await ownership.LoadSnapshotAsync("career")).MaintenanceStates,
                state => state.OwnershipId == "O1");
            Assert.True(before.AirframeWearPercent > 0);
            Assert.Equal(0, before.DamagePercent);

            var restarted = new FlightSessionPersistenceService(
                new FlightSessionCoordinator(), new SqliteFlightSessionCheckpointStore(path),
                consequences: new SqliteOwnershipStore(path));
            Assert.Equal(Owned("O1"), (await restarted.RecoverAsync())?.AircraftIdentity);
            var after = Assert.Single((await ownership.LoadSnapshotAsync("career")).MaintenanceStates,
                state => state.OwnershipId == "O1");
            Assert.Equal(before, after);
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData(1450, false, FlightDamageSeverity.MinorDamage)]
    [InlineData(400, true, FlightDamageSeverity.Severe)]
    public async Task OwnedDamageIsPersistentWithoutFinancialSettlement(
        double descent, bool crashed, FlightDamageSeverity severity)
    {
        string path = TemporaryPath();
        try
        {
            var store = new SqliteOwnershipStore(path);
            await SeedOwnedAsync(store, path);
            var consequence = FlightAirframeConsequenceCalculator.Calculate(Finalized(Owned("O1"), descent, crashed));
            await store.ApplyAsync(consequence);
            var reloaded = new SqliteOwnershipStore(path);
            Assert.Equal(severity, (await reloaded.ReadAsync(consequence.Summary.SessionId))?.Severity);
            var state = Assert.Single((await reloaded.LoadSnapshotAsync("career")).MaintenanceStates,
                x => x.OwnershipId == "O1");
            Assert.True(state.DamagePercent > 0);
            Assert.Equal(0, Assert.Single((await reloaded.LoadSnapshotAsync("career")).MaintenanceStates,
                x => x.OwnershipId == "O2").DamagePercent);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ProviderConsequenceHasHistoryAndDoesNotCreateOwnership()
    {
        string path = TemporaryPath();
        try
        {
            var store = new SqliteOwnershipStore(path);
            await store.InitializeAsync();
            var provider = new FlightSessionAircraftIdentity(
                FlightSessionAircraftKind.Provider, Guid.NewGuid().ToString("D"), "A");
            var consequence = FlightAirframeConsequenceCalculator.Calculate(Finalized(provider, 400));
            await store.ApplyAsync(consequence);
            await store.ApplyAsync(consequence);
            var reopened = new SqliteOwnershipStore(path);
            Assert.Equal(provider, (await reopened.ReadAsync(consequence.Summary.SessionId))?.Summary.Aircraft);
            Assert.Empty((await reopened.LoadSnapshotAsync("career")).Aircraft);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ContractSelectionSurvivesRestartAndRejectsSameModelSwitch()
    {
        string path = TemporaryPath();
        try
        {
            var store = new SqliteOwnershipStore(path);
            await store.InitializeAsync();
            Guid contractId = Guid.NewGuid();
            var selections = (IContractAirframeSelectionStore)store;
            await selections.SaveAsync(contractId, Owned("O1"));
            var reopened = (IContractAirframeSelectionStore)new SqliteOwnershipStore(path);
            Assert.Equal(Owned("O1"), await reopened.ReadAsync(contractId));
            await reopened.SaveAsync(contractId, Owned("O1"));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => reopened.SaveAsync(contractId, Owned("O2")));
            Assert.Equal(Owned("O1"), await reopened.ReadAsync(contractId));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task CrashBetweenFinalCheckpointAndConsequenceRetriesOnRecovery()
    {
        string path = TemporaryPath();
        try
        {
            var ownership = new SqliteOwnershipStore(path);
            await SeedOwnedAsync(ownership, path);
            var coordinator = new FlightSessionCoordinator();
            var checkpoint = new SqliteFlightSessionCheckpointStore(path);
            var persistence = new FlightSessionPersistenceService(
                coordinator, checkpoint, consequences: new FailingConsequenceStore());
            FlightSession started = await persistence.StartAsync(Start, aircraftIdentity: Owned("O1"));
            await Assert.ThrowsAsync<IOException>(() => persistence.AdvanceAsync(
                new FlightSessionAdvance(new FlightStateEvidence(Start.AddSeconds(1),
                    Connected: true, CrashReported: true))));
            Assert.Equal(started.SessionId, (await checkpoint.LoadAsync())?.SessionId);
            Assert.Equal(FlightSessionStatus.Interrupted, (await checkpoint.LoadAsync())?.Status);
            Assert.Null(await ownership.ReadAsync(started.SessionId));

            var restarted = new FlightSessionPersistenceService(
                new FlightSessionCoordinator(), new SqliteFlightSessionCheckpointStore(path),
                consequences: new SqliteOwnershipStore(path));
            Assert.Equal(Owned("O1"), (await restarted.RecoverAsync())?.AircraftIdentity);
            Assert.Equal(FlightDamageSeverity.Severe, (await ownership.ReadAsync(started.SessionId))?.Severity);
            Assert.True(Assert.Single((await ownership.LoadSnapshotAsync("career")).MaintenanceStates,
                state => state.OwnershipId == "O1").DamagePercent > 0);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task LegacyCheckpointRecoversWithoutGuessedOwnership()
    {
        string path = TemporaryPath();
        try
        {
            var checkpoint = new SqliteFlightSessionCheckpointStore(path);
            await checkpoint.SaveAsync(Finalized(null, 400));
            var store = new SqliteOwnershipStore(path);
            await store.InitializeAsync();
            var persistence = new FlightSessionPersistenceService(
                new FlightSessionCoordinator(), checkpoint, consequences: store);
            FlightSession? recovered = await persistence.RecoverAsync();
            Assert.Null(recovered?.AircraftIdentity);
            Assert.Null(await store.ReadAsync(recovered!.SessionId));
        }
        finally { Cleanup(path); }
    }

    private static FlightSessionAircraftIdentity Owned(string id) =>
        new(FlightSessionAircraftKind.Owned, id, "A");

    private sealed class FailingConsequenceStore : IFlightAirframeConsequenceStore
    {
        public Task ApplyAsync(FlightAirframeConsequence consequence,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated crash after checkpoint.");

        public Task<FlightAirframeConsequence?> ReadAsync(Guid sessionId,
            CancellationToken cancellationToken = default) => Task.FromResult<FlightAirframeConsequence?>(null);
    }

    private static FlightSession Finalized(
        FlightSessionAircraftIdentity? aircraft, double descent, bool crashed = false)
    {
        FlightSession session = FlightSession.Start(Start, Guid.NewGuid(), aircraftIdentity: aircraft);
        return session with
        {
            Status = crashed ? FlightSessionStatus.Interrupted : FlightSessionStatus.Completed,
            UpdatedAt = Start.AddHours(2),
            Tracking = session.Tracking with { LandingEpisodeCount = 1, CrashReported = crashed },
            TimeLedger = session.TimeLedger with
            {
                AirborneTime = TimeSpan.FromHours(1),
                BlockTime = TimeSpan.FromHours(1.5)
            },
            Statistics = session.EffectiveStatistics with
            {
                MaximumNearGroundDescentFeetPerMinute = descent,
                MaximumTouchdownApproachDescentFeetPerMinute = descent,
                FuelBurnedPounds = 15,
                MaximumIndicatedAirspeedKnots = 110
            }
        };
    }

    private static async Task SeedOwnedAsync(SqliteOwnershipStore store, string path)
    {
        await store.InitializeAsync();
        await store.SetCareerAccountAsync(new("career", 30_000m, 0m));
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        for (int i = 1; i <= 2; i++)
        {
            string id = $"O{i}";
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO owned_aircraft
                    (ownership_id, career_id, aircraft_id, display_name, source_listing_id,
                     purchase_price_cents, acquired_condition_percent, current_airport_icao, acquired_at, status)
                VALUES ($id, 'career', 'A', 'Fixture', $listing, 10000000, 100, 'KRME', $at, 0);
                INSERT INTO maintenance_state
                    (ownership_id, tracked_airframe_hours, tracked_engine_hours, landing_cycles,
                     airframe_wear, engine_wear, gear_wear, damage, next_inspection_hours, confidence, updated_at)
                VALUES ($id, 0, 0, 0, 0, 0, 0, 0, 50, 0, $at);
                """;
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$listing", $"listing-{id}");
            insert.Parameters.AddWithValue("$at", Start.AddDays(-1).ToString("O"));
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"opencareer-consequence-{Guid.NewGuid():N}.db");

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
