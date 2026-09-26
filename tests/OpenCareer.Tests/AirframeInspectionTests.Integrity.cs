using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Flights;

namespace OpenCareer.Tests;

public sealed partial class AirframeInspectionTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("airframe")]
    [InlineData("condition-revision")]
    [InlineData("service-revision")]
    [InlineData("time")]
    public async Task InvalidRequestFailsBeforeDatabaseAccess(string fault)
    {
        var valid = new AirframeInspectionRequest(Guid.NewGuid(), new(Guid.NewGuid()), 1, 1, Epoch);
        var request = fault switch
        {
            "action" => valid with { MaintenanceActionId = Guid.Empty },
            "airframe" => valid with { AirframeId = default },
            "condition-revision" => valid with { ExpectedConditionRevision = 0 },
            "service-revision" => valid with { ExpectedServiceRevision = 0 },
            _ => valid with { PerformedAt = default }
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service().PerformRoutineInspectionAsync(request));
        Assert.False(File.Exists(DatabasePath));
    }

    [Theory]
    [InlineData("condition-revision")]
    [InlineData("service-revision")]
    [InlineData("backdated")]
    [InlineData("same-time")]
    public async Task StaleOrBackdatedInspectionFailsWithoutWrites(string fault)
    {
        var a = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50))).Application.After;
        var state = await StateAsync(a.Airframe.AirframeId);
        var valid = Request(a, state);
        var request = fault switch
        {
            "condition-revision" => valid with { ExpectedConditionRevision = a.Revision - 1 },
            "service-revision" => valid with { ExpectedServiceRevision = state.Revision - 1 },
            "backdated" => valid with { PerformedAt = state.UpdatedAt.AddTicks(-1) },
            _ => valid with { PerformedAt = state.UpdatedAt }
        };
        var error = await Record.ExceptionAsync(() => Service().PerformRoutineInspectionAsync(request));
        Assert.True(error is AirframeConcurrencyException or ArgumentOutOfRangeException);
        Assert.Equal(state, await StateAsync(a.Airframe.AirframeId)); Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Theory]
    [InlineData("airframe")]
    [InlineData("condition-revision")]
    [InlineData("service-revision")]
    [InlineData("time")]
    [InlineData("repair")]
    public async Task ConflictingGlobalActionIdFailsClosed(string fault)
    {
        var a = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50))).Application.After;
        var other = await CreateAsync();
        var request = Request(a, await StateAsync(a.Airframe.AirframeId));
        var applied = await Service().PerformRoutineInspectionAsync(request);
        var conflict = fault switch
        {
            "airframe" => request with { AirframeId = other.Airframe.AirframeId },
            "condition-revision" => request with { ExpectedConditionRevision = request.ExpectedConditionRevision + 1 },
            "service-revision" => request with { ExpectedServiceRevision = request.ExpectedServiceRevision + 1 },
            _ => request with { PerformedAt = request.PerformedAt.AddMinutes(1) }
        };
        if (fault == "repair")
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RepairDiscreteDamageAsync(
                new(request.MaintenanceActionId, request.AirframeId, request.ExpectedConditionRevision, request.PerformedAt)));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Service().PerformRoutineInspectionAsync(conflict));
        Assert.Equal(applied.ServiceState, await StateAsync(a.Airframe.AirframeId));
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId)); Assert.Equal(other, await Store().FindAsync(other.Airframe.AirframeId));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task RepairIdCannotBeReusedForInspectionAndConcurrentInspectionCommitsOnce()
    {
        var a = (await FlyAsync(await CreateAsync(AirframeDamageState.Recorded), TimeSpan.FromHours(50))).Application.After;
        var repair = await Service().RepairDiscreteDamageAsync(new(Guid.NewGuid(), a.Airframe.AirframeId, a.Revision, a.SavedAt.AddMinutes(1)));
        var request = Request(repair.Current!, await StateAsync(a.Airframe.AirframeId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().PerformRoutineInspectionAsync(
            request with { MaintenanceActionId = repair.Event!.MaintenanceActionId }));
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => Service().PerformRoutineInspectionAsync(request))));
        Assert.Single(results, r => r.WasNewlyApplied);
        Assert.All(results, r => Assert.Equal(AirframeInspectionResultStatus.Inspected, r.Status));
        Assert.Equal(3, (await StateAsync(a.Airframe.AirframeId)).Revision);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task CrashInspectionThenRepairRetainsBothHistoriesAndBothIndependentGates()
    {
        var flight = await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50), FlightSessionStatus.Interrupted, crash: true);
        var a = flight.Application.After;
        var id = a.Airframe.AirframeId;
        var eligibility = new PhysicalAirframeEligibilityService(Store());
        Assert.Equal(PhysicalAirframeEligibilityStatus.Grounded, (await eligibility.EvaluateAsync(ConsequenceFixture.Model, id)).Status);
        var request = Request(a, await StateAsync(id));
        var inspection = await Service().PerformRoutineInspectionAsync(request);
        Assert.Equal(PhysicalAirframeEligibilityStatus.Grounded, (await eligibility.EvaluateAsync(ConsequenceFixture.Model, id)).Status);
        // Equal performed times deliberately exercise the ordinal action-ID tiebreak in the shared stream.
        Guid repairId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var repair = await Service().RepairDiscreteDamageAsync(new(repairId, id, a.Revision, request.PerformedAt));
        Assert.Equal(inspection.ServiceState, await StateAsync(id));
        Assert.True((await eligibility.EvaluateAsync(ConsequenceFixture.Model, id)).IsEligible);
        ClearPool();
        var page1 = (await History().ReadServiceHistoryAsync(new(id, 1))).Snapshot!;
        var page2 = (await History().ReadServiceHistoryAsync(new(id, 1, page1.History.Next))).Snapshot!;
        var ordered = new AirframeServiceEvent[] { inspection.Event!, repair.Event! }
            .OrderByDescending(e => e.PerformedAt).ThenByDescending(e => e.MaintenanceActionId.ToString("D"), StringComparer.Ordinal).ToArray();
        Assert.Equal(ordered[0], Assert.Single(page1.History.Events)); Assert.Equal(ordered[1], Assert.Single(page2.History.Events));
        Assert.Null(page2.History.Next);
        var history = (await History().ReadAsync(new(id))).Snapshot!;
        Assert.Equal(JsonSerializer.Serialize(flight.Application), JsonSerializer.Serialize(Assert.Single(history.History).Application));
        Assert.Equal(AirframeServiceability.AvailableForDispatch, history.Serviceability);
        Assert.False((await Store().ApplyAsync(flight.Application.Consequence, repair.Current!, request.PerformedAt)).WasNewlyApplied);
        Assert.Equal(inspection.ServiceState, await StateAsync(id));
        Assert.Equal(repair.Current, await Store().FindAsync(id));
    }

    [Fact]
    public async Task RepairDoesNotClearInspectionDueAndNextInspectionRecursAtCumulativeBoundary()
    {
        var a = (await FlyAsync(await CreateAsync(AirframeDamageState.Grounding), TimeSpan.FromHours(51))).Application.After;
        var due = await StateAsync(a.Airframe.AirframeId);
        var repair = await Service().RepairDiscreteDamageAsync(new(Guid.NewGuid(), a.Airframe.AirframeId, a.Revision, a.SavedAt.AddMinutes(1)));
        Assert.Equal(due, await StateAsync(a.Airframe.AirframeId));
        Assert.Equal(PhysicalAirframeEligibilityStatus.InspectionDue,
            (await new PhysicalAirframeEligibilityService(Store()).EvaluateAsync(ConsequenceFixture.Model, a.Airframe.AirframeId)).Status);
        var first = await Service().PerformRoutineInspectionAsync(Request(repair.Current!, due));
        var last = (await FlyAsync(repair.Current!, TimeSpan.FromHours(50))).Application.After;
        var dueAgain = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(AirframeInspectionStatus.InspectionDue, dueAgain.InspectionStatus);
        var second = await Service().PerformRoutineInspectionAsync(Request(last, dueAgain));
        Assert.Equal(TimeSpan.FromHours(151), second.ServiceState!.NextInspectionDueAtTrackedAirborneTime);
        Assert.Equal(TimeSpan.FromHours(101), second.ServiceState.TotalTrackedAirborneTime);
        Assert.Equal(first.ServiceState!.Revision + 2, second.ServiceState.Revision);
    }

    [Theory]
    [InlineData("metadata-airframe")]
    [InlineData("metadata-kind")]
    [InlineData("metadata-time")]
    [InlineData("malformed")]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("wear")]
    [InlineData("damage")]
    [InlineData("service-revision")]
    [InlineData("next-due")]
    [InlineData("schedule")]
    [InlineData("rationale")]
    public async Task CorruptInspectionHistoryFailsClosed(string fault)
    {
        var a = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50))).Application.After;
        var b = await CreateAsync();
        var request = Request(a, await StateAsync(a.Airframe.AirframeId));
        var result = await Service().PerformRoutineInspectionAsync(request);
        string sql = fault switch
        {
            "metadata-airframe" => "UPDATE airframe_maintenance_events SET airframe_id=$other;",
            "metadata-kind" => "PRAGMA ignore_check_constraints=ON; UPDATE airframe_maintenance_events SET event_kind=1;",
            "metadata-time" => "UPDATE airframe_maintenance_events SET performed_at_utc_ticks=1;",
            "malformed" => "UPDATE airframe_maintenance_events SET payload_json='invalid';",
            "missing" => "UPDATE airframe_maintenance_events SET payload_json='{}';",
            "schema" => "PRAGMA ignore_check_constraints=ON; UPDATE airframe_maintenance_events SET payload_schema_version=99;",
            "wear" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.after.condition.wearFraction',0);",
            "damage" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.after.condition.damage',2);",
            "service-revision" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.serviceAfter.revision',99);",
            "next-due" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.serviceAfter.nextInspectionDueAtTrackedAirborneTime','9.00:00:00');",
            "schedule" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.serviceBefore.scheduleVersion',99);",
            _ => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.rationale','engine replaced');"
        };
        await ExecuteAsync(sql, ("$other", b.Airframe.AirframeId.ToString()));
        var error = await Record.ExceptionAsync(() => History().ReadServiceHistoryAsync(new(fault == "metadata-airframe" ? b.Airframe.AirframeId : a.Airframe.AirframeId)));
        Assert.True(error is InvalidDataException or JsonException or NotSupportedException or ArgumentException, error?.ToString());
        Assert.NotNull(await Record.ExceptionAsync(() => Service().PerformRoutineInspectionAsync(request)));
        Assert.Equal(result.ServiceState, await StateAsync(a.Airframe.AirframeId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("schedule")]
    [InlineData("version")]
    [InlineData("negative")]
    [InlineData("last")]
    [InlineData("next")]
    [InlineData("origin")]
    [InlineData("revision")]
    [InlineData("column-type")]
    public async Task CorruptServiceStateNeverMakesPhysicalAircraftEligible(string fault)
    {
        var a = await CreateAsync();
        string mutation = fault switch
        {
            "missing" => "DELETE FROM airframe_service_state;",
            "schedule" => "UPDATE airframe_service_state SET schedule_id='unknown';",
            "version" => "UPDATE airframe_service_state SET schedule_version=99;",
            "negative" => "UPDATE airframe_service_state SET total_airborne_ticks=-1;",
            "last" => "UPDATE airframe_service_state SET last_inspection_airborne_ticks=1;",
            "next" => "UPDATE airframe_service_state SET next_inspection_airborne_ticks=1;",
            "origin" => "UPDATE airframe_service_state SET usage_origin=99;",
            "revision" => "UPDATE airframe_service_state SET revision=0;",
            _ => "UPDATE airframe_service_state SET total_airborne_ticks='invalid';"
        };
        await ExecuteAsync("PRAGMA ignore_check_constraints=ON; " + mutation);
        var eligibility = new PhysicalAirframeEligibilityService(Store());
        if (fault == "missing") Assert.Equal(PhysicalAirframeEligibilityStatus.ServiceStateMissing,
            (await eligibility.EvaluateAsync(ConsequenceFixture.Model, a.Airframe.AirframeId)).Status);
        else Assert.NotNull(await Record.ExceptionAsync(() => eligibility.EvaluateAsync(ConsequenceFixture.Model, a.Airframe.AirframeId)));
        Assert.NotNull(await Record.ExceptionAsync(() => History().ReadAsync(new(a.Airframe.AirframeId))));
        Assert.NotNull(await Record.ExceptionAsync(() => FlyAsync(a, TimeSpan.FromHours(1))));
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public async Task Schema16MigrationPreservesAllRowsAndStartsExplicitBaselineWithoutHistoricalBackfill()
    {
        var a = await CreateAsync(AirframeDamageState.Recorded);
        var flight = await FlyAsync(a, TimeSpan.FromHours(51));
        var repair = await Service().RepairDiscreteDamageAsync(new(Guid.NewGuid(), a.Airframe.AirframeId,
            flight.Application.After.Revision, flight.Application.AppliedAt.AddMinutes(1)));
        await new SqliteFlightSessionCheckpointStore(DatabasePath).SaveAsync(ConsequenceFixture.Session(a.Airframe.AirframeId));
        await ExecuteAsync("""
            INSERT INTO job_contracts VALUES ('inspection-contract',1,1,3,100,'{"preserve":"contract"}');
            INSERT INTO economy_ledger_transactions VALUES ('inspection-ledger','inspection-key',100,'preserve','contract','inspection-contract');
            INSERT INTO economy_ledger_postings VALUES ('inspection-ledger',0,0,500,0,'debit');
            INSERT INTO economy_ledger_postings VALUES ('inspection-ledger',1,1,0,500,'credit');
            INSERT INTO logbook_entries VALUES ('inspection-log','inspection-log-key',1,100,100,0,0,0,'C172','KJFK','KJFK','inspection-contract','preserve','{"preserve":"logbook"}');
            DROP TABLE airframe_service_state;
            ALTER TABLE airframe_maintenance_events RENAME TO events_copy;
            DROP INDEX ix_airframe_maintenance_events_airframe;
            CREATE TABLE airframe_maintenance_events (
                maintenance_action_id TEXT NOT NULL PRIMARY KEY, airframe_id TEXT NOT NULL REFERENCES airframes(airframe_id),
                event_kind INTEGER NOT NULL CHECK(event_kind=1), performed_at_utc_ticks INTEGER NOT NULL,
                payload_schema_version INTEGER NOT NULL CHECK(payload_schema_version=1), payload_json TEXT NOT NULL);
            INSERT INTO airframe_maintenance_events SELECT * FROM events_copy;
            DROP TABLE events_copy;
            CREATE INDEX ix_airframe_maintenance_events_airframe ON airframe_maintenance_events(airframe_id,performed_at_utc_ticks,maintenance_action_id);
            PRAGMA user_version=16;
            """);
        var rows = await ExistingRowsAsync();
        var baseline = await StateAsync(a.Airframe.AirframeId);
        Assert.Equal(17L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(rows, await ExistingRowsAsync());
        Assert.Equal(AirframeUsageOrigin.TrackingFromMigrationBaseline, baseline.UsageOrigin);
        Assert.Equal(TimeSpan.Zero, baseline.TotalTrackedAirborneTime);
        Assert.Equal(TimeSpan.FromHours(50), baseline.NextInspectionDueAtTrackedAirborneTime);
        Assert.Equal(repair.Current!.SavedAt, baseline.UpdatedAt);
        Assert.Equal(1, baseline.Revision);
        Assert.Equal(repair.Event, await Store().FindMaintenanceActionAsync(repair.Event!.MaintenanceActionId));
        Assert.False((await Store().ApplyAsync(flight.Application.Consequence, repair.Current, repair.Current.SavedAt)).WasNewlyApplied);
        Assert.Equal(baseline, await StateAsync(a.Airframe.AirframeId));
        var next = await FlyAsync(repair.Current, TimeSpan.FromHours(50));
        var inspected = await Service().PerformRoutineInspectionAsync(Request(next.Application.After, await StateAsync(a.Airframe.AirframeId)));
        Assert.Equal(AirframeUsageOrigin.TrackingFromMigrationBaseline, inspected.ServiceState!.UsageOrigin);
        Assert.Equal(2, (await History().ReadServiceHistoryAsync(new(a.Airframe.AirframeId))).Snapshot!.History.Events.Count);
    }

    private async Task<string> ExistingRowsAsync()
    {
        await using var connection = await OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name <> 'airframe_service_state' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var rows = new Dictionary<string, List<object[]>>();
        foreach (var table in tables)
        {
            rows[table] = [];
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows[table].Add(values); }
        }
        return JsonSerializer.Serialize(rows);
    }

    [Fact]
    public async Task ReadSourceRejectsServiceOnlyRaceAndWrongPhysicalIdentity()
    {
        var a = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50))).Application.After;
        var store = Store();
        var state = await StateAsync(a.Airframe.AirframeId);
        var request = Request(a, state);
        var probe = new ServiceReadProbe(store, async () => await Service().PerformRoutineInspectionAsync(request));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() =>
            new AirframeMaintenanceHistorySource(store, store, store, probe).ReadAsync(new(a.Airframe.AirframeId)));
        Assert.Equal(a, await store.FindAsync(a.Airframe.AirframeId));
        Assert.Equal(AirframeInspectionStatus.Current, (await History().ReadAsync(new(a.Airframe.AirframeId))).Snapshot!.InspectionStatus);
        var wrong = new ServiceReadProbe(store, returnedId: new(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PhysicalAirframeEligibilityService(store, wrong).EvaluateAsync(ConsequenceFixture.Model, a.Airframe.AirframeId));
    }

    [Fact]
    public async Task UsageOverflowAndBackdatedSaveRollBackAllConsequenceEffects()
    {
        var a = await CreateAsync();
        await ExecuteAsync("UPDATE airframe_service_state SET total_airborne_ticks=$ticks;", ("$ticks", long.MaxValue));
        var prior = await StateAsync(a.Airframe.AirframeId);
        await Assert.ThrowsAsync<OverflowException>(() => FlyAsync(a, TimeSpan.FromTicks(1)));
        Assert.Equal(prior, await StateAsync(a.Airframe.AirframeId));
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
        await ExecuteAsync("UPDATE airframe_service_state SET total_airborne_ticks=0, updated_at_utc_ticks=$time;",
            ("$time", Epoch.AddDays(10).UtcTicks));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => FlyAsync(a, TimeSpan.FromHours(1)));
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public async Task RoutineInspectionHistoryQueryCannotLeakSameModelAirframes()
    {
        var a = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(50))).Application.After;
        var b = (await FlyAsync(await CreateAsync(), TimeSpan.FromHours(51))).Application.After;
        var first = await Service().PerformRoutineInspectionAsync(Request(a, await StateAsync(a.Airframe.AirframeId)));
        var second = await Service().PerformRoutineInspectionAsync(Request(b, await StateAsync(b.Airframe.AirframeId)));
        ClearPool();
        Assert.Equal(first.Event, Assert.Single((await History().ReadServiceHistoryAsync(new(a.Airframe.AirframeId))).Snapshot!.History.Events));
        Assert.Equal(second.Event, Assert.Single((await History().ReadServiceHistoryAsync(new(b.Airframe.AirframeId))).Snapshot!.History.Events));
        Assert.Equal(first.ServiceState, await StateAsync(a.Airframe.AirframeId));
    }

    [Fact]
    public void ProductionRegistersServiceStateOnTheExistingPhysicalStore()
    {
        var composition = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<IAirframeServiceStateStore>(provider =>", composition);
        Assert.Contains("(SqliteAirframeStore)provider.GetRequiredService<IAirframeStore>()", composition);
    }

    private sealed class ServiceReadProbe(IAirframeServiceStateStore inner, Func<Task>? secondRead = null, AirframeId? returnedId = null) : IAirframeServiceStateStore
    {
        private int _reads;
        public async Task<AirframeServiceState?> ReadServiceStateAsync(AirframeId id, CancellationToken cancellationToken = default)
        {
            if (++_reads == 2 && secondRead is not null) await secondRead();
            var value = await inner.ReadServiceStateAsync(id, cancellationToken);
            return returnedId is { } wrong && value is not null ? value with { AirframeId = wrong } : value;
        }
    }
}
