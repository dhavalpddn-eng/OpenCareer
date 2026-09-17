using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;
using OpenCareer.SimConnect;

namespace OpenCareer.LiveProbe;

internal sealed class LiveProbeSession(SimConnectConnection connection, LiveProbeOptions options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private long _telemetrySamples;
    private int _connectionTransitions;
    private int _reconnectTransitions;
    private bool _sawConnected;

    internal async Task<int> RunAsync()
    {
        string? directory = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = new FileStream(options.OutputPath, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        await using var writer = new StreamWriter(stream) { AutoFlush = true };

        using var stop = new CancellationTokenSource();
        if (options.Duration is { } duration)
            stop.CancelAfter(duration);

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        var elapsed = Stopwatch.StartNew();

        try
        {
            Console.WriteLine($"OpenCareer live SimConnect probe. Trace: {options.OutputPath}");
            Console.WriteLine("Press Ctrl+C to stop.");
            await WriteJsonAsync(writer, new
            {
                type = "sessionStart",
                observedAtUtc = DateTimeOffset.UtcNow,
                os = RuntimeInformation.OSDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                processId = Environment.ProcessId
            }).ConfigureAwait(false);

            connection.Start();
            await ObserveAsync(writer, stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            await connection.StopAsync().ConfigureAwait(false);
            elapsed.Stop();

            await WriteJsonAsync(writer, new
            {
                type = "sessionEnd",
                observedAtUtc = DateTimeOffset.UtcNow,
                elapsedSeconds = elapsed.Elapsed.TotalSeconds,
                connectionTransitions = _connectionTransitions,
                reconnectTransitions = _reconnectTransitions,
                sawConnected = _sawConnected,
                telemetrySamples = _telemetrySamples,
                finalConnectionState = connection.Current.State.ToString(),
                finalConnectionIssue = connection.Current.Issue.ToString(),
                telemetryCleared = connection.Latest is null
            }).ConfigureAwait(false);

            Console.WriteLine(
                $"Summary: connected={_sawConnected}, telemetrySamples={_telemetrySamples}, " +
                $"reconnects={_reconnectTransitions}, finalState={connection.Current.State}.");
        }

        return 0;
    }

    private async Task ObserveAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        SimulatorConnectionSnapshot? lastConnection = null;
        AircraftTelemetrySnapshot? lastTelemetry = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SimulatorConnectionSnapshot current = connection.Current;
            if (current != lastConnection)
            {
                await RecordConnectionAsync(writer, current).ConfigureAwait(false);
                lastConnection = current;
            }

            AircraftTelemetrySnapshot? telemetry = connection.Latest;
            if (telemetry is null)
            {
                if (lastTelemetry is not null)
                {
                    await WriteJsonAsync(writer, new
                    {
                        type = "telemetryCleared",
                        observedAtUtc = DateTimeOffset.UtcNow,
                        connectionState = current.State.ToString()
                    }).ConfigureAwait(false);
                    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] telemetry cleared");
                    lastTelemetry = null;
                }
            }
            else if (telemetry != lastTelemetry)
            {
                await RecordTelemetryAsync(writer, telemetry).ConfigureAwait(false);
                lastTelemetry = telemetry;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordConnectionAsync(StreamWriter writer, SimulatorConnectionSnapshot snapshot)
    {
        _connectionTransitions++;
        _sawConnected |= snapshot.State == SimulatorConnectionState.Connected;
        if (snapshot.State == SimulatorConnectionState.Reconnecting)
            _reconnectTransitions++;

        await WriteJsonAsync(writer, new
        {
            type = "connection",
            observedAtUtc = DateTimeOffset.UtcNow,
            state = snapshot.State.ToString(),
            issue = snapshot.Issue.ToString(),
            simulator = snapshot.Simulator is null
                ? null
                : new
                {
                    snapshot.Simulator.Name,
                    applicationVersion = snapshot.Simulator.ApplicationVersion.ToString(),
                    simConnectVersion = snapshot.Simulator.SimConnectVersion.ToString()
                }
        }).ConfigureAwait(false);

        string simulator = snapshot.Simulator is null ? string.Empty : $" sim=\"{snapshot.Simulator.Name}\"";
        Console.WriteLine(
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] connection state={snapshot.State} issue={snapshot.Issue}{simulator}");
    }

    private async Task RecordTelemetryAsync(StreamWriter writer, AircraftTelemetrySnapshot telemetry)
    {
        long sequence = Interlocked.Increment(ref _telemetrySamples);

        await WriteJsonAsync(writer, new
        {
            type = "telemetry",
            sequence,
            observedAtUtc = DateTimeOffset.UtcNow,
            sampleTimestampUtc = telemetry.Timestamp,
            telemetry.LatitudeDegrees,
            telemetry.LongitudeDegrees,
            telemetry.AltitudeMslFeet,
            telemetry.AltitudeAglFeet,
            telemetry.IndicatedAirspeedKnots,
            telemetry.GroundSpeedKnots,
            telemetry.VerticalSpeedFeetPerMinute,
            telemetry.HeadingDegrees,
            telemetry.PitchDegrees,
            telemetry.BankDegrees,
            telemetry.NormalAccelerationG,
            telemetry.OnGround,
            telemetry.ParkingBrakeSet,
            telemetry.EnginesRunning,
            telemetry.FuelTotalPounds,
            telemetry.PayloadPounds,
            telemetry.FlapsPositionPercent,
            telemetry.GearDown,
            telemetry.Paused,
            telemetry.SlewActive
        }).ConfigureAwait(false);

        string line = FormattableString.Invariant(
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] telemetry #{sequence} lat={telemetry.LatitudeDegrees:0.00000} lon={telemetry.LongitudeDegrees:0.00000} msl={telemetry.AltitudeMslFeet:0}ft agl={telemetry.AltitudeAglFeet:0}ft ias={telemetry.IndicatedAirspeedKnots:0}kt gs={telemetry.GroundSpeedKnots:0}kt vs={telemetry.VerticalSpeedFeetPerMinute:+0;-0;0}fpm hdg={telemetry.HeadingDegrees:000} ground={telemetry.OnGround} paused={telemetry.Paused} slew={telemetry.SlewActive}");
        Console.WriteLine(line);
    }

    private static async Task WriteJsonAsync(StreamWriter writer, object value)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        await writer.WriteLineAsync(json).ConfigureAwait(false);
    }
}
