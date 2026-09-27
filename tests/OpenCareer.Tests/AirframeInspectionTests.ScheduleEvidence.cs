using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed partial class AirframeInspectionTests
{
    private async Task<AirframeMaintenanceScheduleEvidenceRequest> ScheduleEvidenceRequestAsync(
        AirframeStoreRecord airframe,
        string certifiedModel = "172S",
        string componentModel = "IO-360-L2A",
        Guid? evidenceId = null)
    {
        AirframeServiceState service = await StateAsync(airframe.Airframe.AirframeId);
        DateTimeOffset recordedAt = (airframe.SavedAt > service.UpdatedAt
            ? airframe.SavedAt
            : service.UpdatedAt).AddMinutes(1);
        return new(
            evidenceId ?? Guid.NewGuid(),
            airframe.Airframe.AirframeId,
            AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleId,
            AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleVersion,
            AirframeMaintenanceComponent.Engine,
            certifiedModel,
            componentModel,
            AirframeMaintenanceApplicabilityAuthority.AuthoritativePhysicalAircraftRecord,
            "authoritative-airframe-record:test-fixture/v1",
            AirframeMaintenanceBaselineAuthority.AuthoritativeMaintenanceRecord,
            "authoritative-maintenance-record:test-fixture/v1",
            service.Revision,
            recordedAt);
    }

    [Fact]
    public async Task ExactPhysicalEvidenceCapturesBaselineAndActivatesScheduleAcrossRestart()
    {
        FlightAirframeApplyResult initialFlight = await FlyAsync(
            await CreateAsync(), TimeSpan.FromHours(2), verticalSpeeds: [-500]);
        AirframeStoreRecord current = initialFlight.Application.After;
        AirframeServiceState baselineState = await StateAsync(current.Airframe.AirframeId);
        AirframeMaintenanceScheduleEvidenceRequest request = await ScheduleEvidenceRequestAsync(current);

        AirframeMaintenanceScheduleEvidenceResult applied = await Store().RegisterAsync(request);

        Assert.Equal(AirframeMaintenanceScheduleEvidenceStatus.Registered, applied.Status);
        Assert.True(applied.WasNewlyApplied);
        AirframeMaintenanceScheduleEvidenceRecord record = Assert.IsType<AirframeMaintenanceScheduleEvidenceRecord>(applied.Record);
        Assert.Equal(current.Airframe.AirframeId, record.Applicability.AirframeId);
        Assert.Equal(current.Airframe.CanonicalAircraftId, record.Applicability.CanonicalAircraftId);
        Assert.Equal("172S", record.Applicability.CertifiedAircraftModel);
        Assert.Equal("IO-360-L2A", record.Applicability.InstalledComponentModel);
        Assert.Equal(AirframeMaintenanceComponent.Engine, record.Applicability.Component);
        Assert.Equal(AirframeMaintenanceApplicabilityAuthority.AuthoritativePhysicalAircraftRecord,
            record.Applicability.Authority);
        Assert.Equal(request.SourceReference, record.Applicability.SourceReference);
        Assert.Equal(current.Airframe.AirframeId, record.Baseline.AirframeId);
        Assert.Equal(AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleId, record.Baseline.ScheduleId);
        Assert.Equal(AirframeMaintenanceScheduleCatalog.SkyhawkIo360ScheduleVersion, record.Baseline.ScheduleVersion);
        Assert.Equal(AirframeMaintenanceComponent.Engine, record.Baseline.Component);
        Assert.Equal(baselineState.TotalTrackedAirborneTime, record.Baseline.TrackedAirborneTime);
        Assert.Equal(baselineState.TotalTrackedLandingCycles, record.Baseline.TrackedLandingCycles);
        Assert.Equal(baselineState.Revision, record.Baseline.SourceServiceRevision);
        Assert.Equal(baselineState.UpdatedAt, record.Baseline.SourceServiceUpdatedAt);
        Assert.Equal(baselineState.UsageOrigin, record.Baseline.AirborneTimeOrigin);
        Assert.Equal(baselineState.LandingCycleOrigin, record.Baseline.LandingCycleOrigin);
        Assert.Equal(AirframeMaintenanceBaselineAuthority.AuthoritativeMaintenanceRecord,
            record.Baseline.Authority);
        Assert.Equal("authoritative-maintenance-record:test-fixture/v1", record.Baseline.SourceReference);
        Assert.Equal(1, record.Applicability.Revision);
        Assert.Equal(1, record.Baseline.Revision);
        Assert.Equal(AirframeMaintenanceScheduleEvidenceRecord.CurrentPayloadSchemaVersion,
            record.PayloadSchemaVersion);

        AirframeMaintenanceSnapshot initial = Assert.IsType<AirframeMaintenanceSnapshot>(
            (await History().ReadAsync(new(current.Airframe.AirframeId))).Snapshot);
        AirframeMaintenanceScheduleAssessment initialAssessment = Assert.Single(initial.VerifiedScheduleCandidates);
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyCurrent, initialAssessment.Status);
        Assert.Equal(TimeSpan.Zero, initialAssessment.TrackedAirborneTimeSinceBaseline!.Value);
        Assert.Equal(0L, initialAssessment.TrackedLandingCyclesSinceBaseline!.Value);
        Assert.Equal(LightAircraftRoutineInspectionV1.ScheduleId, initial.ScheduleId);

        ClearPool();
        Assert.Equal(record, Assert.Single(await Store().ReadForAirframeAsync(current.Airframe.AirframeId)));
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyCurrent,
            Assert.Single((await History().ReadAsync(new(current.Airframe.AirframeId))).Snapshot!
                .VerifiedScheduleCandidates).Status);

        FlightAirframeApplyResult almostDue = await FlyAsync(current, TimeSpan.FromHours(50) - TimeSpan.FromTicks(1),
            verticalSpeeds: [-600]);
        AirframeMaintenanceScheduleAssessment beforeBoundary = Assert.Single(
            (await History().ReadAsync(new(current.Airframe.AirframeId))).Snapshot!.VerifiedScheduleCandidates);
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyCurrent, beforeBoundary.Status);
        Assert.Equal(TimeSpan.FromTicks(1), beforeBoundary.TrackedAirborneTimeUntilDue!.Value);

        await FlyAsync(almostDue.Application.After, TimeSpan.FromTicks(1), verticalSpeeds: [-700]);
        AirframeMaintenanceScheduleAssessment boundary = Assert.Single(
            (await History().ReadAsync(new(current.Airframe.AirframeId))).Snapshot!.VerifiedScheduleCandidates);
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyDue, boundary.Status);
        Assert.Equal(AirframeMaintenanceDueReason.TrackedAirborneTime, boundary.DueReasons);
        Assert.Equal(TimeSpan.Zero, boundary.TrackedAirborneTimeUntilDue!.Value);
    }

    [Fact]
    public async Task EvidenceRegistrationIsCreateOnlyIdempotentAndConflictSafe()
    {
        AirframeStoreRecord airframe = await CreateAsync();
        AirframeServiceState service = await StateAsync(airframe.Airframe.AirframeId);
        AirframeMaintenanceScheduleEvidenceRequest request = await ScheduleEvidenceRequestAsync(airframe);

        AirframeMaintenanceScheduleEvidenceResult first = await Store().RegisterAsync(request);
        ClearPool();
        AirframeMaintenanceScheduleEvidenceResult replay = await Store().RegisterAsync(request);

        Assert.True(first.WasNewlyApplied);
        Assert.False(replay.WasNewlyApplied);
        Assert.Equal(first.Record, replay.Record);
        Assert.Equal(service, await StateAsync(airframe.Airframe.AirframeId));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().RegisterAsync(
            request with { SourceReference = "authoritative-airframe-record:different/v1" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().RegisterAsync(
            request with { EvidenceId = Guid.NewGuid() }));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));
        Assert.Equal(service, await StateAsync(airframe.Airframe.AirframeId));
    }

    [Fact]
    public async Task RetainedEvidenceReplaySurvivesAClockCorrection()
    {
        AirframeStoreRecord airframe = await CreateAsync();
        AirframeMaintenanceScheduleEvidenceRequest request = await ScheduleEvidenceRequestAsync(airframe);
        Assert.True((await Store().RegisterAsync(request)).WasNewlyApplied);
        var rolledBackClockStore = new SqliteAirframeStore(
            new(DatabasePath),
            NullLogger<SqliteAirframeStore>.Instance,
            new ScheduleEvidenceClock(request.RecordedAt.AddTicks(-1)));

        AirframeMaintenanceScheduleEvidenceResult replay = await rolledBackClockStore.RegisterAsync(request);

        Assert.False(replay.WasNewlyApplied);
        Assert.Equal(request.EvidenceId, replay.Record!.EvidenceId);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));
    }

    [Fact]
    public async Task StaleServiceRevisionAndMissingAirframeCannotCreateEvidence()
    {
        AirframeStoreRecord airframe = await CreateAsync();
        AirframeMaintenanceScheduleEvidenceRequest request = await ScheduleEvidenceRequestAsync(airframe);

        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Store().RegisterAsync(
            request with { ExpectedServiceRevision = request.ExpectedServiceRevision + 1 }));
        await Assert.ThrowsAsync<NotSupportedException>(() => Store().RegisterAsync(
            request with
            {
                EvidenceId = Guid.NewGuid(),
                ScheduleVersion = request.ScheduleVersion + 1
            }));
        var clockedStore = new SqliteAirframeStore(
            new(DatabasePath),
            NullLogger<SqliteAirframeStore>.Instance,
            new ScheduleEvidenceClock(request.RecordedAt));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => clockedStore.RegisterAsync(
            request with { EvidenceId = Guid.NewGuid(), RecordedAt = request.RecordedAt.AddTicks(1) }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().RegisterAsync(
            request with
            {
                EvidenceId = Guid.NewGuid(),
                RecordedAt = airframe.Airframe.CreatedAt.AddTicks(-1)
            }));
        AirframeMaintenanceScheduleEvidenceResult missing = await Store().RegisterAsync(
            request with { EvidenceId = Guid.NewGuid(), AirframeId = new(Guid.NewGuid()) });

        Assert.Equal(AirframeMaintenanceScheduleEvidenceStatus.NotFound, missing.Status);
        Assert.Null(missing.Record);
        Assert.False(missing.WasNewlyApplied);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));
    }

    [Fact]
    public async Task FailedEvidenceInsertRollsBackAndSameRequestRetriesOnce()
    {
        AirframeStoreRecord airframe = await CreateAsync();
        AirframeMaintenanceScheduleEvidenceRequest request = await ScheduleEvidenceRequestAsync(airframe);
        await ExecuteAsync("""
            CREATE TRIGGER fail_schedule_evidence BEFORE INSERT ON airframe_maintenance_schedule_evidence
            BEGIN SELECT RAISE(ABORT, 'injected schedule evidence failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => Store().RegisterAsync(request));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));
        await ExecuteAsync("DROP TRIGGER fail_schedule_evidence;");

        AirframeMaintenanceScheduleEvidenceResult retry = await Store().RegisterAsync(request);
        AirframeMaintenanceScheduleEvidenceResult replay = await Store().RegisterAsync(request);
        Assert.True(retry.WasNewlyApplied);
        Assert.False(replay.WasNewlyApplied);
        Assert.Equal(retry.Record, replay.Record);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));
    }

    [Fact]
    public async Task MissingOrMismatchedEvidenceRetainsGenericFallbackAndNeverInfersFromTitle()
    {
        AirframeStoreRecord airframe = await CreateAsync();
        AirframeServiceState fallback = await StateAsync(airframe.Airframe.AirframeId);
        AirframeMaintenanceSnapshot withoutEvidence = (await History().ReadAsync(
            new(airframe.Airframe.AirframeId))).Snapshot!;
        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityEvidenceRequired,
            Assert.Single(withoutEvidence.VerifiedScheduleCandidates).Status);
        Assert.Equal(LightAircraftRoutineInspectionV1.ScheduleId, withoutEvidence.ScheduleId);
        Assert.Empty(await Store().ReadForAirframeAsync(airframe.Airframe.AirframeId));

        AirframeMaintenanceScheduleEvidenceRequest mismatch = await ScheduleEvidenceRequestAsync(
            airframe, componentModel: "Continental O-300");
        Assert.True((await Store().RegisterAsync(mismatch)).WasNewlyApplied);
        AirframeMaintenanceSnapshot mismatched = (await History().ReadAsync(
            new(airframe.Airframe.AirframeId))).Snapshot!;
        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityMismatch,
            Assert.Single(mismatched.VerifiedScheduleCandidates).Status);
        Assert.Equal(fallback, mismatched.ServiceState);
        Assert.Equal(LightAircraftRoutineInspectionV1.ScheduleId, mismatched.ScheduleId);

        var titleOnly = new Airframe(new(Guid.NewGuid()), "C172SP Classic Passengers", Epoch.AddDays(-1));
        await Store().CreateAsync(titleOnly, new(0, AirframeDamageState.None), Epoch);
        Assert.Empty(await Store().ReadForAirframeAsync(titleOnly.AirframeId));
        Assert.Empty((await History().ReadAsync(new(titleOnly.AirframeId))).Snapshot!.VerifiedScheduleCandidates);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));
    }

    [Fact]
    public async Task SameModelAirframesRetainIndependentApplicabilityAndBaselines()
    {
        AirframeStoreRecord a = await CreateAsync();
        AirframeStoreRecord b = await CreateAsync();
        AirframeServiceState beforeB = await StateAsync(b.Airframe.AirframeId);
        AirframeMaintenanceScheduleEvidenceResult result = await Store().RegisterAsync(
            await ScheduleEvidenceRequestAsync(a));

        Assert.Equal(a.Airframe.AirframeId, Assert.IsType<AirframeMaintenanceScheduleEvidenceRecord>(result.Record)
            .Applicability.AirframeId);
        Assert.Single(await Store().ReadForAirframeAsync(a.Airframe.AirframeId));
        Assert.Empty(await Store().ReadForAirframeAsync(b.Airframe.AirframeId));
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyCurrent,
            Assert.Single((await History().ReadAsync(new(a.Airframe.AirframeId))).Snapshot!
                .VerifiedScheduleCandidates).Status);
        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityEvidenceRequired,
            Assert.Single((await History().ReadAsync(new(b.Airframe.AirframeId))).Snapshot!
                .VerifiedScheduleCandidates).Status);
        Assert.Equal(beforeB, await StateAsync(b.Airframe.AirframeId));
        Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
    }

    [Fact]
    public async Task GenericInspectionDoesNotMoveComponentBaselineOrClearVerifiedDueState()
    {
        AirframeStoreRecord created = await CreateAsync();
        await Store().RegisterAsync(await ScheduleEvidenceRequestAsync(created));
        AirframeMaintenanceScheduleEvidenceRecord baseline = Assert.Single(
            await Store().ReadForAirframeAsync(created.Airframe.AirframeId));
        FlightAirframeApplyResult flight = await FlyAsync(created, TimeSpan.FromHours(50), verticalSpeeds: [-500]);
        AirframeServiceState due = await StateAsync(created.Airframe.AirframeId);
        Assert.Equal(AirframeInspectionStatus.InspectionDue, due.InspectionStatus);
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyDue,
            Assert.Single((await History().ReadAsync(new(created.Airframe.AirframeId))).Snapshot!
                .VerifiedScheduleCandidates).Status);

        AirframeInspectionResult inspected = await Service().PerformRoutineInspectionAsync(
            Request(flight.Application.After, due));

        Assert.Equal(AirframeInspectionResultStatus.Inspected, inspected.Status);
        Assert.Equal(baseline, Assert.Single(await Store().ReadForAirframeAsync(created.Airframe.AirframeId)));
        AirframeMaintenanceSnapshot snapshot = (await History().ReadAsync(
            new(created.Airframe.AirframeId))).Snapshot!;
        Assert.Equal(AirframeInspectionStatus.Current, snapshot.InspectionStatus);
        Assert.Equal(AirframeMaintenanceScheduleStatus.TrackedProxyDue,
            Assert.Single(snapshot.VerifiedScheduleCandidates).Status);
        Assert.Equal(TimeSpan.Zero,
            Assert.Single(snapshot.VerifiedScheduleCandidates).TrackedAirborneTimeUntilDue!.Value);
    }

    [Fact]
    public async Task Schema18MigrationCreatesEmptyEvidenceAuthorityWithoutBackfill()
    {
        AirframeStoreRecord airframe = await CreateAsync(AirframeDamageState.Recorded);
        AirframeServiceState service = await StateAsync(airframe.Airframe.AirframeId);
        await ExecuteAsync("""
            DROP TABLE airframe_maintenance_schedule_evidence;
            PRAGMA user_version=18;
            """);
        ClearPool();

        Assert.Empty(await Store().ReadForAirframeAsync(airframe.Airframe.AirframeId));

        Assert.Equal(19L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='airframe_maintenance_schedule_evidence';"));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM sqlite_master WHERE type='index' AND name='ix_airframe_maintenance_schedule_evidence_airframe';"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_schedule_evidence;"));
        Assert.Equal(airframe, await Store().FindAsync(airframe.Airframe.AirframeId));
        Assert.Equal(service, await StateAsync(airframe.Airframe.AirframeId));
        Assert.Equal(AirframeMaintenanceScheduleStatus.ApplicabilityEvidenceRequired,
            Assert.Single((await History().ReadAsync(new(airframe.Airframe.AirframeId))).Snapshot!
                .VerifiedScheduleCandidates).Status);
    }

    [Theory]
    [InlineData("metadata-airframe")]
    [InlineData("metadata-aircraft")]
    [InlineData("malformed-payload")]
    [InlineData("unsupported-schema")]
    [InlineData("baseline-source")]
    [InlineData("baseline-out-of-range")]
    public async Task CorruptScheduleEvidenceFailsClosed(string fault)
    {
        AirframeStoreRecord airframe = await CreateAsync();
        AirframeStoreRecord other = await CreateAsync();
        AirframeMaintenanceScheduleEvidenceRequest request = await ScheduleEvidenceRequestAsync(airframe);
        await Store().RegisterAsync(request);
        string sql = fault switch
        {
            "metadata-airframe" => "UPDATE airframe_maintenance_schedule_evidence SET airframe_id=$other;",
            "metadata-aircraft" => "UPDATE airframe_maintenance_schedule_evidence SET canonical_aircraft_id='msfs-title:Other';",
            "malformed-payload" => "UPDATE airframe_maintenance_schedule_evidence SET payload_json='invalid';",
            "unsupported-schema" => "PRAGMA ignore_check_constraints=ON; UPDATE airframe_maintenance_schedule_evidence SET payload_schema_version=99;",
            "baseline-source" => "UPDATE airframe_maintenance_schedule_evidence SET baseline_source_reference='different-record';",
            _ => "UPDATE airframe_maintenance_schedule_evidence SET payload_json=json_set(payload_json,'$.baseline.trackedAirborneTime','99.00:00:00');"
        };
        await ExecuteAsync(sql, ("$other", other.Airframe.AirframeId.ToString()));
        AirframeId query = fault == "metadata-airframe" ? other.Airframe.AirframeId : airframe.Airframe.AirframeId;

        Exception? error = await Record.ExceptionAsync(() => Store().ReadForAirframeAsync(query));

        Assert.True(error is InvalidDataException or NotSupportedException, error?.ToString());
        Assert.Equal(airframe, await Store().FindAsync(airframe.Airframe.AirframeId));
        Assert.Equal(other, await Store().FindAsync(other.Airframe.AirframeId));
    }

    [Fact]
    public void ProductionRegistersScheduleEvidenceOnExistingPhysicalAirframeStore()
    {
        string composition = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<IAirframeMaintenanceScheduleEvidenceStore>(provider =>", composition);
        Assert.Contains("(SqliteAirframeStore)provider.GetRequiredService<IAirframeStore>()", composition);
    }

    private sealed class ScheduleEvidenceClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
