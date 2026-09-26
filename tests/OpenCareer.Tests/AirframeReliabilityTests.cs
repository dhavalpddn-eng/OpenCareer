using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed partial class AirframeReliabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_root, "reliability.db");
    private static DateTimeOffset Epoch => ConsequenceFixture.Epoch;
    private static readonly TimeProvider Clock = new FixedClock();
    private SqliteAirframeStore Store() => new(new(DatabasePath), NullLogger<SqliteAirframeStore>.Instance);
    private AirframeReliabilitySource Source() { var store = Store(); return new(store, store, Clock); }
    private AirframeFailureEligibilityService Boundary() => new(Source());
    private Task<AirframeStoreRecord> CreateAsync(AirframeDamageState damage = AirframeDamageState.None, double wear = 0.1234567890123456) =>
        Store().CreateAsync(new(new AirframeId(Guid.NewGuid()), ConsequenceFixture.Model, Epoch.AddDays(-1)), new(wear, damage), Epoch);
    private async Task<AirframeServiceState> StateAsync(AirframeId id) => Assert.IsType<AirframeServiceState>(await Store().ReadServiceStateAsync(id));
    private async Task<AirframeReliabilityAssessment> ReadAsync(AirframeId id)
    {
        var result = await Source().ReadAsync(id);
        Assert.Equal(AirframeReliabilityReadStatus.Available, result.Status); Assert.Equal(id, result.AirframeId);
        return Assert.IsType<AirframeReliabilityAssessment>(result.Assessment);
    }
    private async Task<AirframeStoreRecord> FlyAsync(AirframeStoreRecord current, int hours = 1, bool crash = false)
    {
        var session = ConsequenceFixture.Session(current.Airframe.AirframeId,
            crash ? FlightSessionStatus.Interrupted : FlightSessionStatus.Completed, crash, -500);
        session = session with { UpdatedAt = current.SavedAt.AddHours(hours + 4),
            TimeLedger = session.TimeLedger with { AirborneTime = TimeSpan.FromHours(hours), BlockTime = TimeSpan.FromHours(hours) } };
        return (await Store().ApplyAsync(FlightAirframeConsequenceCalculator.Calculate(session), current, session.UpdatedAt)).Application.After;
    }
    private async Task<AirframeStoreRecord> RepairAsync(AirframeStoreRecord record)
    {
        var store = Store();
        return (await new AirframeMaintenanceService(store, store).RepairDiscreteDamageAsync(
            new(Guid.NewGuid(), record.Airframe.AirframeId, record.Revision, record.SavedAt.AddMinutes(1)))).Current!;
    }
    private async Task InspectAsync(AirframeStoreRecord record)
    {
        var state = await StateAsync(record.Airframe.AirframeId);
        var time = (record.SavedAt > state.UpdatedAt ? record.SavedAt : state.UpdatedAt).AddMinutes(1);
        var store = Store();
        Assert.Equal(AirframeInspectionResultStatus.Inspected,
            (await new AirframeMaintenanceService(store, store).PerformRoutineInspectionAsync(
                new(Guid.NewGuid(), record.Airframe.AirframeId, record.Revision, state.Revision, time))).Status);
    }

    public static IEnumerable<object[]> StatusCases()
    {
        foreach (var damage in Enum.GetValues<AirframeDamageState>())
        foreach (bool due in new[] { false, true })
        foreach (double wear in new[] { 0d, 1d })
            yield return [damage, due, wear];
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void StatusUsesOnlyKnownDamageAndInspectionFacts(AirframeDamageState damage, bool due, double wear)
    {
        var current = new AirframeStoreRecord(new(new(Guid.NewGuid()), ConsequenceFixture.Model, Epoch), new(wear, damage), 1, Epoch);
        var service = AirframeServiceState.Initial(current.Airframe.AirframeId, Epoch, AirframeUsageOrigin.TrackingFromCreation);
        service = service.AddTrustedUsage(due ? TimeSpan.FromHours(50) : TimeSpan.Zero, 7, Epoch.AddHours(50));
        var result = AirframeReliabilityAssessment.Evaluate(current, service, Clock.GetUtcNow());
        var expected = damage == AirframeDamageState.Grounding || due ? AirframeReliabilityStatus.Unavailable
            : damage == AirframeDamageState.Recorded ? AirframeReliabilityStatus.AttentionRequired : AirframeReliabilityStatus.Nominal;
        Assert.Equal(expected, result.Status);
        var reasons = damage switch
        {
            AirframeDamageState.Grounding => AirframeReliabilityReason.DamageGrounding,
            AirframeDamageState.Recorded => AirframeReliabilityReason.RecordedDamage,
            _ => AirframeReliabilityReason.None
        };
        if (due) reasons |= AirframeReliabilityReason.InspectionDue;
        Assert.Equal(reasons, result.Reasons);
        Assert.Equal(wear, result.WearFraction); Assert.Equal(damage, result.Damage);
        Assert.Equal(current.Airframe.AirframeId, result.AirframeId);
        Assert.Equal(ConsequenceFixture.Model, result.CanonicalAircraftId);
        Assert.Equal(service.TotalTrackedAirborneTime, result.TotalTrackedAirborneTime);
        Assert.Equal(service.TotalTrackedLandingCycles, result.TotalTrackedLandingCycles);
        Assert.Equal(service.LandingCycleOrigin, result.LandingCycleOrigin);
        Assert.Equal(current.Revision, result.ConditionRevision); Assert.Equal(service.Revision, result.ServiceRevision);
        Assert.Equal(Clock.GetUtcNow(), result.EvaluatedAt);
        Assert.Equal(AirframeReliabilityEvidenceSource.OpenCareerFallback, result.Source);
        Assert.Equal(AirframeReliabilityConfidence.FallbackOnly, result.Confidence);
        Assert.Equal(AirframeReliabilityDataAvailability.Unavailable, result.WearStatusThresholdAvailability);
    }

    [Fact]
    public async Task UnknownReliabilityDataIsUnavailableRatherThanANumericEstimate()
    {
        var current = await CreateAsync(); var assessment = await ReadAsync(current.Airframe.AirframeId);
        Assert.Equal(AirframeReliabilityDataAvailability.Unavailable, assessment.FailureProbabilityAvailability);
        Assert.Equal(AirframeReliabilityDataAvailability.Unavailable, assessment.ComponentReliabilityAvailability);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(assessment));
        Assert.Equal("Unavailable", json.RootElement.GetProperty("FailureProbabilityAvailability").GetString());
        Assert.Equal("Unavailable", json.RootElement.GetProperty("ComponentReliabilityAvailability").GetString());
        Assert.False(json.RootElement.TryGetProperty("FailureProbability", out _));
        Assert.False(json.RootElement.TryGetProperty("ComponentHealth", out _));
        Assert.False(json.RootElement.TryGetProperty("ReliabilityPercentage", out _));
    }

    [Theory]
    [InlineData(AirframeDamageState.None, false, AirframeFailureEligibilityStatus.ComponentModelUnavailable)]
    [InlineData(AirframeDamageState.Recorded, false, AirframeFailureEligibilityStatus.ComponentModelUnavailable)]
    [InlineData(AirframeDamageState.Grounding, false, AirframeFailureEligibilityStatus.MaintenanceUnavailable)]
    [InlineData(AirframeDamageState.None, true, AirframeFailureEligibilityStatus.MaintenanceUnavailable)]
    [InlineData(AirframeDamageState.Recorded, true, AirframeFailureEligibilityStatus.MaintenanceUnavailable)]
    [InlineData(AirframeDamageState.Grounding, true, AirframeFailureEligibilityStatus.MaintenanceUnavailable)]
    public async Task PhysicalFailureBoundaryNeverAuthorizesGeneration(AirframeDamageState damage, bool due, AirframeFailureEligibilityStatus expected)
    {
        var current = await CreateAsync(damage, 1);
        if (due) current = await FlyAsync(current, 50);
        var service = await StateAsync(current.Airframe.AirframeId);
        var result = await Boundary().EvaluateAsync(new(ConsequenceFixture.Model, current.Airframe.AirframeId));
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == AirframeFailureEligibilityStatus.ComponentModelUnavailable, result.MaintenanceGatePassed);
        Assert.False(result.CanGenerateFailure);
        Assert.Equal(current.Airframe.AirframeId, result.PhysicalAirframeId);
        Assert.Equal(current, result.Assessment!.Current);
        Assert.Equal(service, result.Assessment.ServiceState);
        if (damage == AirframeDamageState.Grounding && due)
            Assert.Equal(AirframeReliabilityReason.DamageGrounding | AirframeReliabilityReason.InspectionDue, result.Assessment.Reasons);
        // This separate dispatch authority is unchanged; passing it does not enable a failure.
        var dispatch = await new PhysicalAirframeEligibilityService(Store()).EvaluateAsync(ConsequenceFixture.Model, current.Airframe.AirframeId);
        Assert.Equal(result.MaintenanceGatePassed, dispatch.IsEligible);
        Assert.Equal(current, await Store().FindAsync(current.Airframe.AirframeId));
        Assert.Equal(service, await StateAsync(current.Airframe.AirframeId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelOnlyOrUnknownAssignmentRequiresNoPhysicalReads(bool modelOnly)
    {
        var result = await Boundary().EvaluateAsync(modelOnly ? new(ConsequenceFixture.Model) : null);
        Assert.Equal(AirframeFailureEligibilityStatus.NoPhysicalAirframe, result.Status);
        Assert.Null(result.PhysicalAirframeId); Assert.Null(result.Assessment); Assert.Null(result.ReadStatus);
        Assert.False(result.MaintenanceGatePassed); Assert.False(result.CanGenerateFailure);
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task ExactIdentityAndSameModelIsolationSurviveRestartWithoutWrites()
    {
        var a = await CreateAsync(AirframeDamageState.Recorded); var b = await CreateAsync();
        var first = await ReadAsync(a.Airframe.AirframeId); var other = await ReadAsync(b.Airframe.AirframeId);
        await ExecuteAsync("""
            CREATE TRIGGER no_condition BEFORE UPDATE ON airframes BEGIN SELECT RAISE(ABORT, 'read mutated condition'); END;
            CREATE TRIGGER no_service BEFORE UPDATE ON airframe_service_state BEGIN SELECT RAISE(ABORT, 'read mutated service'); END;
            CREATE TRIGGER no_flight_history BEFORE INSERT ON flight_airframe_consequences BEGIN SELECT RAISE(ABORT, 'read wrote history'); END;
            CREATE TRIGGER no_service_history BEFORE INSERT ON airframe_maintenance_events BEGIN SELECT RAISE(ABORT, 'read wrote history'); END;
            """);
        ClearPool();
        Assert.Equal(first, await ReadAsync(a.Airframe.AirframeId));
        Assert.Equal(other, await ReadAsync(b.Airframe.AirframeId));
        Assert.Equal(AirframeReliabilityStatus.AttentionRequired, first.Status);
        Assert.Equal(AirframeReliabilityStatus.Nominal, other.Status);
        Assert.Equal(AirframeFailureEligibilityStatus.ComponentModelUnavailable,
            (await Boundary().EvaluateAsync(new(ConsequenceFixture.Model, a.Airframe.AirframeId))).Status);
        var snapshot = (await new AirframeMaintenanceHistorySource(Store(), Store(), serviceStates: Store(), clock: Clock)
            .ReadAsync(new(a.Airframe.AirframeId))).Snapshot!;
        Assert.Equal(first, snapshot.Reliability);
        Assert.Equal(0, snapshot.TotalTrackedLandingCycles);
        Assert.Equal(AirframeUsageOrigin.TrackingFromCreation, snapshot.LandingCycleOrigin);
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId)); Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
        Assert.Equal(18L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table' AND name LIKE '%reliability%';"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrashRepairPreservesUsageAndLeavesInspectionDueUnavailable(bool due)
    {
        var crashed = await FlyAsync(await CreateAsync(), due ? 50 : 1, crash: true);
        var id = crashed.Airframe.AirframeId;
        var before = await ReadAsync(id);
        Assert.Equal(AirframeReliabilityStatus.Unavailable, before.Status);
        Assert.True(before.Reasons.HasFlag(AirframeReliabilityReason.DamageGrounding));
        var repaired = await RepairAsync(crashed);
        var after = await ReadAsync(id);
        Assert.Equal(before.ServiceState, after.ServiceState);
        Assert.Equal(before.TotalTrackedAirborneTime, after.TotalTrackedAirborneTime);
        Assert.Equal(1, before.TotalTrackedLandingCycles);
        Assert.Equal(before.TotalTrackedLandingCycles, after.TotalTrackedLandingCycles);
        Assert.Equal(before.WearFraction, after.WearFraction);
        Assert.Equal(due ? AirframeReliabilityStatus.Unavailable : AirframeReliabilityStatus.Nominal, after.Status);
        if (due)
        {
            Assert.Equal(AirframeReliabilityReason.InspectionDue, after.Reasons);
            await InspectAsync(repaired);
            Assert.Equal(AirframeReliabilityStatus.Nominal, (await ReadAsync(id)).Status);
        }
        Assert.Equal(AirframeFailureEligibilityStatus.ComponentModelUnavailable, (await Boundary().EvaluateAsync(new(ConsequenceFixture.Model, id))).Status);
        Assert.Single((await Store().ReadHistoryAsync(new(id))).Entries);
    }

    [Theory]
    [InlineData(AirframeDamageState.None, AirframeReliabilityStatus.Nominal)]
    [InlineData(AirframeDamageState.Recorded, AirframeReliabilityStatus.AttentionRequired)]
    [InlineData(AirframeDamageState.Grounding, AirframeReliabilityStatus.Unavailable)]
    public async Task InspectionNeverErasesRecordedOrGroundingDamage(AirframeDamageState damage, AirframeReliabilityStatus expected)
    {
        var condition = await FlyAsync(await CreateAsync(damage), 50);
        var id = condition.Airframe.AirframeId;
        Assert.Equal(AirframeReliabilityStatus.Unavailable, (await ReadAsync(id)).Status);
        await InspectAsync(condition);
        var result = await ReadAsync(id);
        Assert.Equal(expected, result.Status); Assert.Equal(damage, result.Damage); Assert.Equal(condition, result.Current);
        Assert.Equal(AirframeInspectionStatus.Current, result.InspectionStatus);
        Assert.Equal(1, result.TotalTrackedLandingCycles);
        Assert.False(result.Reasons.HasFlag(AirframeReliabilityReason.InspectionDue));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
    private void ClearPool()
    {
        using var pool = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString());
        SqliteConnection.ClearPool(pool);
    }
    public void Dispose() { ClearPool(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Epoch.AddDays(10); }
}
