using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
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
    private readonly FlightEvidenceProcessor _flightEvidenceProcessor = new();
    private FlightTrackingSnapshot? _flightTracking;
    private SimulatorMissionActorHandle? _escortActor;
    private AircraftTelemetrySnapshot? _lastEscortTelemetry;
    private bool _escortSpawnAttempted;

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
                processId = Environment.ProcessId,
                sideWorkEscortTest = options.Escort is not null,
                escort = options.Escort is null
                    ? null
                    : new
                    {
                        options.Escort.ContainerTitle,
                        options.Escort.Livery,
                        options.Escort.TailNumber,
                        options.Escort.FlightNumber,
                        options.Escort.FlightPlanPath,
                        options.Escort.FlightPlanPosition,
                        options.Escort.TouchAndGo
                    }
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
            await TryRemoveEscortAsync(writer).ConfigureAwait(false);
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
                if (lastConnection?.State == SimulatorConnectionState.Connected
                    && current.State != SimulatorConnectionState.Connected)
                {
                    _escortActor = null;
                    _lastEscortTelemetry = null;
                    _escortSpawnAttempted = false;
                }

                await RecordConnectionAsync(writer, current).ConfigureAwait(false);
                lastConnection = current;
            }

            if (current.State == SimulatorConnectionState.Connected)
                await TrySpawnEscortAsync(writer, cancellationToken).ConfigureAwait(false);

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

            await RecordEscortTelemetryIfChangedAsync(writer, telemetry).ConfigureAwait(false);

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
            telemetry.SlewActive,
            telemetry.GearRetractable,
            telemetry.GearCenterPositionPercent,
            telemetry.GearLeftPositionPercent,
            telemetry.GearRightPositionPercent
        }).ConfigureAwait(false);

        string line = FormattableString.Invariant(
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] telemetry #{sequence} lat={telemetry.LatitudeDegrees:0.00000} lon={telemetry.LongitudeDegrees:0.00000} msl={telemetry.AltitudeMslFeet:0}ft agl={telemetry.AltitudeAglFeet:0}ft ias={telemetry.IndicatedAirspeedKnots:0}kt gs={telemetry.GroundSpeedKnots:0}kt vs={telemetry.VerticalSpeedFeetPerMinute:+0;-0;0}fpm hdg={telemetry.HeadingDegrees:000} ground={telemetry.OnGround} gear={telemetry.GearDown} gearRaw={telemetry.GearCenterPositionPercent:0}/{telemetry.GearLeftPositionPercent:0}/{telemetry.GearRightPositionPercent:0} retr={telemetry.GearRetractable} paused={telemetry.Paused} slew={telemetry.SlewActive}");
        Console.WriteLine(line);

        await RecordFlightEvidenceAsync(writer, telemetry).ConfigureAwait(false);
    }

    private async Task RecordFlightEvidenceAsync(
        StreamWriter writer,
        AircraftTelemetrySnapshot telemetry)
    {
        var evidence = _flightEvidenceProcessor.Process(
            connected: true,
            telemetry,
            telemetry.Timestamp);

        _flightTracking ??= FlightTrackingSnapshot.Start(evidence.Timestamp);
        FlightTrackingSnapshot previous = _flightTracking;
        FlightTrackingSnapshot next =
            FlightTrackingStateMachine.Advance(previous, evidence);
        _flightTracking = next;

        bool noteworthy =
            next.State != previous.State
            || evidence.EngineStartObserved
            || evidence.TakeoffCandidate
            || evidence.RejectedTakeoffConfirmed
            || evidence.AirborneConfirmed
            || evidence.ApproachConfirmed
            || evidence.TouchdownConfirmed
            || evidence.BounceRecontact
            || evidence.GoAroundConfirmed
            || evidence.TouchAndGoConfirmed
            || evidence.LandingRolloutConfirmed
            || evidence.ParkingConfirmed;

        if (!noteworthy)
            return;

        await WriteJsonAsync(writer, new
        {
            type = "flightEvidence",
            observedAtUtc = DateTimeOffset.UtcNow,
            sampleTimestampUtc = telemetry.Timestamp,
            previousState = previous.State.ToString(),
            state = next.State.ToString(),
            next.TakeoffCount,
            next.LandingEpisodeCount,
            next.BounceCount,
            next.TouchAndGoCount,
            next.RejectedTakeoffCount,
            evidence.StableTelemetry,
            evidence.ValidLoadedAircraft,
            evidence.EngineStartObserved,
            evidence.SelfPoweredMovementForFlight,
            evidence.TakeoffCandidate,
            evidence.RejectedTakeoffConfirmed,
            evidence.AirborneConfirmed,
            evidence.ApproachConfirmed,
            evidence.TouchdownConfirmed,
            evidence.BounceRecontact,
            evidence.GoAroundConfirmed,
            evidence.TouchAndGoConfirmed,
            evidence.LandingRolloutConfirmed,
            evidence.ParkingConfirmed
        }).ConfigureAwait(false);

        Console.WriteLine(
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] flight {previous.State} -> {next.State}" +
            $" takeoffs={next.TakeoffCount} landings={next.LandingEpisodeCount}" +
            $" bounces={next.BounceCount} rejected={next.RejectedTakeoffCount}");
    }


    private async Task TrySpawnEscortAsync(
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        if (options.Escort is null
            || _escortActor is not null
            || _escortSpawnAttempted)
        {
            return;
        }

        _escortSpawnAttempted = true;
        SimulatorMissionAircraftSpawnRequest request =
            options.Escort.ToSpawnRequest();

        await WriteJsonAsync(writer, new
        {
            type = "escortSpawnRequested",
            observedAtUtc = DateTimeOffset.UtcNow,
            request.ActorKey,
            request.ContainerTitle,
            request.Livery,
            request.TailNumber,
            request.FlightNumber,
            request.FlightPlanPath,
            request.FlightPlanPosition,
            request.TouchAndGo
        }).ConfigureAwait(false);

        Console.WriteLine(
            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] Side Work: spawning protected aircraft \"{request.ContainerTitle}\".");

        try
        {
            SimulatorMissionActorHandle actor =
                await connection
                    .SpawnEnrouteAircraftAsync(request)
                    .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                    .ConfigureAwait(false);

            _escortActor = actor;
            await WriteJsonAsync(writer, new
            {
                type = "escortSpawned",
                observedAtUtc = DateTimeOffset.UtcNow,
                actor.ActorKey,
                actor.ObjectId
            }).ConfigureAwait(false);

            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] Side Work: protected aircraft spawned objectId={actor.ObjectId}.");
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            await WriteJsonAsync(writer, new
            {
                type = "escortSpawnFailed",
                observedAtUtc = DateTimeOffset.UtcNow,
                exceptionType = ex.GetType().FullName,
                ex.Message
            }).ConfigureAwait(false);

            Console.Error.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] Side Work: protected aircraft spawn FAILED: {ex.Message}");
        }
    }

    private async Task RecordEscortTelemetryIfChangedAsync(
        StreamWriter writer,
        AircraftTelemetrySnapshot? player)
    {
        if (_escortActor is null)
            return;

        SimulatorMissionActorSnapshot? actor =
            connection.Actors.FirstOrDefault(
                candidate =>
                    candidate.Handle.ObjectId == _escortActor.ObjectId
                    && string.Equals(
                        candidate.Handle.ActorKey,
                        _escortActor.ActorKey,
                        StringComparison.Ordinal));

        AircraftTelemetrySnapshot? protectedAircraft =
            actor?.Telemetry;

        if (protectedAircraft is null
            || protectedAircraft == _lastEscortTelemetry)
        {
            return;
        }

        _lastEscortTelemetry = protectedAircraft;

        double? horizontalSeparationNm = null;
        double? verticalSeparationFeet = null;
        double? groundSpeedDifferenceKnots = null;

        if (player is not null)
        {
            horizontalSeparationNm = DistanceNauticalMiles(
                player.LatitudeDegrees,
                player.LongitudeDegrees,
                protectedAircraft.LatitudeDegrees,
                protectedAircraft.LongitudeDegrees);
            verticalSeparationFeet =
                Math.Abs(
                    player.AltitudeMslFeet
                    - protectedAircraft.AltitudeMslFeet);
            groundSpeedDifferenceKnots =
                Math.Abs(
                    player.GroundSpeedKnots
                    - protectedAircraft.GroundSpeedKnots);
        }

        await WriteJsonAsync(writer, new
        {
            type = "escortTelemetry",
            observedAtUtc = DateTimeOffset.UtcNow,
            actorKey = actor!.Handle.ActorKey,
            actorObjectId = actor.Handle.ObjectId,
            sampleTimestampUtc = protectedAircraft.Timestamp,
            protectedAircraft.LatitudeDegrees,
            protectedAircraft.LongitudeDegrees,
            protectedAircraft.AltitudeMslFeet,
            protectedAircraft.AltitudeAglFeet,
            protectedAircraft.IndicatedAirspeedKnots,
            protectedAircraft.GroundSpeedKnots,
            protectedAircraft.VerticalSpeedFeetPerMinute,
            protectedAircraft.HeadingDegrees,
            protectedAircraft.OnGround,
            protectedAircraft.GearDown,
            protectedAircraft.GearRetractable,
            protectedAircraft.GearCenterPositionPercent,
            protectedAircraft.GearLeftPositionPercent,
            protectedAircraft.GearRightPositionPercent,
            horizontalSeparationNm,
            verticalSeparationFeet,
            groundSpeedDifferenceKnots
        }).ConfigureAwait(false);

        string separation = horizontalSeparationNm.HasValue
            ? FormattableString.Invariant(
                $" sep={horizontalSeparationNm.Value:0.00}nm/{verticalSeparationFeet!.Value:0}ft")
            : string.Empty;

        Console.WriteLine(
            FormattableString.Invariant(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] escort id={actor.Handle.ObjectId} " +
                $"lat={protectedAircraft.LatitudeDegrees:0.00000} lon={protectedAircraft.LongitudeDegrees:0.00000} " +
                $"agl={protectedAircraft.AltitudeAglFeet:0}ft gs={protectedAircraft.GroundSpeedKnots:0}kt " +
                $"ground={protectedAircraft.OnGround}{separation}"));
    }

    private async Task TryRemoveEscortAsync(StreamWriter writer)
    {
        if (_escortActor is null)
            return;

        SimulatorMissionActorHandle actor = _escortActor;
        _escortActor = null;

        if (connection.Current.State != SimulatorConnectionState.Connected)
            return;

        try
        {
            await connection
                .RemoveActorAsync(actor)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            await WriteJsonAsync(writer, new
            {
                type = "escortRemoved",
                observedAtUtc = DateTimeOffset.UtcNow,
                actor.ActorKey,
                actor.ObjectId
            }).ConfigureAwait(false);

            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] Side Work: protected aircraft removed.");
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(writer, new
            {
                type = "escortRemoveFailed",
                observedAtUtc = DateTimeOffset.UtcNow,
                actor.ActorKey,
                actor.ObjectId,
                exceptionType = ex.GetType().FullName,
                ex.Message
            }).ConfigureAwait(false);

            Console.Error.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] Side Work: protected aircraft cleanup failed: {ex.Message}");
        }
    }

    private static double DistanceNauticalMiles(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB)
    {
        const double EarthRadiusNauticalMiles = 3_440.065;

        static double ToRadians(double degrees) =>
            degrees * Math.PI / 180d;

        double lat1 = ToRadians(latitudeA);
        double lat2 = ToRadians(latitudeB);
        double deltaLat = ToRadians(latitudeB - latitudeA);
        double deltaLon = ToRadians(longitudeB - longitudeA);

        double a =
            Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(lat1)
            * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2)
            * Math.Sin(deltaLon / 2);

        double centralAngle =
            2 * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(Math.Max(0, 1 - a)));

        return EarthRadiusNauticalMiles * centralAngle;
    }

    private static async Task WriteJsonAsync(StreamWriter writer, object value)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        await writer.WriteLineAsync(json).ConfigureAwait(false);
    }
}
