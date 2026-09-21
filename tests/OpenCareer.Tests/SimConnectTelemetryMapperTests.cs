using OpenCareer.SimConnect;

namespace OpenCareer.Tests;

public sealed class SimConnectTelemetryMapperTests
{
    [Fact]
    public void MapsSdkValuesIntoNormalizedTelemetry()
    {
        var values = new double[SimConnectTelemetryDefinition.ValueCount];
        Set(values, SimConnectTelemetryValue.Latitude, 43.2338);
        Set(values, SimConnectTelemetryValue.Longitude, -75.4069);
        Set(values, SimConnectTelemetryValue.AltitudeMsl, 2_500);
        Set(values, SimConnectTelemetryValue.AltitudeAgl, 1_100);
        Set(values, SimConnectTelemetryValue.IndicatedAirspeed, 140);
        Set(values, SimConnectTelemetryValue.GroundSpeed, 152);
        Set(values, SimConnectTelemetryValue.VerticalSpeedFeetPerSecond, -12.5);
        Set(values, SimConnectTelemetryValue.HeadingTrue, -10);
        Set(values, SimConnectTelemetryValue.Pitch, 3.5);
        Set(values, SimConnectTelemetryValue.Bank, -8.25);
        Set(values, SimConnectTelemetryValue.NormalAcceleration, 1.08);
        Set(values, SimConnectTelemetryValue.OnGround, 0);
        Set(values, SimConnectTelemetryValue.ParkingBrake, 0);
        Set(values, SimConnectTelemetryValue.NumberOfEngines, 3);
        Set(values, SimConnectTelemetryValue.Engine1Combustion, -1);
        Set(values, SimConnectTelemetryValue.Engine2Combustion, 0);
        Set(values, SimConnectTelemetryValue.Engine3Combustion, 1);
        Set(values, SimConnectTelemetryValue.Engine4Combustion, 1);
        Set(values, SimConnectTelemetryValue.FuelTotalWeight, 100);
        Set(values, SimConnectTelemetryValue.TotalWeight, 2_500);
        Set(values, SimConnectTelemetryValue.EmptyWeight, 1_800);
        Set(values, SimConnectTelemetryValue.FlapsHandlePercentOver100, 0.25);
        Set(values, SimConnectTelemetryValue.GearTotalPercent, 99);
        Set(values, SimConnectTelemetryValue.SlewActive, -1);

        DateTimeOffset timestamp = new(2026, 9, 17, 21, 30, 0, TimeSpan.Zero);
        var snapshot = SimConnectTelemetryMapper.Map(values, timestamp, paused: true);

        Assert.NotNull(snapshot);
        Assert.Equal(timestamp, snapshot.Timestamp);
        Assert.Equal(43.2338, snapshot.LatitudeDegrees);
        Assert.Equal(-75.4069, snapshot.LongitudeDegrees);
        Assert.Equal(-750, snapshot.VerticalSpeedFeetPerMinute);
        Assert.Equal(350, snapshot.HeadingDegrees);
        Assert.Equal(2, snapshot.EnginesRunning);
        Assert.Equal(100, snapshot.FuelTotalPounds);
        Assert.Equal(600, snapshot.PayloadPounds);
        Assert.Equal(25, snapshot.FlapsPositionPercent);
        Assert.True(snapshot.GearDown);
        Assert.True(snapshot.Paused);
        Assert.True(snapshot.SlewActive);
        Assert.False(snapshot.OnGround);
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.95, true)]
    [InlineData(0.94, false)]
    [InlineData(100.0, true)]
    [InlineData(95.0, true)]
    [InlineData(94.9, false)]
    public void GearExtensionAcceptsObservedAndDocumentedPercentScales(double rawValue, bool expectedDown)
    {
        var values = new double[SimConnectTelemetryDefinition.ValueCount];
        Set(values, SimConnectTelemetryValue.GearTotalPercent, rawValue);

        var snapshot = SimConnectTelemetryMapper.Map(values, DateTimeOffset.UtcNow, paused: false);

        Assert.NotNull(snapshot);
        Assert.Equal(expectedDown, snapshot.GearDown);
    }

    [Fact]
    public void InvalidOrNonFinitePacketsAreRejected()
    {
        Assert.Null(SimConnectTelemetryMapper.Map([], DateTimeOffset.UtcNow, paused: false));

        var values = new double[SimConnectTelemetryDefinition.ValueCount];
        values[0] = double.NaN;
        Assert.Null(SimConnectTelemetryMapper.Map(values, DateTimeOffset.UtcNow, paused: false));
    }

    [Fact]
    public void DerivedPayloadAndPercentagesAreBounded()
    {
        var values = new double[SimConnectTelemetryDefinition.ValueCount];
        Set(values, SimConnectTelemetryValue.FuelTotalWeight, 800);
        Set(values, SimConnectTelemetryValue.TotalWeight, 1_000);
        Set(values, SimConnectTelemetryValue.EmptyWeight, 500);
        Set(values, SimConnectTelemetryValue.FlapsHandlePercentOver100, 2);
        Set(values, SimConnectTelemetryValue.GearTotalPercent, 94.9);

        var snapshot = SimConnectTelemetryMapper.Map(values, DateTimeOffset.UtcNow, paused: false);

        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot.PayloadPounds);
        Assert.Equal(100, snapshot.FlapsPositionPercent);
        Assert.False(snapshot.GearDown);
    }

    private static void Set(double[] values, SimConnectTelemetryValue index, double value) =>
        values[(int)index] = value;
}
