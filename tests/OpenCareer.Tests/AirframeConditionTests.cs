using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class AirframeConditionTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PhysicalIdentityRoundTripsAndDoesNotRepresentTheModel()
    {
        var id = new AirframeId(Guid.NewGuid());
        Assert.Equal(id, AirframeId.Parse(id.ToString()));
        Assert.Throws<ArgumentException>(() => new AirframeId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => default(AirframeId).Validate());

        var a = new Airframe(id, AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId, Epoch);
        var b = new Airframe(new AirframeId(Guid.NewGuid()), a.CanonicalAircraftId, Epoch);
        Assert.Equal(a.CanonicalAircraftId, b.CanonicalAircraftId);
        Assert.NotEqual(a.AirframeId, b.AirframeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-airframe")]
    [InlineData("msfs-title:C172SP Classic Passengers")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void BlankOrInvalidPhysicalIdentityFailsClosed(string? value)
    {
        Exception? failure = Record.Exception(() => AirframeId.Parse(value!));
        Assert.True(failure is ArgumentException or FormatException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" model ")]
    [InlineData("model\nvariant")]
    public void InvalidCanonicalIdentityIsRejected(string? value) =>
        Assert.ThrowsAny<ArgumentException>(() => new Airframe(new AirframeId(Guid.NewGuid()), value!, Epoch));

    [Fact]
    public void DefaultIdentityAndMissingCreationTimeAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Airframe(default, "model", Epoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Airframe(new AirframeId(Guid.NewGuid()), "model", default));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidWearIsRejected(double wear) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirframeCondition(wear, AirframeDamageState.None));

    [Fact]
    public void WearAndDiscreteDamageHaveIndependentMeaning()
    {
        var worn = new AirframeCondition(0.8, AirframeDamageState.None);
        var damaged = new AirframeCondition(0, AirframeDamageState.Grounding);
        var recorded = new AirframeCondition(0.8, AirframeDamageState.Recorded);
        Assert.False(worn.HasDamage);
        Assert.False(worn.RequiresGrounding);
        Assert.True(damaged.HasDamage);
        Assert.True(damaged.RequiresGrounding);
        Assert.Equal(0, damaged.WearFraction);
        Assert.Equal(worn.WearFraction, recorded.WearFraction);
        Assert.True(recorded.HasDamage);
        Assert.False(recorded.RequiresGrounding);
        Assert.Equal(1, new AirframeCondition(1, AirframeDamageState.None).WearFraction);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AirframeCondition(0, (AirframeDamageState)99));
    }
}
