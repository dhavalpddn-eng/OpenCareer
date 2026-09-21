using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;
using OpenCareer.SimConnect;

namespace OpenCareer.LiveProbe;

internal sealed class LiveProbeSession(
    SimConnectConnection connection,
    LiveProbeOptions options,
    LiveFlightSessionValidator? flightSessionValidator = null)
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
            if (flightSessionValidator is not null)
            {
                Console.WriteLine(
                    "FlightSession validation enabled. Start stationary on the ground; " +
                    "probe completion verification is synthetic and does not settle career rewards.");
            }

            Console.WriteLine("Press Ctrl+C to stop.");
            await WriteJsonAsync(writer, new
            {
                type = "sessionStart",
                observedAtUtc = DateTimeOffset.UtcNow,
                os = RuntimeInformation.OSDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                processId = Environment.ProcessId,
                flightSessionValidation = flightSessionValidator is not null
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

            FlightSession? finalFlightSession =
                flightSessionValidator?.Current;

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
                telemetryCleared = connection.Latest is null,
                flightSession = finalFlightSession is null
                    ? null
                    : new
                    {
                        status = finalFlightSession.Status.ToString(),
                        operationState = finalFlightSession.OperationState.ToString(),
                        trackingState = finalFlightSession.Tracking.State.ToString(),
                        finalFlightSession.Tracking.TakeoffCount,
                        finalFlightSession.Tracking.LandingEpisodeCount,
                        finalFlightSession.Tracking.BounceCount,
                        finalFlightSession.Tracking.TouchAndGoCount
                    }
            }).ConfigureAwait(false);

            Console.WriteLine(
                $"Summary: connected={_sawConnected}, telemetrySamples={_telemetrySamples}, " +
                $"reconnects={_reconnectTransitions}, finalState={connection.Current.State}.");

            if (finalFlightSession is not null)
            {
                Console.WriteLine(
                    $"FlightSession summary: status={finalFlightSession.Status} " +
                    $"operation={finalFlightSession.OperationState} " +
                    $"tracking={finalFlightSession.Tracking.State} " +
                    $"takeoffs={finalFlightSession.Tracking.TakeoffCount} " +
                    $"landings={finalFlightSession.Tracking.LandingEpisodeCount}.");
            }
        }

        return 0;
    }

    private async Task ObserveAsync(
        StreamWriter writer,
        CancellationToken cancellationToken)
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
                await RecordTelemetryAsync(
                        writer,
                        telemetry,
                        cancellationToken)
                    .ConfigureAwait(false);

                lastTelemetry = telemetry;
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RecordConnectionAsync(
        StreamWriter writer,
        SimulatorConnectionSnapshot snapshot)
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

        string simulator =
            snapshot.Simulator is null
                ? string.Empty
                : $" sim=\"{snapshot.Simulator.Name}\"";

        Console.WriteLine(
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] connection state={snapshot.State} issue={snapshot.Issue}{simulator}");
    }

    private async Task RecordTelemetryAsync(
        StreamWriter writer,
        AircraftTelemetrySnapshot telemetry,
        CancellationToken cancellationToken)
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
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] telemetry #{sequence} lat={telemetry.LatitudeDegrees:0.00000} lon={telemetry.LongitudeDegrees:0.00000} msl={telemetry.AltitudeMslFeet:0}ft agl={telemetry.AltitudeAglFeet:0}ft ias={telemetry.IndicatedAirspeedKnots:0}kt gs={telemetry.GroundSpeedKnots:0}kt vs={telemetry.VerticalSpeedFeetPerMinute:+0;-0;0}fpm hdg={telemetry.HeadingDegrees:000} ground={telemetry.OnGround} gear={telemetry.GearDown} brake={telemetry.ParkingBrakeSet} engines={telemetry.EnginesRunning} paused={telemetry.Paused} slew={telemetry.SlewActive}");
        Console.WriteLine(line);

        if (flightSessionValidator is null)
            return;

        try
        {
            IReadOnlyList<LiveFlightSessionTransition> transitions =
                await flightSessionValidator
                    .ObserveAsync(
                        telemetry,
                        cancellationToken)
                    .ConfigureAwait(false);

            foreach (LiveFlightSessionTransition transition in transitions)
            {
                await WriteJsonAsync(writer, new
                {
                    type = "flightSessionTransition",
                    observedAtUtc = DateTimeOffset.UtcNow,
                    transition.Timestamp,
                    status = transition.Status.ToString(),
                    operationState = transition.OperationState.ToString(),
                    trackingState = transition.TrackingState.ToString(),
                    transition.TakeoffCount,
                    transition.LandingEpisodeCount,
                    transition.BounceCount,
                    transition.TouchAndGoCount,
                    transition.Reason
                }).ConfigureAwait(false);

                Console.WriteLine(
                    $"*** FLIGHT SESSION: tracking={transition.TrackingState} " +
                    $"operation={transition.OperationState} status={transition.Status} " +
                    $"reason={transition.Reason}");
            }
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(writer, new
            {
                type = "flightSessionValidationError",
                observedAtUtc = DateTimeOffset.UtcNow,
                exception = ex.GetType().Name,
                message = ex.Message
            }).ConfigureAwait(false);

            Console.Error.WriteLine(
                $"*** FLIGHT SESSION VALIDATION ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task WriteJsonAsync(
        StreamWriter writer,
        object value)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        await writer.WriteLineAsync(json).ConfigureAwait(false);
    }
}
