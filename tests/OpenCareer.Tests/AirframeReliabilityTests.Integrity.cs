using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed partial class AirframeReliabilityTests
{
    [Fact]
    public async Task MissingAirframeIsNotFoundAndCannotPassFailureBoundary()
    {
        var id = new AirframeId(Guid.NewGuid());
        var read = await Source().ReadAsync(id);
        Assert.Equal(AirframeReliabilityReadStatus.NotFound, read.Status); Assert.Null(read.Assessment);
        var failure = await Boundary().EvaluateAsync(new(ConsequenceFixture.Model, id));
        Assert.Equal(AirframeFailureEligibilityStatus.ReliabilityUnavailable, failure.Status);
        Assert.Equal(AirframeReliabilityReadStatus.NotFound, failure.ReadStatus);
        Assert.False(failure.MaintenanceGatePassed); Assert.False(failure.CanGenerateFailure);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframes;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_service_state;"));
    }

    [Fact]
    public async Task DefaultIdentityIsRejectedBeforePhysicalAccess()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Source().ReadAsync(default));
        Assert.Throws<ArgumentException>(() => new OpenCareer.Domain.Flights.FlightSessionAircraftIdentity(ConsequenceFixture.Model, default(AirframeId)));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task CancelledReadNeverAccessesPhysicalState()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var id = new AirframeId(Guid.NewGuid());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source().ReadAsync(id, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Boundary().EvaluateAsync(new(ConsequenceFixture.Model, id), cancellation.Token));
        Assert.False(File.Exists(DatabasePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingServiceStateOrAuthorityReturnsExplicitUnavailable(bool missingAuthority)
    {
        var current = await CreateAsync();
        var id = current.Airframe.AirframeId;
        AirframeReliabilitySource source;
        if (missingAuthority) source = new(new ConditionOnlyStore(current), clock: Clock);
        else { await ExecuteAsync("DELETE FROM airframe_service_state;"); source = Source(); }
        var read = await source.ReadAsync(id);
        Assert.Equal(AirframeReliabilityReadStatus.ServiceStateUnavailable, read.Status); Assert.Null(read.Assessment);
        var result = await new AirframeFailureEligibilityService(source).EvaluateAsync(new(ConsequenceFixture.Model, id));
        Assert.Equal(AirframeFailureEligibilityStatus.ReliabilityUnavailable, result.Status);
        Assert.Equal(read.Status, result.ReadStatus); Assert.Null(result.Assessment);
        Assert.False(result.MaintenanceGatePassed); Assert.False(result.CanGenerateFailure);
        Assert.Equal(current, await Store().FindAsync(id));
    }

    [Theory]
    [InlineData("airframe")]
    [InlineData("service")]
    [InlineData("service-time")]
    public async Task MismatchedAuthoritativeIdentityFailsClosed(string fault)
    {
        var a = await CreateAsync(); var b = await CreateAsync(); var service = await StateAsync(a.Airframe.AirframeId);
        var probe = new ReadProbe(Store())
        {
            OverrideCondition = fault == "airframe" ? b : null,
            OverrideService = fault == "service" ? await StateAsync(b.Airframe.AirframeId)
                : fault == "service-time" ? service with { UpdatedAt = a.Airframe.CreatedAt.AddTicks(-1) } : null
        };
        var source = new AirframeReliabilitySource(probe, probe, Clock);
        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync(a.Airframe.AirframeId));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AirframeFailureEligibilityService(source)
            .EvaluateAsync(new(ConsequenceFixture.Model, a.Airframe.AirframeId)));
        Assert.Equal(a, await Store().FindAsync(a.Airframe.AirframeId)); Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
    }

    [Fact]
    public async Task AssignedCanonicalModelCannotBorrowAnotherModelsAssessment()
    {
        var current = await CreateAsync();
        var result = await Boundary().EvaluateAsync(new("different-canonical-model", current.Airframe.AirframeId));
        Assert.Equal(AirframeFailureEligibilityStatus.ReliabilityUnavailable, result.Status);
        Assert.Null(result.Assessment); Assert.False(result.MaintenanceGatePassed); Assert.False(result.CanGenerateFailure);
        Assert.Equal(ConsequenceFixture.Model, (await ReadAsync(current.Airframe.AirframeId)).CanonicalAircraftId);
    }

    [Theory]
    [InlineData("service-only")]
    [InlineData("condition-only")]
    [InlineData("both")]
    public async Task ConcurrentInputChangeRejectsMixedReliabilityAndFreshReadSucceeds(string change)
    {
        var current = await FlyAsync(await CreateAsync(), 50);
        var probe = new ReadProbe(Store());
        if (change == "condition-only")
            probe.BeforeConditionRead = async n => { if (n == 2) await Store().UpdateConditionAsync(current.Airframe,
                new(current.Condition.WearFraction, AirframeDamageState.Recorded), current.Revision, current.SavedAt.AddMinutes(1)); };
        else probe.BeforeServiceRead = async n =>
        {
            if (n != 2) return;
            if (change == "service-only") await InspectAsync(current);
            else await FlyAsync(current);
        };
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => new AirframeReliabilitySource(probe, probe, Clock).ReadAsync(current.Airframe.AirframeId));
        var fresh = await ReadAsync(current.Airframe.AirframeId);
        Assert.Equal(await Store().FindAsync(current.Airframe.AirframeId), fresh.Current);
        Assert.Equal(await StateAsync(current.Airframe.AirframeId), fresh.ServiceState);
    }

    [Fact]
    public async Task MaintenanceReliabilityUsesExistingCoherentRecordsWithoutExtraReads()
    {
        var current = await CreateAsync(AirframeDamageState.Recorded);
        var probe = new ReadProbe(Store());
        var snapshot = (await new AirframeMaintenanceHistorySource(probe, Store(), serviceStates: probe, clock: Clock)
            .ReadAsync(new(current.Airframe.AirframeId))).Snapshot!;
        Assert.Equal(2, probe.ConditionReads); Assert.Equal(2, probe.ServiceReads);
        var assessment = snapshot.Reliability;
        Assert.Equal(current, assessment.Current); Assert.Same(snapshot.ServiceState, assessment.ServiceState);
        Assert.Equal(snapshot.Revision, assessment.ConditionRevision); Assert.Equal(snapshot.ServiceState.Revision, assessment.ServiceRevision);
        Assert.Equal(snapshot.EvaluatedAt, assessment.EvaluatedAt);
        Assert.Equal(AirframeReliabilityStatus.AttentionRequired, assessment.Status);
        Assert.Equal(AirframeServiceability.AvailableForDispatch, snapshot.Serviceability);
        Assert.Equal(assessment, snapshot.Reliability);
        Assert.Equal(2, probe.ConditionReads); Assert.Equal(2, probe.ServiceReads);
    }

    [Theory]
    [InlineData("condition")]
    [InlineData("service")]
    public async Task MaintenanceSnapshotStillRejectsConcurrentInputsBeforeReliabilityPublication(string change)
    {
        var current = await FlyAsync(await CreateAsync(), 50);
        var probe = new ReadProbe(Store());
        if (change == "service") probe.BeforeServiceRead = async n => { if (n == 2) await InspectAsync(current); };
        else probe.BeforeConditionRead = async n => { if (n == 2) await Store().UpdateConditionAsync(current.Airframe,
            new(0.5, AirframeDamageState.Grounding), current.Revision, current.SavedAt.AddMinutes(1)); };
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() =>
            new AirframeMaintenanceHistorySource(probe, Store(), serviceStates: probe, clock: Clock).ReadAsync(new(current.Airframe.AirframeId)));
    }

    [Theory]
    [InlineData("condition")]
    [InlineData("service")]
    [InlineData("schedule")]
    public async Task CorruptPersistedInputsCannotProduceAssessmentOrFailureEligibility(string fault)
    {
        var current = await CreateAsync(); var id = current.Airframe.AirframeId;
        await ExecuteAsync("PRAGMA ignore_check_constraints=ON; " + (fault switch
        {
            "condition" => "UPDATE airframes SET wear_fraction='corrupt';",
            "service" => "UPDATE airframe_service_state SET total_airborne_ticks=-1;",
            _ => "UPDATE airframe_service_state SET schedule_version=99;"
        }));
        var error = await Record.ExceptionAsync(() => Source().ReadAsync(id));
        Assert.True(error is InvalidDataException or NotSupportedException, error?.ToString());
        Assert.NotNull(await Record.ExceptionAsync(() => Boundary().EvaluateAsync(new(ConsequenceFixture.Model, id))));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public void AssessmentRejectsInvalidEvaluationTimestampAndInputs()
    {
        var current = new AirframeStoreRecord(new(new(Guid.NewGuid()), ConsequenceFixture.Model, Epoch), new(1, AirframeDamageState.None), 1, Epoch);
        var service = AirframeServiceState.Initial(current.Airframe.AirframeId, Epoch, AirframeUsageOrigin.TrackingFromCreation);
        Assert.Throws<ArgumentOutOfRangeException>(() => AirframeReliabilityAssessment.Evaluate(current, service, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => AirframeReliabilityAssessment.Evaluate(current with { Revision = 0 }, service, Epoch));
        Assert.Throws<InvalidDataException>(() => AirframeReliabilityAssessment.Evaluate(current, service with { AirframeId = new(Guid.NewGuid()) }, Epoch));
    }

    [Fact]
    public void ProductionRegistersReadAndBoundaryServicesWithoutSimulatorDependencies()
    {
        string app = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<AirframeReliabilitySource>()", app);
        Assert.Contains("AddSingleton<AirframeFailureEligibilityService>()", app);
        Assert.All(typeof(AirframeFailureEligibilityService).GetConstructors().Single().GetParameters(),
            parameter => Assert.Equal(typeof(AirframeReliabilitySource), parameter.ParameterType));
        Assert.DoesNotContain("Eligible", Enum.GetNames<AirframeFailureEligibilityStatus>());
    }

    private sealed class ConditionOnlyStore(AirframeStoreRecord current) : IAirframeStore
    {
        public Task<AirframeStoreRecord?> FindAsync(AirframeId id, CancellationToken ct = default) => Task.FromResult<AirframeStoreRecord?>(current);
        public Task<AirframeStoreRecord> CreateAsync(Airframe a, AirframeCondition c, DateTimeOffset t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AirframeStoreRecord> UpdateConditionAsync(Airframe a, AirframeCondition c, long r, DateTimeOffset t, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ReadProbe(SqliteAirframeStore inner) : IAirframeStore, IAirframeServiceStateStore
    {
        public int ConditionReads { get; private set; }
        public int ServiceReads { get; private set; }
        public AirframeStoreRecord? OverrideCondition { get; init; }
        public AirframeServiceState? OverrideService { get; init; }
        public Func<int, Task>? BeforeConditionRead { get; set; }
        public Func<int, Task>? BeforeServiceRead { get; set; }
        public async Task<AirframeStoreRecord?> FindAsync(AirframeId id, CancellationToken ct = default)
        {
            ConditionReads++; if (BeforeConditionRead is not null) await BeforeConditionRead(ConditionReads);
            return OverrideCondition ?? await inner.FindAsync(id, ct);
        }
        public async Task<AirframeServiceState?> ReadServiceStateAsync(AirframeId id, CancellationToken ct = default)
        {
            ServiceReads++; if (BeforeServiceRead is not null) await BeforeServiceRead(ServiceReads);
            return OverrideService ?? await inner.ReadServiceStateAsync(id, ct);
        }
        public Task<AirframeStoreRecord> CreateAsync(Airframe a, AirframeCondition c, DateTimeOffset t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AirframeStoreRecord> UpdateConditionAsync(Airframe a, AirframeCondition c, long r, DateTimeOffset t, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
