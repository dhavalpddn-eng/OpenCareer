using Microsoft.Data.Sqlite;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed partial class AirframeInspectionTests
{
    private async Task<AirframeComponentInspectionRequest> ComponentInspectionRequestAsync(
        AirframeStoreRecord condition,
        AirframeMaintenanceScheduleEvidenceRecord scheduleState,
        Guid? actionId = null)
    {
        AirframeServiceState service = await StateAsync(condition.Airframe.AirframeId);
        DateTimeOffset performedAt = new[]
        {
            condition.SavedAt,
            service.UpdatedAt,
            scheduleState.Baseline.EstablishedAt
        }.Max().AddMinutes(1);
        return new(
            actionId ?? Guid.NewGuid(),
            scheduleState.EvidenceId,
            condition.Airframe.AirframeId,
            condition.Airframe.CanonicalAircraftId,
            scheduleState.Applicability.CertifiedAircraftModel,
            scheduleState.Applicability.InstalledComponentModel,
            scheduleState.Applicability.Component,
            scheduleState.Baseline.ScheduleId,
            scheduleState.Baseline.ScheduleVersion,
            scheduleState.Applicability.Revision,
            scheduleState.Baseline.Revision,
            condition.Revision,
            service.Revision,
            AirframeMaintenanceBaselineAuthority.AuthoritativeMaintenanceRecord,
            "authoritative-component-service-record:test-fixture/v1",
            performedAt);
    }

    [Fact]
    public async Task VerifiedComponentInspectionAdvancesOnlyMatchingAppendOnlyBaseline()
    {
        AirframeStoreRecord created = await CreateAsync(AirframeDamageState.Recorded, wear: 0.75);
        AirframeMaintenanceScheduleEvidenceRequest registration = await ScheduleEvidenceRequestAsync(created);
        AirframeMaintenanceScheduleEvidenceRecord initial = Assert.IsType<AirframeMaintenanceScheduleEvidenceRecord>(
            (await Store().RegisterAsync(registration)).Record);
        string immutablePayload = Assert.IsType<string>(await ScalarAsync(
            "SELECT payload_json FROM airframe_maintenance_schedule_evidence WHERE evidence_id='" +
            initial.EvidenceId.ToString("D") + "';"));

        FlightAirframeApplyResult flight = await FlyAsync(
            created,
            TimeSpan.FromHours(50),
            verticalSpeeds: [-500]);
        AirframeStoreRecord condition = flight.Application.After;
        AirframeServiceState serviceBefore = await StateAsync(created.Airframe.AirframeId);
        AirframeMaintenanceScheduleEvidenceRecord scheduleBefore = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest request = await ComponentInspectionRequestAsync(
            condition,
            scheduleBefore);

        AirframeComponentInspectionResult result =
            await Service().PerformVerifiedComponentInspectionAsync(request);

        Assert.Equal(AirframeComponentInspectionResultStatus.Inspected, result.Status);
        Assert.True(result.WasNewlyApplied);
        Assert.Equal(condition, result.Condition);
        Assert.Equal(serviceBefore, result.ServiceState);
        Assert.Equal(condition, await Store().FindAsync(created.Airframe.AirframeId));
        Assert.Equal(serviceBefore, await StateAsync(created.Airframe.AirframeId));
        AirframeMaintenanceScheduleEvidenceRecord scheduleAfter = Assert.IsType<AirframeMaintenanceScheduleEvidenceRecord>(
            result.ScheduleState);
        Assert.Equal(scheduleBefore.EvidenceId, scheduleAfter.EvidenceId);
        Assert.Equal(scheduleBefore.Applicability, scheduleAfter.Applicability);
        Assert.Equal(scheduleBefore.Baseline.Revision + 1, scheduleAfter.Baseline.Revision);
        Assert.Equal(serviceBefore.TotalTrackedAirborneTime, scheduleAfter.Baseline.TrackedAirborneTime);
        Assert.Equal(serviceBefore.TotalTrackedLandingCycles, scheduleAfter.Baseline.TrackedLandingCycles);
        Assert.Equal(serviceBefore.UsageOrigin, scheduleAfter.Baseline.AirborneTimeOrigin);
        Assert.Equal(serviceBefore.LandingCycleOrigin, scheduleAfter.Baseline.LandingCycleOrigin);
        Assert.Equal(serviceBefore.Revision, scheduleAfter.Baseline.SourceServiceRevision);
        Assert.Equal(serviceBefore.UpdatedAt, scheduleAfter.Baseline.SourceServiceUpdatedAt);
        Assert.Equal(request.ServiceRecordReference, scheduleAfter.Baseline.SourceReference);
        Assert.Equal(request.PerformedAt, scheduleAfter.Baseline.EstablishedAt);

        AirframeComponentInspectionEvent retained = Assert.IsType<AirframeComponentInspectionEvent>(result.Event);
        Assert.Equal(scheduleBefore, retained.ScheduleBefore);
        Assert.Equal(scheduleAfter, retained.ScheduleAfter);
        Assert.Equal(condition, retained.Before);
        Assert.Equal(condition, retained.After);
        Assert.Equal(serviceBefore, retained.AuthoritativeServiceState);
        retained.Validate();
        Assert.Equal(2L, await ScalarAsync(
            "SELECT count(*) FROM airframe_component_service_baselines WHERE evidence_id='" +
            initial.EvidenceId.ToString("D") + "';"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.Equal(4L, await ScalarAsync(
            "SELECT payload_schema_version FROM airframe_maintenance_events WHERE maintenance_action_id='" +
            request.MaintenanceActionId.ToString("D") + "';"));
        Assert.Equal(immutablePayload, await ScalarAsync(
            "SELECT payload_json FROM airframe_maintenance_schedule_evidence WHERE evidence_id='" +
            initial.EvidenceId.ToString("D") + "';"));

        AirframeMaintenanceScheduleEvidenceResult registrationReplay = await Store().RegisterAsync(registration);
        Assert.False(registrationReplay.WasNewlyApplied);
        Assert.Equal(initial, registrationReplay.Record);
        AirframeMaintenanceSnapshot snapshot = Assert.IsType<AirframeMaintenanceSnapshot>(
            (await History().ReadAsync(new(created.Airframe.AirframeId))).Snapshot);
        AirframeMaintenanceScheduleAssessment assessment = Assert.Single(snapshot.VerifiedScheduleCandidates);
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyCurrent, assessment.Status);
        Assert.Equal(TimeSpan.Zero, assessment.TrackedAirborneTimeSinceBaseline);
        Assert.Equal(0L, assessment.TrackedLandingCyclesSinceBaseline);
        Assert.Equal(AirframeInspectionStatus.InspectionDue, snapshot.InspectionStatus);

        ClearPool();
        AirframeComponentInspectionResult replay =
            await Service().PerformVerifiedComponentInspectionAsync(request);
        Assert.False(replay.WasNewlyApplied);
        Assert.Equal(retained, replay.Event);
        Assert.Equal(scheduleAfter, replay.ScheduleState);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().PerformVerifiedComponentInspectionAsync(
                request with { ServiceRecordReference = "authoritative-component-service-record:conflict/v1" }));
        Assert.Equal(2L, await ScalarAsync(
            "SELECT count(*) FROM airframe_component_service_baselines WHERE evidence_id='" +
            initial.EvidenceId.ToString("D") + "';"));
        Assert.Equal(retained, Assert.Single(
            (await History().ReadServiceHistoryAsync(new(created.Airframe.AirframeId))).Snapshot!
                .History.Events));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().PerformRoutineInspectionAsync(new(
                request.MaintenanceActionId,
                created.Airframe.AirframeId,
                condition.Revision,
                serviceBefore.Revision,
                request.PerformedAt.AddMinutes(1))));
    }

    [Fact]
    public async Task ComponentInspectionRequiresExactPersistedIdentityAndDoesNotReserveNotDueAction()
    {
        AirframeStoreRecord airframe = await CreateAsync();
        AirframeStoreRecord sibling = await CreateAsync();
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(airframe));
        AirframeMaintenanceScheduleEvidenceRecord schedule = Assert.Single(
            await Store().ReadForAirframeAsync(airframe.Airframe.AirframeId));
        Guid reusableAction = Guid.NewGuid();
        AirframeComponentInspectionRequest exact = await ComponentInspectionRequestAsync(
            airframe,
            schedule,
            reusableAction);

        AirframeComponentInspectionResult notDue =
            await Service().PerformVerifiedComponentInspectionAsync(exact);
        Assert.Equal(AirframeComponentInspectionResultStatus.InspectionNotDue, notDue.Status);
        Assert.Null(notDue.Event);
        Assert.Null(await Store().FindMaintenanceActionAsync(reusableAction));
        Assert.Equal(
            AirframeComponentInspectionResultStatus.ScheduleEvidenceNotFound,
            (await Service().PerformVerifiedComponentInspectionAsync(
                exact with { MaintenanceActionId = Guid.NewGuid(), EvidenceId = Guid.NewGuid() })).Status);

        AirframeComponentInspectionRequest[] mismatches =
        [
            exact with
            {
                MaintenanceActionId = Guid.NewGuid(),
                AirframeId = sibling.Airframe.AirframeId,
                ExpectedConditionRevision = sibling.Revision,
                ExpectedServiceRevision = (await StateAsync(sibling.Airframe.AirframeId)).Revision
            },
            exact with { MaintenanceActionId = Guid.NewGuid(), CanonicalAircraftId = "msfs-title:Other" },
            exact with { MaintenanceActionId = Guid.NewGuid(), CertifiedAircraftModel = "172R" },
            exact with { MaintenanceActionId = Guid.NewGuid(), InstalledComponentModel = "O-300" },
            exact with { MaintenanceActionId = Guid.NewGuid(), Component = (AirframeMaintenanceComponent)99 },
            exact with { MaintenanceActionId = Guid.NewGuid(), ScheduleId = "Other.Schedule" },
            exact with { MaintenanceActionId = Guid.NewGuid(), ScheduleVersion = 99 },
            exact with { MaintenanceActionId = Guid.NewGuid(), ExpectedApplicabilityRevision = 99 },
            exact with { MaintenanceActionId = Guid.NewGuid(), ExpectedBaselineRevision = 99 },
            exact with { MaintenanceActionId = Guid.NewGuid(), ExpectedConditionRevision = 99 },
            exact with { MaintenanceActionId = Guid.NewGuid(), ExpectedServiceRevision = 99 },
            exact with { MaintenanceActionId = Guid.NewGuid(), Authority = (AirframeMaintenanceBaselineAuthority)99 }
        ];
        foreach (AirframeComponentInspectionRequest mismatch in mismatches)
        {
            Exception? error = await Record.ExceptionAsync(
                () => Service().PerformVerifiedComponentInspectionAsync(mismatch));
            Assert.NotNull(error);
        }
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_component_service_baselines;"));

        FlightAirframeApplyResult dueFlight = await FlyAsync(
            airframe,
            TimeSpan.FromHours(50),
            verticalSpeeds: [-500]);
        AirframeComponentInspectionRequest dueRequest = await ComponentInspectionRequestAsync(
            dueFlight.Application.After,
            schedule,
            reusableAction);
        Assert.True((await Service().PerformVerifiedComponentInspectionAsync(dueRequest)).WasNewlyApplied);
    }

    [Fact]
    public async Task FutureComponentInspectionCannotAdvanceOrReserveActionIdentity()
    {
        AirframeStoreRecord created = await CreateAsync();
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(created));
        FlightAirframeApplyResult flight = await FlyAsync(
            created,
            TimeSpan.FromHours(50),
            verticalSpeeds: [-500]);
        AirframeMaintenanceScheduleEvidenceRecord schedule = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest request = (await ComponentInspectionRequestAsync(
            flight.Application.After,
            schedule)) with { PerformedAt = Epoch.AddDays(31) };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service().PerformVerifiedComponentInspectionAsync(request));

        Assert.Null(await Store().FindMaintenanceActionAsync(request.MaintenanceActionId));
        Assert.Equal(schedule, Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId)));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM airframe_component_service_baselines;"));
    }

    [Fact]
    public async Task ComponentServiceBaselineFencesStalePhysicalStateMutations()
    {
        AirframeStoreRecord created = await CreateAsync(AirframeDamageState.Recorded);
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(created));
        FlightAirframeApplyResult firstFlight = await FlyAsync(
            created,
            TimeSpan.FromHours(50),
            verticalSpeeds: [-500]);
        AirframeStoreRecord condition = firstFlight.Application.After;
        AirframeServiceState service = await StateAsync(created.Airframe.AirframeId);
        AirframeMaintenanceScheduleEvidenceRecord schedule = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest componentRequest = await ComponentInspectionRequestAsync(
            condition,
            schedule);
        DateTimeOffset staleTime = service.UpdatedAt.AddSeconds(30);
        var routineRequest = new AirframeInspectionRequest(
            Guid.NewGuid(),
            created.Airframe.AirframeId,
            condition.Revision,
            service.Revision,
            staleTime);
        var repairRequest = new AirframeRepairRequest(
            Guid.NewGuid(),
            created.Airframe.AirframeId,
            condition.Revision,
            staleTime);
        FlightSession staleSession = ConsequenceFixture.Session(
            created.Airframe.AirframeId,
            verticalSpeeds: [-500]);
        staleSession = staleSession with
        {
            UpdatedAt = staleTime,
            TimeLedger = staleSession.TimeLedger with
            {
                AirborneTime = TimeSpan.FromHours(1),
                BlockTime = TimeSpan.FromHours(1)
            }
        };
        FlightAirframeConsequence staleConsequence =
            FlightAirframeConsequenceCalculator.Calculate(staleSession);

        Assert.True((await Service().PerformVerifiedComponentInspectionAsync(componentRequest))
            .WasNewlyApplied);

        await Assert.ThrowsAsync<AirframeConcurrencyException>(
            () => Service().PerformRoutineInspectionAsync(routineRequest));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(
            () => Service().RepairDiscreteDamageAsync(repairRequest));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(
            () => Store().UpdateConditionAsync(
                condition.Airframe,
                condition.Condition,
                condition.Revision,
                staleTime));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(
            () => Store().ApplyAsync(staleConsequence, condition, staleTime));

        Assert.Equal(condition, await Store().FindAsync(created.Airframe.AirframeId));
        Assert.Equal(service, await StateAsync(created.Airframe.AirframeId));
        Assert.Null(await Store().FindMaintenanceActionAsync(routineRequest.MaintenanceActionId));
        Assert.Null(await Store().FindMaintenanceActionAsync(repairRequest.MaintenanceActionId));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));

        FlightAirframeApplyResult laterFlight = await FlyAsync(
            condition,
            TimeSpan.FromHours(1),
            verticalSpeeds: [-500]);
        Assert.True(laterFlight.WasNewlyApplied);
        Assert.False((await Service().PerformVerifiedComponentInspectionAsync(componentRequest))
            .WasNewlyApplied);
    }

    [Theory]
    [InlineData("BEFORE INSERT ON airframe_component_service_baselines")]
    [InlineData("BEFORE INSERT ON airframe_maintenance_events")]
    [InlineData("AFTER INSERT ON airframe_maintenance_events")]
    public async Task ComponentInspectionFailureRollsBackBaselineAndHistoryAndRetries(string point)
    {
        AirframeStoreRecord created = await CreateAsync();
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(created));
        FlightAirframeApplyResult flight = await FlyAsync(created, TimeSpan.FromHours(50), verticalSpeeds: [-500]);
        AirframeMaintenanceScheduleEvidenceRecord schedule = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest request = await ComponentInspectionRequestAsync(
            flight.Application.After,
            schedule);
        AirframeServiceState service = await StateAsync(created.Airframe.AirframeId);
        await ExecuteAsync($"CREATE TRIGGER fail_component_service {point} BEGIN SELECT RAISE(ABORT, 'injected'); END;");

        await Assert.ThrowsAsync<SqliteException>(
            () => Service().PerformVerifiedComponentInspectionAsync(request));

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_component_service_baselines;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.Equal(schedule, Assert.Single(await Store().ReadForAirframeAsync(created.Airframe.AirframeId)));
        Assert.Equal(service, await StateAsync(created.Airframe.AirframeId));
        Assert.Null(await Store().FindMaintenanceActionAsync(request.MaintenanceActionId));
        await ExecuteAsync("DROP TRIGGER fail_component_service;");

        Assert.True((await Service().PerformVerifiedComponentInspectionAsync(request)).WasNewlyApplied);
        Assert.False((await Service().PerformVerifiedComponentInspectionAsync(request)).WasNewlyApplied);
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM airframe_component_service_baselines;"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task ConcurrentIdenticalComponentInspectionCommitsExactlyOnce()
    {
        AirframeStoreRecord created = await CreateAsync();
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(created));
        FlightAirframeApplyResult flight = await FlyAsync(created, TimeSpan.FromHours(50), verticalSpeeds: [-500]);
        AirframeMaintenanceScheduleEvidenceRecord schedule = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest request = await ComponentInspectionRequestAsync(
            flight.Application.After,
            schedule);
        const int callerCount = 3;
        using var ready = new CountdownEvent(callerCount);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AirframeComponentInspectionResult>[] calls = Enumerable.Range(0, callerCount)
            .Select(_ => Task.Run(async () =>
            {
                AirframeMaintenanceService service = Service();
                ready.Signal();
                await start.Task;
                return await service.PerformVerifiedComponentInspectionAsync(request);
            }))
            .ToArray();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        start.SetResult();

        AirframeComponentInspectionResult[] results = await Task.WhenAll(calls);

        Assert.Single(results, result => result.WasNewlyApplied);
        Assert.All(results, result =>
        {
            Assert.Equal(AirframeComponentInspectionResultStatus.Inspected, result.Status);
            Assert.Equal(request.MaintenanceActionId, result.Event!.MaintenanceActionId);
        });
        Assert.All(results, result => Assert.Equal(results[0].ScheduleState, result.ScheduleState));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM airframe_component_service_baselines;"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task CorruptComponentBaselineOrEventFailsClosed()
    {
        AirframeStoreRecord created = await CreateAsync();
        AirframeMaintenanceScheduleEvidenceRequest registration = await ScheduleEvidenceRequestAsync(created);
        await Store().RegisterAsync(registration);
        FlightAirframeApplyResult flight = await FlyAsync(created, TimeSpan.FromHours(50), verticalSpeeds: [-500]);
        AirframeMaintenanceScheduleEvidenceRecord schedule = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest request = await ComponentInspectionRequestAsync(
            flight.Application.After,
            schedule);
        await Service().PerformVerifiedComponentInspectionAsync(request);
        await ExecuteAsync("""
            UPDATE airframe_component_service_baselines
            SET payload_json=json_set(payload_json,'$.baseline.trackedLandingCycles',99)
            WHERE baseline_revision=2;
            """);

        Assert.NotNull(await Record.ExceptionAsync(
            () => Store().ReadForAirframeAsync(created.Airframe.AirframeId)));
        Assert.NotNull(await Record.ExceptionAsync(
            () => History().ReadServiceHistoryAsync(new(created.Airframe.AirframeId))));
        Assert.NotNull(await Record.ExceptionAsync(
            () => Service().PerformVerifiedComponentInspectionAsync(request)));
        Assert.NotNull(await Record.ExceptionAsync(() => Store().RegisterAsync(registration)));
    }

    [Fact]
    public async Task ForgedConditionOrOrphanComponentHistoryFailsClosed()
    {
        AirframeStoreRecord created = await CreateAsync();
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(created));
        FlightAirframeApplyResult flight = await FlyAsync(
            created,
            TimeSpan.FromHours(50),
            verticalSpeeds: [-500]);
        AirframeMaintenanceScheduleEvidenceRecord schedule = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest request = await ComponentInspectionRequestAsync(
            flight.Application.After,
            schedule);
        await Service().PerformVerifiedComponentInspectionAsync(request);
        await ExecuteAsync("""
            UPDATE airframe_maintenance_events
            SET payload_json=json_set(
                payload_json,
                '$.before.condition.wearFraction', 0.5,
                '$.after.condition.wearFraction', 0.5)
            WHERE maintenance_action_id=$action;
            """, ("$action", request.MaintenanceActionId.ToString("D")));

        Assert.NotNull(await Record.ExceptionAsync(
            () => Service().PerformVerifiedComponentInspectionAsync(request)));
        Assert.NotNull(await Record.ExceptionAsync(
            () => Store().ReadServiceHistoryAsync(new(created.Airframe.AirframeId))));

        await ExecuteAsync("""
            UPDATE airframe_maintenance_events
            SET payload_json=json_set(
                payload_json,
                '$.before.condition.wearFraction', $wear,
                '$.after.condition.wearFraction', $wear)
            WHERE maintenance_action_id=$action;
            """,
            ("$wear", flight.Application.After.Condition.WearFraction),
            ("$action", request.MaintenanceActionId.ToString("D")));
        AirframeServiceState authoritativeService = await StateAsync(created.Airframe.AirframeId);
        await ExecuteAsync("""
            UPDATE airframe_maintenance_events
            SET payload_json=json_set(
                payload_json,
                '$.authoritativeServiceState.lastInspectionAtTrackedAirborneTime', '01:00:00',
                '$.authoritativeServiceState.nextInspectionDueAtTrackedAirborneTime', '2.03:00:00')
            WHERE maintenance_action_id=$action;
            """, ("$action", request.MaintenanceActionId.ToString("D")));
        Assert.NotNull(await Record.ExceptionAsync(
            () => Service().PerformVerifiedComponentInspectionAsync(request)));
        await ExecuteAsync("""
            UPDATE airframe_maintenance_events
            SET payload_json=json_set(
                payload_json,
                '$.authoritativeServiceState.lastInspectionAtTrackedAirborneTime', $last,
                '$.authoritativeServiceState.nextInspectionDueAtTrackedAirborneTime', $next)
            WHERE maintenance_action_id=$action;
            """,
            ("$last", authoritativeService.LastInspectionAtTrackedAirborneTime.ToString()),
            ("$next", authoritativeService.NextInspectionDueAtTrackedAirborneTime.ToString()),
            ("$action", request.MaintenanceActionId.ToString("D")));
        Guid orphanActionId = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT INTO airframe_maintenance_events (
                maintenance_action_id, airframe_id, event_kind,
                performed_at_utc_ticks, payload_schema_version, payload_json)
            SELECT $orphan, airframe_id, event_kind,
                performed_at_utc_ticks, payload_schema_version,
                json_set(payload_json, '$.request.maintenanceActionId', $orphan)
            FROM airframe_maintenance_events
            WHERE maintenance_action_id=$action;
            """,
            ("$orphan", orphanActionId.ToString("D")),
            ("$action", request.MaintenanceActionId.ToString("D")));

        Assert.NotNull(await Record.ExceptionAsync(
            () => Store().ReadServiceHistoryAsync(new(created.Airframe.AirframeId))));
        Assert.NotNull(await Record.ExceptionAsync(
            () => Store().FindMaintenanceActionAsync(orphanActionId)));
    }

    [Fact]
    public async Task TruncatedComponentBaselineTailCannotBeReplaced()
    {
        AirframeStoreRecord created = await CreateAsync();
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(created));
        FlightAirframeApplyResult flight = await FlyAsync(
            created,
            TimeSpan.FromHours(50),
            verticalSpeeds: [-500]);
        AirframeMaintenanceScheduleEvidenceRecord scheduleBefore = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        AirframeComponentInspectionRequest first = await ComponentInspectionRequestAsync(
            flight.Application.After,
            scheduleBefore);
        await Service().PerformVerifiedComponentInspectionAsync(first);
        await ExecuteAsync(
            "DELETE FROM airframe_component_service_baselines WHERE baseline_revision=2;");
        AirframeComponentInspectionRequest replacement = first with
        {
            MaintenanceActionId = Guid.NewGuid(),
            PerformedAt = first.PerformedAt.AddMinutes(1)
        };

        Assert.NotNull(await Record.ExceptionAsync(
            () => Service().PerformVerifiedComponentInspectionAsync(replacement)));

        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM airframe_component_service_baselines;"));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.NotNull(await Record.ExceptionAsync(
            () => Store().FindMaintenanceActionAsync(first.MaintenanceActionId)));
        Assert.Null(await Store().FindMaintenanceActionAsync(replacement.MaintenanceActionId));
    }

    [Fact]
    public async Task Schema19MigrationCopiesRetainedBaselineWithoutRecapturingLaterUsage()
    {
        AirframeStoreRecord created = await CreateAsync();
        AirframeMaintenanceScheduleEvidenceRequest registration = await ScheduleEvidenceRequestAsync(created);
        AirframeMaintenanceScheduleEvidenceRecord initial = Assert.IsType<AirframeMaintenanceScheduleEvidenceRecord>(
            (await Store().RegisterAsync(registration)).Record);
        string immutablePayload = Assert.IsType<string>(await ScalarAsync(
            "SELECT payload_json FROM airframe_maintenance_schedule_evidence WHERE evidence_id='" +
            initial.EvidenceId.ToString("D") + "';"));
        await FlyAsync(created, TimeSpan.FromHours(50), verticalSpeeds: [-500]);
        AirframeServiceState laterUsage = await StateAsync(created.Airframe.AirframeId);
        Assert.True(laterUsage.TotalTrackedAirborneTime > initial.Baseline.TrackedAirborneTime);
        await ExecuteAsync("""
            DROP TABLE airframe_component_service_baselines;
            PRAGMA user_version=19;
            """);
        ClearPool();

        AirframeMaintenanceScheduleEvidenceRecord migrated = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));

        Assert.Equal(20L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(initial, migrated);
        Assert.Equal(immutablePayload, await ScalarAsync(
            "SELECT payload_json FROM airframe_component_service_baselines WHERE baseline_revision=1;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyDue,
            Assert.Single((await History().ReadAsync(new(created.Airframe.AirframeId))).Snapshot!
                .VerifiedScheduleCandidates).Status);
    }
}
