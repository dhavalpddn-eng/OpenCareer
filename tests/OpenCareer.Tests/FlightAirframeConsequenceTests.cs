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
    public void StrongestRecentSampleAndBounceRecontactStayOneLandingCycle()
    {
        static FlightSessionObservation Sample(int second, double? descent = null,
            bool touchdown = false, bool bounce = false) => new(
                Start.AddSeconds(second), 43, -76, 100, 90, 80, 90, 0, false,
                descent, touchdown, bounce);

        FlightSessionStatistics statistics = FlightSessionStatistics.Empty
            .Observe(Sample(0, 1450), null)
            .Observe(Sample(5, 550), null)
            .Observe(Sample(12, touchdown: true), null)
            .Observe(Sample(14, 1800), null)
            .Observe(Sample(19, bounce: true), null);
        Assert.Equal(1800, statistics.MaximumTouchdownApproachDescentFeetPerMinute);
        Assert.Equal(Start.AddSeconds(14), statistics.TouchdownDescentSampleAt);
        Assert.Equal(Start.AddSeconds(19), statistics.CorrelatedTouchdownAt);

        FlightSession session = Finalized(Owned("O1"), 0) with { Statistics = statistics };
        FlightAirframeConsequence consequence = FlightAirframeConsequenceCalculator.Calculate(session);
        Assert.Equal(FlightDamageSeverity.MajorDamage, consequence.Severity);
        Assert.Equal(1, consequence.Usage.LandingCycles);
        Assert.Equal(5d, consequence.Decision?.CorrelationAgeSeconds);
        Assert.True(consequence.Decision!.DescentWithinCorrelationWindow);
    }

    [Fact]
    public void LowFrequencyTouchdownSampleWithinWindowCorrelatesButLaterOneDoesNot()
    {
        FlightSessionObservation approach = new(Start, 43, -76, 100, 90, 80, 90, 0,
            false, NearGroundDescentFeetPerMinute: 1400);
        FlightSessionStatistics within = FlightSessionStatistics.Empty.Observe(approach, null)
            .Observe(new FlightSessionObservation(Start.AddSeconds(20), 43, -76, 100,
                40, 30, 90, 0, false, TouchdownConfirmed: true), null);
        FlightSessionStatistics outside = FlightSessionStatistics.Empty.Observe(approach, null)
            .Observe(new FlightSessionObservation(Start.AddSeconds(21), 43, -76, 100,
                40, 30, 90, 0, false, TouchdownConfirmed: true), null);
        Assert.Equal(1400, within.MaximumTouchdownApproachDescentFeetPerMinute);
        Assert.Equal(Start, within.TouchdownDescentSampleAt);
        Assert.Null(outside.CorrelatedTouchdownAt);
        Assert.Equal(0, outside.MaximumTouchdownApproachDescentFeetPerMinute);
    }

    [Fact]
    public void DuplicateAcceptedObservationCannotChangeSummaryOrCorrelation()
    {
        var approach = new FlightSessionObservation(Start, 43, -76, 100, 90,
            80, 90, 0, false, NearGroundDescentFeetPerMinute: 1300);
        FlightSessionStatistics once = FlightSessionStatistics.Empty.Observe(approach, null);
        Assert.Equal(Start, once.LastAcceptedObservationAt);
        Assert.Equal(1, once.AcceptedObservationCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => once.Observe(approach, null));
        Assert.Equal(1, once.AcceptedObservationCount);
        Assert.Equal(1300, once.MaximumNearGroundDescentFeetPerMinute);
    }

    [Fact]
    public void IncreasingDescentHasMonotoneSeverityAndForgivingFirmWear()
    {
        double[] rates = [450, 1050, 1450, 2000];
        FlightAirframeConsequence[] results = rates.Select(rate =>
            FlightAirframeConsequenceCalculator.Calculate(Finalized(Owned("O1"), rate)))
            .ToArray();
        Assert.Equal(FlightDamageSeverity.Normal, results[0].Severity);
        Assert.Equal(FlightDamageSeverity.ElevatedWear, results[1].Severity);
        Assert.Equal(FlightDamageSeverity.MinorDamage, results[2].Severity);
        Assert.Equal(FlightDamageSeverity.MajorDamage, results[3].Severity);
        for (int i = 1; i < results.Length; i++)
        {
            Assert.True((int)results[i].Severity >= (int)results[i - 1].Severity);
            Assert.True(results[i].Decision!.AdditionalLandingGearWearPercent
                >= results[i - 1].Decision!.AdditionalLandingGearWearPercent);
        }
        Assert.Equal(0, results[1].Decision?.CrashDamagePercent);
        Assert.Equal(0, results[1].Decision!.TouchdownDamagePercent);
        Assert.True(results[1].Decision.AdditionalLandingGearWearPercent > 0);
    }

    [Fact]
    public void FutureAircraftCalibrationCanBeSuppliedWithoutChangingSessionHistory()
    {
        FlightSession session = Finalized(Owned("O1"), 1000);
        FlightAirframeConsequence baseline = FlightAirframeConsequenceCalculator.Calculate(session);
        FlightAirframeConsequence adjusted = FlightAirframeConsequenceCalculator.Calculate(
            session, FlightAirframeCalibration.Conservative with
            {
                ElevatedDescentFpm = 1100,
                MinorDamageDescentFpm = 1500,
                MajorDamageDescentFpm = 2000
            });
        Assert.Equal(FlightDamageSeverity.ElevatedWear, baseline.Severity);
        Assert.Equal(FlightDamageSeverity.Normal, adjusted.Severity);
        Assert.Equal(session.SessionId, adjusted.Summary.SessionId);
        Assert.Equal(1100, adjusted.Decision?.ElevatedThresholdFpm);
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
            FlightAirframeConsequence saved = (await store.ReadLatestAsync())!;
            Assert.Equal(FlightSessionStatus.Completed, saved.Summary.FinalizationStatus);
            Assert.Equal(session.EffectiveStatistics.AcceptedObservationCount,
                saved.Summary.AcceptedObservationCount);
            Assert.Equal(o1, saved.Application?.After);
            Assert.Equal("O1", saved.Summary.Aircraft.InstanceId);
            Assert.Equal(0, saved.Application?.Before?.DamagePercent);

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
            Assert.Equal(2, (await ownership.ReadAsync(completed.SessionId))?.Summary.AcceptedObservationCount);
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
            Assert.Equal(1, await CountRowsAsync(path, "flight_airframe_consequences"));
            Assert.Equal(1, await CountRowsAsync(path, "maintenance_events"));
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
            if (!crashed)
                Assert.InRange(state.DamagePercent, 0.01, 1.0);
            else
                Assert.True(state.DamagePercent >= 40);
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
            Assert.NotNull((await reopened.ReadLatestAsync())?.Decision);
            Assert.Null((await reopened.ReadLatestAsync())?.Application?.After);
            Assert.Empty((await reopened.LoadSnapshotAsync("career")).Aircraft);
            Assert.Equal(1, await CountRowsAsync(path, "flight_airframe_consequences"));
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
            Assert.Equal(started.SessionId, (await ownership.ReadLatestAsync())?.Summary.SessionId);
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
        public Task<FlightAirframeConsequence?> ReadLatestAsync(
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
                TouchdownDescentSampleAt = Start.AddHours(1).AddSeconds(-2),
                CorrelatedTouchdownAt = Start.AddHours(1),
                FuelBurnedPounds = 15,
                MaximumIndicatedAirspeedKnots = 110
            },
            LandingEpisodes = [new FlightSessionLandingEpisode(1,
                Start.AddHours(1), FlightSessionLandingKind.FullStop, 0)]
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

    private static async Task<long> CountRowsAsync(string path, string table)
    {
        // Table names are fixed test constants, never user input.
        if (table is not ("flight_airframe_consequences" or "maintenance_events"))
            throw new ArgumentException("Unsupported test table.", nameof(table));
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
