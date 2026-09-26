using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed partial class AirframeInspectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_root, "inspection.db");
    private static DateTimeOffset Epoch => ConsequenceFixture.Epoch;
    private SqliteAirframeStore Store() => new(new(DatabasePath), NullLogger<SqliteAirframeStore>.Instance);
    private AirframeMaintenanceService Service() { var store = Store(); return new(store, store); }
    private AirframeMaintenanceHistorySource History() { var store = Store(); return new(store, store, store); }
    private Task<AirframeStoreRecord> CreateAsync(AirframeDamageState damage = AirframeDamageState.None, double wear = 0.1234567890123456) =>
        Store().CreateAsync(new(new AirframeId(Guid.NewGuid()), ConsequenceFixture.Model, Epoch.AddDays(-1)), new(wear, damage), Epoch);
    private async Task<AirframeServiceState> StateAsync(AirframeId id) => Assert.IsType<AirframeServiceState>(await Store().ReadServiceStateAsync(id));
    private static AirframeInspectionRequest Request(AirframeStoreRecord condition, AirframeServiceState service) =>
        new(Guid.NewGuid(), condition.Airframe.AirframeId, condition.Revision, service.Revision,
            (condition.SavedAt > service.UpdatedAt ? condition.SavedAt : service.UpdatedAt).AddMinutes(1));

    private async Task<FlightAirframeApplyResult> FlyAsync(AirframeStoreRecord before, TimeSpan time,
        FlightSessionStatus status = FlightSessionStatus.Completed, bool crash = false, double[]? verticalSpeeds = null)
    {
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId, status, crash, verticalSpeeds ?? []);
        return await ApplyFlightAsync(before, time, session);
    }

    private async Task<FlightAirframeApplyResult> ApplyFlightAsync(
        AirframeStoreRecord before, TimeSpan time, FlightSession session)
    {
        session = session with { UpdatedAt = before.SavedAt.Add(time).AddHours(4),
            TimeLedger = session.TimeLedger with { AirborneTime = time, BlockTime = time } };
        var decision = FlightAirframeConsequenceCalculator.Calculate(session);
        return await Store().ApplyAsync(decision, before, session.UpdatedAt);
    }

    [Fact]
    public async Task CreationAtomicallyInitializesIndependentExactUsageAndFallback()
    {
        var a = await CreateAsync(); var b = await CreateAsync();
        var state = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(TimeSpan.Zero, state.TotalTrackedAirborneTime);
        Assert.Equal(TimeSpan.Zero, state.LastInspectionAtTrackedAirborneTime);
        Assert.Equal(TimeSpan.FromHours(50), state.NextInspectionDueAtTrackedAirborneTime);
        Assert.Equal("LightAircraftRoutineInspection", state.ScheduleId);
        Assert.Equal(1, state.ScheduleVersion); Assert.Equal(1, state.Revision);
        Assert.Equal(a.SavedAt, state.UpdatedAt);
        Assert.Equal(AirframeUsageOrigin.TrackingFromCreation, state.UsageOrigin);
        Assert.Equal(0, state.TotalTrackedLandingCycles);
        Assert.Equal(AirframeUsageOrigin.TrackingFromCreation, state.LandingCycleOrigin);
        Assert.Equal(AirframeInspectionStatus.Current, state.InspectionStatus);
        var other = await StateAsync(b.Airframe.AirframeId);
        await FlyAsync(a, TimeSpan.FromHours(1), verticalSpeeds: [-500]);
        Assert.Equal(1, (await StateAsync(a.Airframe.AirframeId)).TotalTrackedLandingCycles);
        Assert.Equal(other, await StateAsync(b.Airframe.AirframeId));
        Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
        Assert.Equal(18L, await ScalarAsync("PRAGMA user_version;"));
        await ExecuteAsync("CREATE TRIGGER fail_init BEFORE INSERT ON airframe_service_state BEGIN SELECT RAISE(ABORT, 'injected'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => CreateAsync());
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM airframes;"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM airframe_service_state;"));
    }

    [Fact]
    public async Task TrustedUsageIsExactOnceAcrossFlightsAndRestart()
    {
        var a = await CreateAsync();
        var first = await FlyAsync(a, TimeSpan.FromHours(1), verticalSpeeds: [-500, -1600, -800]);
        var state = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(TimeSpan.FromHours(1), state.TotalTrackedAirborneTime);
        Assert.Equal(1, state.TotalTrackedLandingCycles);
        Assert.Equal(AirframeUsageOrigin.TrackingFromCreation, state.LandingCycleOrigin);
        Assert.Equal(2, state.Revision);
        ClearPool();
        Assert.False((await Store().ApplyAsync(first.Application.Consequence, a, first.Application.AppliedAt)).WasNewlyApplied);
        Assert.Equal(state, await StateAsync(a.Airframe.AirframeId));
        var twoEpisodes = ConsequenceFixture.SessionWithLandingEpisodes(
            a.Airframe.AirframeId, [-400], [-700, -600]);
        var second = await ApplyFlightAsync(first.Application.After, TimeSpan.FromTicks(123456789), twoEpisodes);
        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromTicks(123456789), (await StateAsync(a.Airframe.AirframeId)).TotalTrackedAirborneTime);
        Assert.Equal(3, (await StateAsync(a.Airframe.AirframeId)).TotalTrackedLandingCycles);
        Assert.Equal(3, (await StateAsync(a.Airframe.AirframeId)).Revision);
        Assert.False((await Store().ApplyAsync(first.Application.Consequence, second.Application.After, second.Application.AppliedAt)).WasNewlyApplied);
        Assert.Equal(3, (await StateAsync(a.Airframe.AirframeId)).Revision);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public async Task AuthoritativeLandingCounterTracksLegacyCyclesWithoutContactDetails()
    {
        var a = await CreateAsync();
        var session = ConsequenceFixture.Session(a.Airframe.AirframeId);
        session = session with
        {
            Tracking = session.Tracking with { LandingEpisodeCount = 2 },
            LandingEpisodes = []
        };
        var result = await ApplyFlightAsync(a, TimeSpan.FromHours(1), session);
        Assert.Equal(2, result.Application.Consequence.Summary.LandingEpisodeCount);
        Assert.Empty(result.Application.Consequence.Summary.LandingEpisodes);
        Assert.Equal(2, (await StateAsync(a.Airframe.AirframeId)).TotalTrackedLandingCycles);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Completed, false, true)]
    [InlineData(FlightSessionStatus.Cancelled, false, true)]
    [InlineData(FlightSessionStatus.Interrupted, false, false)]
    [InlineData(FlightSessionStatus.Interrupted, true, true)]
    public async Task AirborneUsageFollowsConditionDecisionButConfirmedLandingCyclesRemainFactual(
        FlightSessionStatus status, bool crash, bool tracked)
    {
        var a = await CreateAsync(); var initial = await StateAsync(a.Airframe.AirframeId);
        var result = await FlyAsync(a, TimeSpan.FromHours(1), status, crash, [-500]);
        Assert.Equal(tracked, result.Application.Consequence.ApplyCondition);
        var service = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(tracked ? TimeSpan.FromHours(1) : TimeSpan.Zero, service.TotalTrackedAirborneTime);
        Assert.Equal(1, service.TotalTrackedLandingCycles);
        Assert.Equal(AirframeUsageOrigin.TrackingFromCreation, service.LandingCycleOrigin);
        Assert.Equal(2, service.Revision);
        if (!tracked)
        {
            Assert.Equal(initial.TotalTrackedAirborneTime, service.TotalTrackedAirborneTime);
            Assert.Equal(initial.UsageOrigin, service.UsageOrigin);
            Assert.Equal(a, result.Application.After);
        }
        if (crash) Assert.True(result.Application.After.Condition.RequiresGrounding);
        Assert.False((await Store().ApplyAsync(result.Application.Consequence, result.Application.After,
            result.Application.AppliedAt)).WasNewlyApplied);
        Assert.Equal(service, await StateAsync(a.Airframe.AirframeId));
    }

    [Fact]
    public async Task UntrustedInterruptedFlightWithoutConfirmedLandingSkipsServiceUpdate()
    {
        var a = await CreateAsync();
        var initial = await StateAsync(a.Airframe.AirframeId);
        var result = await FlyAsync(a, TimeSpan.FromHours(1), FlightSessionStatus.Interrupted, crash: false);
        Assert.False(result.Application.Consequence.ApplyCondition);
        Assert.Equal(0, result.Application.Consequence.Summary.LandingEpisodeCount);
        Assert.Equal(initial, await StateAsync(a.Airframe.AirframeId));
        Assert.Equal(a, result.Application.After);
    }

    [Fact]
    public async Task InterruptedLandingCycleAndHistoryCommitAtomically()
    {
        var a = await CreateAsync();
        var initial = await StateAsync(a.Airframe.AirframeId);
        await ExecuteAsync("""
            CREATE TRIGGER fail_interrupted_history BEFORE INSERT ON flight_airframe_consequences
            BEGIN SELECT RAISE(ABORT, 'injected'); END;
            """);
        await Assert.ThrowsAsync<SqliteException>(() => FlyAsync(a, TimeSpan.FromHours(1),
            FlightSessionStatus.Interrupted, crash: false, verticalSpeeds: [-500]));
        Assert.Equal(initial, await StateAsync(a.Airframe.AirframeId));
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
        await ExecuteAsync("DROP TRIGGER fail_interrupted_history;");

        var applied = await FlyAsync(a, TimeSpan.FromHours(1), FlightSessionStatus.Interrupted,
            crash: false, verticalSpeeds: [-500]);
        var service = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(TimeSpan.Zero, service.TotalTrackedAirborneTime);
        Assert.Equal(1, service.TotalTrackedLandingCycles);
        Assert.Equal(a, applied.Application.After);
    }

    [Fact]
    public async Task ModelOnlyFlightDoesNotReadOrCreateServiceState()
    {
        var session = ConsequenceFixture.Session(verticalSpeeds: [-500]) with { AircraftIdentity = new(ConsequenceFixture.Model) };
        var store = Store();
        Assert.Null(await new FlightAirframeConsequenceCoordinator(store, store, TimeProvider.System).ApplyAsync(session));
        Assert.False(File.Exists(DatabasePath));
        Assert.True((await new PhysicalAirframeEligibilityService().EvaluateAsync(ConsequenceFixture.Model, null)).IsEligible);
    }

    [Theory]
    [InlineData(-1, AirframeInspectionStatus.Current)]
    [InlineData(0, AirframeInspectionStatus.InspectionDue)]
    [InlineData(1, AirframeInspectionStatus.InspectionDue)]
    public async Task ExactFiftyHourBoundaryHasNoGraceOrFloatingComparison(int seconds, AirframeInspectionStatus expected)
    {
        var a = await CreateAsync(AirframeDamageState.Recorded, 1);
        await FlyAsync(a, TimeSpan.FromHours(50) + TimeSpan.FromSeconds(seconds), verticalSpeeds: [-500]);
        var service = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(1, service.TotalTrackedLandingCycles);
        Assert.Equal(expected, service.InspectionStatus);
        Assert.Equal(TimeSpan.FromSeconds(Math.Max(0, -seconds)), service.TimeUntilInspection);
        var snapshot = (await History().ReadAsync(new(a.Airframe.AirframeId))).Snapshot!;
        Assert.Equal(service, snapshot.ServiceState);
        Assert.Equal(expected, snapshot.InspectionStatus);
        Assert.Equal(service.TotalTrackedAirborneTime.TotalHours, snapshot.TotalTrackedAirborneHours);
        Assert.Equal(service.TimeUntilInspection.TotalHours, snapshot.HoursUntilInspection);
        var eligibility = await new PhysicalAirframeEligibilityService(Store()).EvaluateAsync(ConsequenceFixture.Model, a.Airframe.AirframeId);
        Assert.Equal(seconds < 0 ? PhysicalAirframeEligibilityStatus.Eligible : PhysicalAirframeEligibilityStatus.InspectionDue, eligibility.Status);
        Assert.Equal(seconds < 0 ? AirframeServiceability.AvailableForDispatch : AirframeServiceability.InspectionDue, snapshot.Serviceability);
        Assert.Equal(AirframeDamageState.Recorded, snapshot.Condition.Damage);
    }

    [Theory]
    [InlineData("AFTER UPDATE ON airframe_service_state")]
    [InlineData("AFTER UPDATE ON airframes")]
    [InlineData("BEFORE INSERT ON flight_airframe_consequences")]
    [InlineData("AFTER INSERT ON flight_airframe_consequences")]
    public async Task ConsequenceFailureRollsBackConditionHistoryAndUsage(string point)
    {
        var a = await CreateAsync(); var state = await StateAsync(a.Airframe.AirframeId);
        await ExecuteAsync($"CREATE TRIGGER fail_usage {point} BEGIN SELECT RAISE(ABORT, 'injected'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => FlyAsync(a, TimeSpan.FromHours(1), verticalSpeeds: [-500]));
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(state, await StateAsync(a.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
        await ExecuteAsync("DROP TRIGGER fail_usage;");
        Assert.True((await FlyAsync(a, TimeSpan.FromHours(1), verticalSpeeds: [-500])).WasNewlyApplied);
        Assert.Equal(TimeSpan.FromHours(1), (await StateAsync(a.Airframe.AirframeId)).TotalTrackedAirborneTime);
        Assert.Equal(1, (await StateAsync(a.Airframe.AirframeId)).TotalTrackedLandingCycles);
    }

    [Theory]
    [InlineData(AirframeDamageState.None)]
    [InlineData(AirframeDamageState.Recorded)]
    [InlineData(AirframeDamageState.Grounding)]
    public async Task DueInspectionChangesOnlyServiceStateAndRestoresOnlyInspectionEligibility(AirframeDamageState damage)
    {
        var a = await CreateAsync(damage, 1); var b = await CreateAsync(damage);
        var other = await StateAsync(b.Airframe.AirframeId);
        var condition = (await FlyAsync(a, TimeSpan.FromHours(51), verticalSpeeds: [-500])).Application.After;
        var due = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(1, due.TotalTrackedLandingCycles);
        var request = Request(condition, due);
        await ExecuteAsync("CREATE TRIGGER no_condition_update BEFORE UPDATE ON airframes BEGIN SELECT RAISE(ABORT, 'inspection changed condition'); END;");
        var result = await Service().PerformRoutineInspectionAsync(request);
        Assert.Equal(AirframeInspectionResultStatus.Inspected, result.Status); Assert.True(result.WasNewlyApplied);
        Assert.Equal(condition, result.Condition);
        Assert.Equal(condition, await Store().FindAsync(a.Airframe.AirframeId));
        var after = Assert.IsType<AirframeServiceState>(result.ServiceState);
        Assert.Equal(TimeSpan.FromHours(51), after.TotalTrackedAirborneTime);
        Assert.Equal(due.TotalTrackedLandingCycles, after.TotalTrackedLandingCycles);
        Assert.Equal(TimeSpan.FromHours(51), after.LastInspectionAtTrackedAirborneTime);
        Assert.Equal(TimeSpan.FromHours(101), after.NextInspectionDueAtTrackedAirborneTime);
        Assert.Equal(due.Revision + 1, after.Revision); Assert.Equal(request.PerformedAt, after.UpdatedAt);
        Assert.Equal(AirframeInspectionStatus.Current, after.InspectionStatus);
        Assert.Equal(due.UsageOrigin, after.UsageOrigin);
        Assert.Equal(due.LandingCycleOrigin, after.LandingCycleOrigin);
        var retained = Assert.IsType<AirframeRoutineInspectionEvent>(result.Event);
        Assert.Equal(condition, retained.Before); Assert.Equal(condition, retained.After);
        Assert.Equal(due, retained.ServiceBefore); Assert.Equal(after, retained.ServiceAfter);
        Assert.Equal(request, retained.Request); retained.Validate();
        Assert.Equal(3L, await ScalarAsync(
            "SELECT payload_schema_version FROM airframe_maintenance_events WHERE maintenance_action_id='" +
            request.MaintenanceActionId.ToString("D") + "';"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            "UPDATE airframe_maintenance_events SET payload_schema_version=1 WHERE maintenance_action_id=$action;",
            ("$action", request.MaintenanceActionId.ToString("D"))));
        var snapshot = (await History().ReadAsync(new(a.Airframe.AirframeId))).Snapshot!;
        Assert.Equal(damage == AirframeDamageState.Grounding ? AirframeServiceability.Grounded : AirframeServiceability.AvailableForDispatch, snapshot.Serviceability);
        Assert.Equal(damage != AirframeDamageState.Grounding,
            (await new PhysicalAirframeEligibilityService(Store()).EvaluateAsync(ConsequenceFixture.Model, a.Airframe.AirframeId)).IsEligible);
        Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId)); Assert.Equal(other, await StateAsync(b.Airframe.AirframeId));
    }

    [Fact]
    public async Task NotDueAndMissingAreExplicitWithNoHistoryOrStateWrites()
    {
        var a = await CreateAsync(); var state = await StateAsync(a.Airframe.AirframeId);
        await ExecuteAsync("""
            CREATE TRIGGER no_usage_update BEFORE UPDATE ON airframe_service_state BEGIN SELECT RAISE(ABORT, 'unexpected'); END;
            CREATE TRIGGER no_condition_update BEFORE UPDATE ON airframes BEGIN SELECT RAISE(ABORT, 'unexpected'); END;
            CREATE TRIGGER no_history BEFORE INSERT ON airframe_maintenance_events BEGIN SELECT RAISE(ABORT, 'unexpected'); END;
            """);
        var request = Request(a, state);
        var result = await Service().PerformRoutineInspectionAsync(request);
        Assert.Equal(AirframeInspectionResultStatus.InspectionNotDue, result.Status);
        Assert.Null(result.Event); Assert.False(result.WasNewlyApplied); Assert.Equal(a, result.Condition); Assert.Equal(state, result.ServiceState);
        Assert.Null(await Store().FindMaintenanceActionAsync(request.MaintenanceActionId));
        Assert.Equal(AirframeInspectionResultStatus.NotFound, (await Service().PerformRoutineInspectionAsync(request with { AirframeId = new(Guid.NewGuid()) })).Status);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task InspectionReplayRetainsResultAcrossRestartAndLaterUsage()
    {
        var a = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50), verticalSpeeds: [-500])).Application.After;
        var due = await StateAsync(a.Airframe.AirframeId); var request = Request(a, due);
        var first = await Service().PerformRoutineInspectionAsync(request);
        ClearPool();
        var replay = await Service().PerformRoutineInspectionAsync(request);
        Assert.False(replay.WasNewlyApplied); Assert.Equal(first.Event, replay.Event);
        var next = await Store().ApplyAsync(FlightAirframeConsequenceCalculator.Calculate(
                ConsequenceFixture.Session(a.Airframe.AirframeId, verticalSpeeds: [-500])),
            a, request.PerformedAt.AddHours(3));
        var afterUsage = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(1, first.ServiceState!.TotalTrackedLandingCycles);
        Assert.Equal(2, afterUsage.TotalTrackedLandingCycles);
        Assert.Equal(first.ServiceState, (await Service().PerformRoutineInspectionAsync(request)).ServiceState);
        Assert.Equal(afterUsage, await StateAsync(a.Airframe.AirframeId));
        Assert.Equal(next.Application.After, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Theory]
    [InlineData("AFTER UPDATE ON airframe_service_state")]
    [InlineData("BEFORE INSERT ON airframe_maintenance_events")]
    [InlineData("AFTER INSERT ON airframe_maintenance_events")]
    public async Task InspectionFailureRollsBackBothWritesAndSameRequestRetriesOnce(string point)
    {
        var a = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50))).Application.After;
        var state = await StateAsync(a.Airframe.AirframeId); var request = Request(a, state);
        await ExecuteAsync($"CREATE TRIGGER fail_inspection {point} BEGIN SELECT RAISE(ABORT, 'injected'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Service().PerformRoutineInspectionAsync(request));
        Assert.Equal(state, await StateAsync(a.Airframe.AirframeId)); Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Null(await Store().FindMaintenanceActionAsync(request.MaintenanceActionId));
        await ExecuteAsync("DROP TRIGGER fail_inspection;");
        Assert.True((await Service().PerformRoutineInspectionAsync(request)).WasNewlyApplied);
        Assert.False((await Service().PerformRoutineInspectionAsync(request)).WasNewlyApplied);
        Assert.Equal(state.Revision + 1, (await StateAsync(a.Airframe.AirframeId)).Revision);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(); return connection;
    }
    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
    private void ClearPool()
    {
        using var pool = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString());
        SqliteConnection.ClearPool(pool);
    }
    public void Dispose() { ClearPool(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
