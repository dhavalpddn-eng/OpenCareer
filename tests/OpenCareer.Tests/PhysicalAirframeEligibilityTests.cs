using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class PhysicalAirframeEligibilityTests
{
    [Theory]
    [InlineData(AirframeDamageState.None, 0, PhysicalAirframeEligibilityStatus.Eligible)]
    [InlineData(AirframeDamageState.None, 1, PhysicalAirframeEligibilityStatus.Eligible)]
    [InlineData(AirframeDamageState.Recorded, 1, PhysicalAirframeEligibilityStatus.Eligible)]
    [InlineData(AirframeDamageState.Grounding, 0, PhysicalAirframeEligibilityStatus.Grounded)]
    [InlineData(AirframeDamageState.Grounding, 1, PhysicalAirframeEligibilityStatus.Grounded)]
    public async Task OnlyRequiresGroundingBlocksConditionEligibility(AirframeDamageState damage, double wear, PhysicalAirframeEligibilityStatus expected)
    {
        var original = Record(damage, wear);
        var store = new ReadOnlyStore(original);
        var service = new PhysicalAirframeEligibilityService(store);
        var result = await service.EvaluateAsync(ConsequenceFixture.Model, original.Airframe.AirframeId);
        Assert.Equal(expected, result.Status);
        Assert.Equal(!original.Condition.RequiresGrounding, result.IsEligible);
        Assert.Equal(1, store.ReadCount);
        Assert.Same(original, store.Record);
        if (expected == PhysicalAirframeEligibilityStatus.Grounded) Assert.Contains("grounded", result.Detail);
    }

    [Fact]
    public async Task ModelOnlyHasNoPhysicalStoreDependencyOrLookup()
    {
        var store = new ReadOnlyStore(null) { FailRead = true };
        Assert.True((await new PhysicalAirframeEligibilityService(store).EvaluateAsync(ConsequenceFixture.Model, null)).IsEligible);
        Assert.True((await new PhysicalAirframeEligibilityService().EvaluateAsync(ConsequenceFixture.Model, null)).IsEligible);
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(PhysicalAirframeEligibilityStatus.AuthorityUnavailable,
            (await new PhysicalAirframeEligibilityService().EvaluateAsync(ConsequenceFixture.Model, new AirframeId(Guid.NewGuid()))).Status);
    }

    [Theory]
    [InlineData("missing", PhysicalAirframeEligibilityStatus.Missing)]
    [InlineData("wrong-id", PhysicalAirframeEligibilityStatus.IdentityMismatch)]
    [InlineData("wrong-model", PhysicalAirframeEligibilityStatus.ModelMismatch)]
    public async Task ExactRequestedIdentityAndModelAreRequired(string fault, PhysicalAirframeEligibilityStatus expected)
    {
        var retained = Record(AirframeDamageState.None, 0);
        var id = fault == "wrong-id" ? new AirframeId(Guid.NewGuid()) : retained.Airframe.AirframeId;
        string model = fault == "wrong-model" ? "another-model" : ConsequenceFixture.Model;
        var service = new PhysicalAirframeEligibilityService(new ReadOnlyStore(fault == "missing" ? null : retained));
        Assert.Equal(expected, (await service.EvaluateAsync(model, id)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequireEligibleAsync(model, id));
    }

    [Fact]
    public async Task DefaultPhysicalIdIsRejectedBeforeStoreRead()
    {
        var store = new ReadOnlyStore(null) { FailRead = true };
        await Assert.ThrowsAsync<ArgumentException>(() => new PhysicalAirframeEligibilityService(store)
            .EvaluateAsync(ConsequenceFixture.Model, default(AirframeId)));
        Assert.Equal(0, store.ReadCount);
    }

    private static AirframeStoreRecord Record(AirframeDamageState damage, double wear) => new(
        new(new AirframeId(Guid.NewGuid()), ConsequenceFixture.Model, ConsequenceFixture.Epoch),
        new(wear, damage), 1, ConsequenceFixture.Epoch);

    private sealed class ReadOnlyStore(AirframeStoreRecord? record) : IAirframeStore, IAirframeServiceStateStore
    {
        public AirframeStoreRecord? Record { get; } = record;
        public int ReadCount { get; private set; }
        public bool FailRead { get; init; }
        public Task<AirframeStoreRecord?> FindAsync(AirframeId airframeId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (FailRead) throw new InvalidOperationException("Model-only path must not access physical aircraft.");
            return Task.FromResult(Record);
        }
        public Task<AirframeServiceState?> ReadServiceStateAsync(AirframeId id, CancellationToken ct = default)
        {
            if (FailRead) throw new InvalidOperationException("Model-only path must not access service state.");
            return Task.FromResult<AirframeServiceState?>(Record is null ? null
                : AirframeServiceState.Initial(id, Record.SavedAt, AirframeUsageOrigin.TrackingFromCreation));
        }
        public Task<AirframeStoreRecord> CreateAsync(Airframe airframe, AirframeCondition condition, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AirframeStoreRecord> UpdateConditionAsync(Airframe airframe, AirframeCondition condition, long expectedRevision, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
