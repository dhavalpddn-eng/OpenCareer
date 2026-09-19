using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;
using OpenCareer.SimConnect.Native;
using Polly;
using Polly.Retry;

namespace OpenCareer.SimConnect;

public sealed class SimConnectConnection :
    ISimulatorConnection,
    ISimulatorTelemetrySource,
    ISimulatorMissionActorService
{
    private const uint ActorRequestIdBase = 0x4F440000;

    private readonly object _lifecycleGate = new();
    private readonly ISimConnectApi _api;
    private readonly ILogger<SimConnectConnection> _logger;
    private readonly SimConnectConnectionOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentQueue<MissionActorCommand> _missionActorCommands = new();
    private readonly AutoResetEvent _missionActorSignal = new(false);
    private SimulatorConnectionSnapshot _current =
        new(SimulatorConnectionState.Disconnected);
    private AircraftTelemetrySnapshot? _latestTelemetry;
    private SimulatorMissionActorSnapshot[] _actors = [];
    private CancellationTokenSource? _stop;
    private Task _worker = Task.CompletedTask;
    private bool _disposed;
    private bool _missionActorSignalDisposed;

    public SimConnectConnection(ILogger<SimConnectConnection> logger)
        : this(new NativeSimConnectApi(), logger, new(), TimeProvider.System)
    {
    }

    internal SimConnectConnection(
        ISimConnectApi api,
        ILogger<SimConnectConnection> logger,
        SimConnectConnectionOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();
        _api = api;
        _logger = logger;
        _options = options;
        _clock = clock;
    }

    public SimulatorConnectionSnapshot Current => Volatile.Read(ref _current);

    public AircraftTelemetrySnapshot? Latest =>
        Volatile.Read(ref _latestTelemetry);

    public IReadOnlyList<SimulatorMissionActorSnapshot> Actors =>
        Volatile.Read(ref _actors);

    public void Start()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_worker.IsCompleted)
                return;

            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            CancellationToken token = _stop.Token;
            _worker = Task.Factory.StartNew(
                () => Run(token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    public Task StopAsync()
    {
        lock (_lifecycleGate)
        {
            _stop?.Cancel();
            return _worker;
        }
    }

    public Task<SimulatorMissionActorHandle> SpawnEnrouteAircraftAsync(
        SimulatorMissionAircraftSpawnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var completion =
            new TaskCompletionSource<SimulatorMissionActorHandle>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        QueueMissionActorCommand(
            new SpawnMissionActorCommand(request, completion));
        return completion.Task;
    }

    public Task RemoveActorAsync(SimulatorMissionActorHandle actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor.ActorKey);

        if (actor.ObjectId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actor),
                "Mission actor object ID must be non-zero.");
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        QueueMissionActorCommand(
            new RemoveMissionActorCommand(actor, completion));
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        Task worker;
        lock (_lifecycleGate)
        {
            _disposed = true;
            _stop?.Cancel();
            worker = _worker;
        }

        await worker.ConfigureAwait(false);

        lock (_lifecycleGate)
        {
            _stop?.Dispose();
            _stop = null;

            if (!_missionActorSignalDisposed)
            {
                _missionActorSignal.Dispose();
                _missionActorSignalDisposed = true;
            }
        }
    }

    private void QueueMissionActorCommand(MissionActorCommand command)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (Current.State != SimulatorConnectionState.Connected)
            {
                throw new InvalidOperationException(
                    "Mission actors require an active simulator connection.");
            }

            _missionActorCommands.Enqueue(command);
            _missionActorSignal.Set();
        }
    }

    private void Run(CancellationToken token)
    {
        bool everConnected = false;
        ResiliencePipeline<ConnectionAttemptResult> reconnectPipeline =
            CreateReconnectPipeline();

        PublishTelemetry(null);
        PublishActors([]);
        Publish(new(SimulatorConnectionState.WaitingForSimulator));

        try
        {
            while (!token.IsCancellationRequested)
            {
                ConnectionAttemptResult attempt =
                    reconnectPipeline.Execute(
                        cancellationToken =>
                        {
                            ConnectionAttemptResult result =
                                RunConnectionAttempt(
                                    cancellationToken,
                                    everConnected);
                            everConnected = result.EverConnected;
                            return result;
                        },
                        token);

                if (token.IsCancellationRequested)
                    break;

                if (attempt.ConnectedThisSession)
                {
                    TimeSpan delay =
                        attempt.Issue
                            == SimulatorConnectionIssue.VersionMismatch
                            ? _options.RuntimeRetryDelay
                            : _options.InitialRetryDelay;

                    if (token.WaitHandle.WaitOne(delay))
                        break;
                }
            }
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            PublishTelemetry(null);
            PublishActors([]);
            FailQueuedActorCommands(
                new InvalidOperationException(
                    "Simulator connection worker stopped unexpectedly.",
                    ex));
            _logger.LogError(
                ex,
                "SimConnect connection worker stopped unexpectedly.");
            Publish(new(
                SimulatorConnectionState.Faulted,
                SimulatorConnectionIssue.UnexpectedError));
        }
        finally
        {
            PublishTelemetry(null);
            PublishActors([]);
            FailQueuedActorCommands(
                new InvalidOperationException(
                    "Simulator connection is no longer active."));

            if (token.IsCancellationRequested)
                Publish(new(SimulatorConnectionState.Disconnected));
        }
    }

    private ResiliencePipeline<ConnectionAttemptResult>
        CreateReconnectPipeline()
    {
        var retry = new RetryStrategyOptions<ConnectionAttemptResult>
        {
            ShouldHandle =
                new PredicateBuilder<ConnectionAttemptResult>()
                    .HandleResult(static result =>
                        !result.ConnectedThisSession
                        && result.Issue
                            != SimulatorConnectionIssue.None)
                    .Handle<DllNotFoundException>()
                    .Handle<BadImageFormatException>()
                    .Handle<EntryPointNotFoundException>()
                    .Handle<PlatformNotSupportedException>(),
            Delay = _options.InitialRetryDelay,
            MaxDelay = _options.MaximumRetryDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            MaxRetryAttempts = int.MaxValue,
            DelayGenerator = args =>
            {
                if (IsRuntimeFailure(args.Outcome.Exception)
                    || (args.Outcome.Exception is null
                        && args.Outcome.Result.Issue
                            == SimulatorConnectionIssue.VersionMismatch))
                {
                    return new ValueTask<TimeSpan?>(
                        _options.RuntimeRetryDelay);
                }

                return new ValueTask<TimeSpan?>((TimeSpan?)null);
            }
        };

        return new ResiliencePipelineBuilder<ConnectionAttemptResult>()
            .AddRetry(retry)
            .Build();
    }

    private ConnectionAttemptResult RunConnectionAttempt(
        CancellationToken token,
        bool everConnected)
    {
        try
        {
            SimulatorConnectionIssue issue =
                RunSession(
                    token,
                    out bool connectedThisSession);

            bool hasEverConnected =
                everConnected || connectedThisSession;

            PublishTelemetry(null);
            PublishActors([]);

            if (!token.IsCancellationRequested)
            {
                bool incompatible =
                    issue == SimulatorConnectionIssue.VersionMismatch;

                Publish(new(
                    incompatible
                        ? SimulatorConnectionState.Unavailable
                        : hasEverConnected
                            ? SimulatorConnectionState.Reconnecting
                            : SimulatorConnectionState.WaitingForSimulator,
                    issue));

                FailQueuedActorCommands(
                    new InvalidOperationException(
                        "Simulator connection changed before the mission-actor command was processed."));
            }

            return new ConnectionAttemptResult(
                issue,
                connectedThisSession,
                hasEverConnected);
        }
        catch (Exception ex)
            when (IsRuntimeFailure(ex))
        {
            PublishTelemetry(null);
            PublishActors([]);
            FailQueuedActorCommands(
                new InvalidOperationException(
                    "SimConnect runtime is unavailable.",
                    ex));

            SimulatorConnectionIssue issue =
                MapRuntimeIssue(ex);

            Publish(new(
                SimulatorConnectionState.Unavailable,
                issue));

            _logger.LogWarning(
                ex,
                "SimConnect runtime unavailable: {ExceptionType}: {Message}",
                ex.GetType().FullName,
                ex.Message);

            throw;
        }
    }

    private static bool IsRuntimeFailure(Exception? exception) =>
        exception is
            DllNotFoundException
            or BadImageFormatException
            or EntryPointNotFoundException
            or PlatformNotSupportedException;

    private static SimulatorConnectionIssue MapRuntimeIssue(
        Exception exception) =>
        exception switch
        {
            DllNotFoundException =>
                SimulatorConnectionIssue.RuntimeMissing,
            PlatformNotSupportedException =>
                SimulatorConnectionIssue.UnsupportedPlatform,
            _ => SimulatorConnectionIssue.RuntimeIncompatible
        };

    private SimulatorConnectionIssue RunSession(
        CancellationToken token,
        out bool connectedThisSession)
    {
        connectedThisSession = false;
        using var notification = new AutoResetEvent(false);
        WaitHandle[] waits =
            [token.WaitHandle, notification, _missionActorSignal];
        nint handle = nint.Zero;
        var pendingActorCreates =
            new Dictionary<uint, SpawnMissionActorCommand>();
        var actorTelemetryRequests = new Dictionary<uint, uint>();
        var actorState =
            new Dictionary<uint, SimulatorMissionActorSnapshot>();
        uint nextActorRequestId = ActorRequestIdBase;

        try
        {
            int result = _api.Open(
                out handle,
                notification.SafeWaitHandle.DangerousGetHandle());
            if (result < 0 || handle == nint.Zero)
            {
                _logger.LogDebug(
                    "SimConnect open failed with HRESULT {HResult:X8}.",
                    result);
                return SimulatorConnectionIssue.ConnectionFailed;
            }

            if (token.IsCancellationRequested)
                return SimulatorConnectionIssue.None;

            Publish(new(SimulatorConnectionState.Connecting));
            long openedAt = _clock.GetTimestamp();
            long lastHeartbeatAt = openedAt;
            uint nextRequestId = 1;
            uint? pendingHeartbeat = null;
            bool acknowledged = false;
            bool paused = false;
            var messages = new List<SimConnectMessage>();
            Exception? callbackError = null;
            DispatchCallback callback = (data, size, _) =>
            {
                try
                {
                    messages.Add(
                        SimConnectMessageDecoder.Decode(data, size));
                }
                catch (Exception ex)
                {
                    callbackError ??= ex;
                }
            };

            while (!token.IsCancellationRequested)
            {
                messages.Clear();
                result = _api.CallDispatch(handle, callback);
                if (callbackError is not null)
                {
                    _logger.LogWarning(
                        callbackError,
                        "Invalid SimConnect response; reopening connection.");
                    return SimulatorConnectionIssue.InvalidResponse;
                }

                if (result < 0
                    && (result != unchecked((int)0x80004005)
                        || messages.Count != 0))
                {
                    _logger.LogDebug(
                        "SimConnect dispatch failed with HRESULT {HResult:X8}.",
                        result);
                    return SimulatorConnectionIssue.ConnectionLost;
                }

                if (token.IsCancellationRequested)
                    return SimulatorConnectionIssue.None;

                foreach (var message in messages)
                {
                    switch (message.Kind)
                    {
                        case SimConnectMessageKind.Open
                            when !acknowledged:
                            if (!ConfigureTelemetry(handle))
                                return SimulatorConnectionIssue.SimulatorError;

                            acknowledged = true;
                            connectedThisSession = true;
                            lastHeartbeatAt = _clock.GetTimestamp();
                            Publish(new(
                                SimulatorConnectionState.Connected,
                                Simulator: message.Simulator));
                            break;

                        case SimConnectMessageKind.Quit:
                            return SimulatorConnectionIssue.ConnectionLost;

                        case SimConnectMessageKind.Event
                            when acknowledged
                                && message.EventId
                                    == SimConnectTelemetryDefinition.PauseEventId:
                            paused = message.EventData != 0;
                            if (Latest is { } currentTelemetry)
                            {
                                PublishTelemetry(
                                    currentTelemetry with
                                    {
                                        Timestamp = _clock.GetUtcNow(),
                                        Paused = paused
                                    });
                            }

                            UpdateActorPauseState(
                                actorState,
                                paused);
                            break;

                        case SimConnectMessageKind.SimObjectData
                            when acknowledged:
                            if (message.RequestId
                                    == SimConnectTelemetryDefinition.RequestId
                                && message.DefinitionId
                                    == SimConnectTelemetryDefinition.DefinitionId)
                            {
                                var telemetry =
                                    SimConnectTelemetryMapper.Map(
                                        message.Data,
                                        _clock.GetUtcNow(),
                                        paused);
                                if (telemetry is not null)
                                    PublishTelemetry(telemetry);
                                else
                                    _logger.LogDebug(
                                        "Ignored invalid aircraft telemetry packet.");
                                break;
                            }

                            if (message.DefinitionId
                                    == SimConnectTelemetryDefinition.DefinitionId
                                && actorTelemetryRequests.TryGetValue(
                                    message.RequestId,
                                    out uint actorObjectId)
                                && actorObjectId == message.ObjectId
                                && actorState.TryGetValue(
                                    actorObjectId,
                                    out var actor))
                            {
                                var actorTelemetry =
                                    SimConnectTelemetryMapper.Map(
                                        message.Data,
                                        _clock.GetUtcNow(),
                                        paused);
                                if (actorTelemetry is not null)
                                {
                                    actorState[actorObjectId] =
                                        actor with
                                        {
                                            Telemetry = actorTelemetry,
                                            UpdatedAt = actorTelemetry.Timestamp
                                        };
                                    PublishActors(actorState.Values);
                                }
                                else
                                {
                                    _logger.LogDebug(
                                        "Ignored invalid mission-actor telemetry packet for object {ObjectId}.",
                                        actorObjectId);
                                }
                            }
                            break;

                        case SimConnectMessageKind.AssignedObjectId
                            when acknowledged:
                            if (pendingActorCreates.Remove(
                                message.RequestId,
                                out var createCommand))
                            {
                                CompleteActorCreation(
                                    handle,
                                    createCommand,
                                    message.ObjectId,
                                    ref nextActorRequestId,
                                    actorTelemetryRequests,
                                    actorState);
                            }
                            break;

                        case SimConnectMessageKind.SystemState
                            when pendingHeartbeat == message.RequestId:
                            pendingHeartbeat = null;
                            break;

                        case SimConnectMessageKind.Exception:
                            _logger.LogWarning(
                                "SimConnect exception {Code}, send {SendId}, parameter {Index}.",
                                message.ExceptionCode,
                                message.SendId,
                                message.ParameterIndex);
                            return message.ExceptionCode == 5
                                ? SimulatorConnectionIssue.VersionMismatch
                                : SimulatorConnectionIssue.SimulatorError;
                    }
                }

                if (!acknowledged
                    && _clock.GetElapsedTime(openedAt)
                        >= _options.HandshakeTimeout)
                {
                    return SimulatorConnectionIssue.HandshakeTimeout;
                }

                if (acknowledged)
                {
                    DrainMissionActorCommands(
                        handle,
                        ref nextActorRequestId,
                        pendingActorCreates,
                        actorTelemetryRequests,
                        actorState);

                    if (pendingHeartbeat.HasValue
                        && _clock.GetElapsedTime(lastHeartbeatAt)
                            >= _options.HeartbeatTimeout)
                    {
                        return SimulatorConnectionIssue.ResponseTimeout;
                    }

                    if (!pendingHeartbeat.HasValue
                        && _clock.GetElapsedTime(lastHeartbeatAt)
                            >= _options.HeartbeatInterval)
                    {
                        pendingHeartbeat = nextRequestId++;
                        result = _api.RequestSystemState(
                            handle,
                            pendingHeartbeat.Value);
                        if (result < 0)
                            return SimulatorConnectionIssue.ConnectionLost;
                        lastHeartbeatAt = _clock.GetTimestamp();
                    }
                }

                if (WaitHandle.WaitAny(
                    waits,
                    _options.DispatchInterval) == 0)
                {
                    break;
                }
            }

            return SimulatorConnectionIssue.None;
        }
        finally
        {
            var ended = new InvalidOperationException(
                "Simulator connection ended before the mission-actor operation completed.");

            foreach (var pending in pendingActorCreates.Values)
                pending.Fail(ended);

            FailQueuedActorCommands(ended);

            if (handle != nint.Zero)
            {
                foreach (uint objectId in actorState.Keys.ToArray())
                    TryRemoveActor(
                        handle,
                        objectId,
                        ref nextActorRequestId);

                Close(handle);
            }

            PublishActors([]);
        }
    }

    private void DrainMissionActorCommands(
        nint handle,
        ref uint nextActorRequestId,
        IDictionary<uint, SpawnMissionActorCommand> pendingCreates,
        IDictionary<uint, uint> actorTelemetryRequests,
        IDictionary<uint, SimulatorMissionActorSnapshot> actorState)
    {
        while (_missionActorCommands.TryDequeue(out var command))
        {
            switch (command)
            {
                case SpawnMissionActorCommand spawn:
                    if (actorState.Values.Any(actor =>
                            string.Equals(
                                actor.Handle.ActorKey,
                                spawn.Request.ActorKey,
                                StringComparison.OrdinalIgnoreCase))
                        || pendingCreates.Values.Any(pending =>
                            string.Equals(
                                pending.Request.ActorKey,
                                spawn.Request.ActorKey,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        spawn.Fail(
                            new InvalidOperationException(
                                $"Mission actor key '{spawn.Request.ActorKey}' is already active."));
                        break;
                    }

                    uint createRequestId =
                        NextActorRequestId(ref nextActorRequestId);
                    int createResult =
                        _api.CreateEnrouteAtcAircraftEx1(
                            handle,
                            spawn.Request.ContainerTitle,
                            spawn.Request.Livery,
                            spawn.Request.TailNumber,
                            spawn.Request.FlightNumber,
                            spawn.Request.FlightPlanPath,
                            spawn.Request.FlightPlanPosition,
                            spawn.Request.TouchAndGo,
                            createRequestId);

                    if (createResult < 0)
                    {
                        spawn.Fail(
                            new InvalidOperationException(
                                $"SimConnect rejected mission-actor creation with HRESULT {createResult:X8}."));
                        break;
                    }

                    pendingCreates.Add(
                        createRequestId,
                        spawn);
                    break;

                case RemoveMissionActorCommand remove:
                    if (!actorState.TryGetValue(
                            remove.Actor.ObjectId,
                            out var existing)
                        || !string.Equals(
                            existing.Handle.ActorKey,
                            remove.Actor.ActorKey,
                            StringComparison.Ordinal))
                    {
                        remove.Fail(
                            new InvalidOperationException(
                                "Mission actor is not owned by the active simulator session."));
                        break;
                    }

                    uint removeRequestId =
                        NextActorRequestId(ref nextActorRequestId);
                    int removeResult = _api.RemoveObject(
                        handle,
                        remove.Actor.ObjectId,
                        removeRequestId);

                    if (removeResult < 0)
                    {
                        remove.Fail(
                            new InvalidOperationException(
                                $"SimConnect rejected mission-actor removal with HRESULT {removeResult:X8}."));
                        break;
                    }

                    actorState.Remove(remove.Actor.ObjectId);
                    foreach (uint telemetryRequestId
                        in actorTelemetryRequests
                            .Where(pair =>
                                pair.Value == remove.Actor.ObjectId)
                            .Select(pair => pair.Key)
                            .ToArray())
                    {
                        actorTelemetryRequests.Remove(
                            telemetryRequestId);
                    }

                    PublishActors(actorState.Values);
                    remove.Complete();
                    break;
            }
        }
    }

    private void CompleteActorCreation(
        nint handle,
        SpawnMissionActorCommand command,
        uint objectId,
        ref uint nextActorRequestId,
        IDictionary<uint, uint> actorTelemetryRequests,
        IDictionary<uint, SimulatorMissionActorSnapshot> actorState)
    {
        if (objectId == 0)
        {
            command.Fail(
                new InvalidOperationException(
                    "SimConnect returned an invalid mission-actor object ID."));
            return;
        }

        uint telemetryRequestId =
            NextActorRequestId(ref nextActorRequestId);
        int telemetryResult = _api.RequestDataOnObject(
            handle,
            telemetryRequestId,
            SimConnectTelemetryDefinition.DefinitionId,
            objectId,
            SimConnectPeriod.Second);

        if (telemetryResult < 0)
        {
            TryRemoveActor(
                handle,
                objectId,
                ref nextActorRequestId);
            command.Fail(
                new InvalidOperationException(
                    $"SimConnect rejected mission-actor telemetry with HRESULT {telemetryResult:X8}."));
            return;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        var actorHandle = new SimulatorMissionActorHandle(
            command.Request.ActorKey,
            objectId);
        actorState.Add(
            objectId,
            new SimulatorMissionActorSnapshot(
                actorHandle,
                Telemetry: null,
                CreatedAt: now,
                UpdatedAt: now));
        actorTelemetryRequests.Add(
            telemetryRequestId,
            objectId);
        PublishActors(actorState.Values);
        command.Complete(actorHandle);
    }

    private void UpdateActorPauseState(
        IDictionary<uint, SimulatorMissionActorSnapshot> actorState,
        bool paused)
    {
        bool changed = false;
        DateTimeOffset now = _clock.GetUtcNow();

        foreach (uint objectId in actorState.Keys.ToArray())
        {
            var actor = actorState[objectId];
            if (actor.Telemetry is not { } telemetry)
                continue;

            actorState[objectId] =
                actor with
                {
                    Telemetry = telemetry with
                    {
                        Timestamp = now,
                        Paused = paused
                    },
                    UpdatedAt = now
                };
            changed = true;
        }

        if (changed)
            PublishActors(actorState.Values);
    }

    private bool ConfigureTelemetry(nint handle)
    {
        foreach (var datum in SimConnectTelemetryDefinition.Data)
        {
            int result = _api.AddToDataDefinition(
                handle,
                SimConnectTelemetryDefinition.DefinitionId,
                datum.Name,
                datum.Units);
            if (result < 0)
            {
                _logger.LogWarning(
                    "SimConnect rejected telemetry definition {Datum} with HRESULT {HResult:X8}.",
                    datum.Name,
                    result);
                return false;
            }
        }

        int subscribeResult = _api.SubscribeToSystemEvent(
            handle,
            SimConnectTelemetryDefinition.PauseEventId,
            "Pause_EX1");
        if (subscribeResult < 0)
        {
            _logger.LogWarning(
                "SimConnect rejected Pause_EX1 subscription with HRESULT {HResult:X8}.",
                subscribeResult);
            return false;
        }

        int requestResult = _api.RequestDataOnUserAircraft(
            handle,
            SimConnectTelemetryDefinition.RequestId,
            SimConnectTelemetryDefinition.DefinitionId,
            SimConnectPeriod.Second);
        if (requestResult < 0)
        {
            _logger.LogWarning(
                "SimConnect rejected aircraft telemetry request with HRESULT {HResult:X8}.",
                requestResult);
            return false;
        }

        return true;
    }

    private void TryRemoveActor(
        nint handle,
        uint objectId,
        ref uint nextActorRequestId)
    {
        try
        {
            uint requestId =
                NextActorRequestId(ref nextActorRequestId);
            int result = _api.RemoveObject(
                handle,
                objectId,
                requestId);
            if (result < 0)
            {
                _logger.LogDebug(
                    "SimConnect mission-actor cleanup returned HRESULT {HResult:X8} for object {ObjectId}.",
                    result,
                    objectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Mission-actor cleanup failed for object {ObjectId}.",
                objectId);
        }
    }

    private void Close(nint handle)
    {
        try
        {
            int result = _api.Close(handle);
            if (result < 0)
            {
                _logger.LogWarning(
                    "SimConnect close returned HRESULT {HResult:X8}.",
                    result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SimConnect close failed after connection loss.");
        }
    }

    private static uint NextActorRequestId(ref uint next)
    {
        uint requestId = next;
        next = next == uint.MaxValue
            ? ActorRequestIdBase
            : next + 1;
        return requestId;
    }

    private void FailQueuedActorCommands(Exception exception)
    {
        while (_missionActorCommands.TryDequeue(out var command))
            command.Fail(exception);
    }

    private void Publish(SimulatorConnectionSnapshot snapshot)
    {
        if (Current == snapshot)
            return;

        Volatile.Write(ref _current, snapshot);
        _logger.LogInformation(
            "Simulator connection: {State}; issue: {Issue}.",
            snapshot.State,
            snapshot.Issue);
    }

    private void PublishTelemetry(
        AircraftTelemetrySnapshot? snapshot) =>
        Volatile.Write(ref _latestTelemetry, snapshot);

    private void PublishActors(
        IEnumerable<SimulatorMissionActorSnapshot> snapshots)
    {
        var next = snapshots
            .OrderBy(
                actor => actor.Handle.ActorKey,
                StringComparer.Ordinal)
            .ToArray();
        Volatile.Write(ref _actors, next);
    }

    private readonly record struct ConnectionAttemptResult(
        SimulatorConnectionIssue Issue,
        bool ConnectedThisSession,
        bool EverConnected);

    private abstract record MissionActorCommand
    {
        internal abstract void Fail(Exception exception);
    }

    private sealed record SpawnMissionActorCommand(
        SimulatorMissionAircraftSpawnRequest Request,
        TaskCompletionSource<SimulatorMissionActorHandle> Completion)
        : MissionActorCommand
    {
        internal void Complete(
            SimulatorMissionActorHandle actor) =>
            Completion.TrySetResult(actor);

        internal override void Fail(Exception exception) =>
            Completion.TrySetException(exception);
    }

    private sealed record RemoveMissionActorCommand(
        SimulatorMissionActorHandle Actor,
        TaskCompletionSource<bool> Completion)
        : MissionActorCommand
    {
        internal void Complete() =>
            Completion.TrySetResult(true);

        internal override void Fail(Exception exception) =>
            Completion.TrySetException(exception);
    }
}
