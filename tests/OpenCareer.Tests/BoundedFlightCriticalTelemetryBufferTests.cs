using OpenCareer.Domain.Telemetry;
using OpenCareer.SimConnect;

namespace OpenCareer.Tests;

public sealed class BoundedFlightCriticalTelemetryBufferTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BufferOrdersSamplesRejectsDuplicatesAndDropsOldestOnOverflow()
    {
        var buffer = new BoundedFlightCriticalTelemetryBuffer(capacity: 3);

        buffer.Add(Sample(1));
        buffer.Add(Sample(2));
        buffer.Add(Sample(2));
        buffer.Add(Sample(1.5));
        buffer.Add(Sample(3));
        buffer.Add(Sample(4));

        Assert.Equal(
            [2d, 3d, 4d],
            buffer.ReadAfter(null)
                .Select(sample => (sample.Timestamp - Epoch).TotalSeconds));
        Assert.Equal(
            [3d, 4d],
            buffer.ReadAfter(Epoch.AddSeconds(2))
                .Select(sample => (sample.Timestamp - Epoch).TotalSeconds));
    }

    [Fact]
    public void ClearRemovesOldSessionSamplesAndResetsTimestampBoundary()
    {
        var buffer = new BoundedFlightCriticalTelemetryBuffer(capacity: 2);
        buffer.Add(Sample(10));

        buffer.Clear();
        buffer.Add(Sample(1));

        AircraftTelemetrySnapshot retained = Assert.Single(buffer.ReadAfter(null));
        Assert.Equal(Epoch.AddSeconds(1), retained.Timestamp);
    }

    [Fact]
    public void DecimationRetainsOnGroundEdgesInsideMinimumSpacing()
    {
        var buffer = new BoundedFlightCriticalTelemetryBuffer(
            capacity: 10,
            minimumSampleSpacing: TimeSpan.FromMilliseconds(50));

        Assert.True(buffer.Add(Sample(0.001, onGround: true)));
        Assert.False(buffer.Add(Sample(0.011, onGround: true)));
        Assert.True(buffer.Add(Sample(0.021, onGround: false)));
        Assert.True(buffer.Add(Sample(0.031, onGround: true)));
        Assert.False(buffer.Add(Sample(0.041, onGround: true)));
        Assert.True(buffer.Add(Sample(0.091, onGround: true)));

        AircraftTelemetrySnapshot[] retained =
            buffer.ReadAfter(null).ToArray();
        Assert.Equal(
            [true, false, true, true],
            retained.Select(sample => sample.OnGround));
        Assert.Equal(
            [
                Epoch.AddMilliseconds(1),
                Epoch.AddMilliseconds(21),
                Epoch.AddMilliseconds(31),
                Epoch.AddMilliseconds(91)
            ],
            retained.Select(sample => sample.Timestamp));
    }

    [Fact]
    public void EdgeHeavyFrameStreamRemainsBoundedAtProductionCapacity()
    {
        var buffer = new BoundedFlightCriticalTelemetryBuffer(
            SimConnectConnection.FlightCriticalTelemetryCapacity,
            SimConnectConnection.FlightCriticalTelemetryMinimumSpacing);

        for (int index = 1; index <= 1_000; index++)
        {
            Assert.True(buffer.Add(
                Sample(
                    index / 1_000d,
                    onGround: index % 2 == 0)));
        }

        AircraftTelemetrySnapshot[] retained =
            buffer.ReadAfter(null).ToArray();
        Assert.Equal(
            SimConnectConnection.FlightCriticalTelemetryCapacity,
            retained.Length);
        Assert.Equal(Epoch.AddMilliseconds(745), retained[0].Timestamp);
        Assert.Equal(Epoch.AddSeconds(1), retained[^1].Timestamp);
        Assert.All(
            retained.Zip(retained.Skip(1)),
            pair => Assert.True(
                pair.First.Timestamp < pair.Second.Timestamp));
    }

    private static AircraftTelemetrySnapshot Sample(
        double seconds,
        bool onGround = false) =>
        new(
            Epoch.AddSeconds(seconds),
            LatitudeDegrees: 32,
            LongitudeDegrees: -97,
            AltitudeMslFeet: 700,
            AltitudeAglFeet: 10,
            IndicatedAirspeedKnots: 60,
            GroundSpeedKnots: 55,
            VerticalSpeedFeetPerMinute: 0,
            HeadingDegrees: 90,
            PitchDegrees: 0,
            BankDegrees: 0,
            NormalAccelerationG: 1,
            OnGround: onGround,
            ParkingBrakeSet: false,
            EnginesRunning: 1,
            FuelTotalPounds: 500,
            PayloadPounds: 200,
            FlapsPositionPercent: 0,
            GearDown: true,
            Paused: false,
            SlewActive: false);
}
