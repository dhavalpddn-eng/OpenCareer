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

    private static AircraftTelemetrySnapshot Sample(double seconds) =>
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
            OnGround: false,
            ParkingBrakeSet: false,
            EnginesRunning: 1,
            FuelTotalPounds: 500,
            PayloadPounds: 200,
            FlapsPositionPercent: 0,
            GearDown: true,
            Paused: false,
            SlewActive: false);
}
