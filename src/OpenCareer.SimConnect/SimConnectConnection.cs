using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.SimConnect;

public sealed class SimConnectConnection : ISimulatorConnection, ISimulatorTelemetrySource
{
    private readonly object _lifecycleGate = new();
    private readonly ISimConnectApi _api;
    private readonly ILogger<SimConnectConnection> _logger;
    private readonly SimConnectConnectionOptions _options;
    private readonly TimeProvider _clock;
    private SimulatorConnectionSnapshot _current = new(SimulatorConnectionState.Disconnected);
    private AircraftTelemetrySnapshot? _latestTelemetry;
    private SimConnectAircraftCatalogSnapshot _aircraftCatalog = SimConnectAircraftCatalogSnapshot.Unavailable;
    private readonly ConcurrentQueue<SimConnectAirportFacilityQuery> _airportFacilityQueries = new();
    private CancellationTokenSource? _stop;
    private Task _worker = Task.CompletedTask;
    private bool _disposed;

    public SimConnectConnection(ILogger<SimConnectConnection> logger)
        : this(new NativeSimConnectApi(), logger, new(), TimeProvider.System) { }

    internal SimConnectConnection(ISimConnectApi api, ILogger<SimConnectConnection> logger,
        SimConnectConnectionOptions options, TimeProvider clock)
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
    public AircraftTelemetrySnapshot? Latest => Volatile.Read(ref _latestTelemetry);
    internal SimConnectAircraftCatalogSnapshot AircraftCatalog => Volatile.Read(ref _aircraftCatalog);

    internal async Task<SimConnectAirportFacilitySnapshot?> RequestAirportFacilityAsync(
        string icao,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        if (Current.State != SimulatorConnectionState.Connected)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var query = new SimConnectAirportFacilityQuery(
            icao.Trim().ToUpperInvariant(),
            cancellationToken);

        _airportFacilityQueries.Enqueue(query);

        return await query.Completion.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

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
            // A dedicated thread keeps native calls serialized and never occupies the UI thread.
            _worker = Task.Factory.StartNew(() => Run(token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
        }
    }

    private void Run(CancellationToken token)
    {
        bool everConnected = false;
        int failures = 0;
        PublishTelemetry(null);
        PublishAircraftCatalog(SimConnectAircraftCatalogSnapshot.Unavailable);
        Publish(new(SimulatorConnectionState.WaitingForSimulator));
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    var issue = RunSession(token, ref everConnected, ref failures);
                    PublishTelemetry(null);
                    PublishAircraftCatalog(SimConnectAircraftCatalogSnapshot.Unavailable);
                    if (token.IsCancellationRequested)
                        break;

                    bool incompatible = issue == SimulatorConnectionIssue.VersionMismatch;
                    Publish(new(incompatible ? SimulatorConnectionState.Unavailable :
                        everConnected ? SimulatorConnectionState.Reconnecting : SimulatorConnectionState.WaitingForSimulator, issue));
                    failures = Math.Min(failures + 1, 31);
                    delay = incompatible ? _options.RuntimeRetryDelay : _options.RetryDelay(failures);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
                    or EntryPointNotFoundException or PlatformNotSupportedException)
                {
                    PublishTelemetry(null);
                    PublishAircraftCatalog(SimConnectAircraftCatalogSnapshot.Unavailable);
                    var issue = ex switch
                    {
                        DllNotFoundException => SimulatorConnectionIssue.RuntimeMissing,
                        PlatformNotSupportedException => SimulatorConnectionIssue.UnsupportedPlatform,
                        _ => SimulatorConnectionIssue.RuntimeIncompatible
                    };
                    Publish(new(SimulatorConnectionState.Unavailable, issue));
                    _logger.LogDebug(ex, "SimConnect runtime unavailable.");
                    delay = _options.RuntimeRetryDelay;
                }

                // Cancellation interrupts backoff immediately; it never creates another connection.
                if (token.WaitHandle.WaitOne(delay))
                    break;
            }
        }
        catch (Exception ex)
        {
            PublishTelemetry(null);
            PublishAircraftCatalog(SimConnectAircraftCatalogSnapshot.Unavailable);
            _logger.LogError(ex, "SimConnect connection worker stopped unexpectedly.");
            Publish(new(SimulatorConnectionState.Faulted, SimulatorConnectionIssue.UnexpectedError));
        }
        finally
        {
            PublishTelemetry(null);
            PublishAircraftCatalog(SimConnectAircraftCatalogSnapshot.Unavailable);
            if (token.IsCancellationRequested)
                Publish(new(SimulatorConnectionState.Disconnected));
        }
    }

    private SimulatorConnectionIssue RunSession(CancellationToken token, ref bool everConnected, ref int failures)
    {
        using var notification = new AutoResetEvent(false);
        WaitHandle[] waits = [token.WaitHandle, notification];
        nint handle = nint.Zero;
        ActiveSimConnectAirportFacilityRequest? activeAirportFacilityRequest = null;
        try
        {
            int result = _api.Open(out handle, notification.SafeWaitHandle.DangerousGetHandle());
            if (result < 0 || handle == nint.Zero)
            {
                _logger.LogDebug("SimConnect open failed with HRESULT {HResult:X8}.", result);
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
            bool airportFacilityConfigured = false;
            uint nextAirportFacilityRequestId = SimConnectAirportFacilityDefinition.FirstRequestId;
            uint? aircraftCatalogPageCount = null;
            var aircraftCatalogPages = new Dictionary<uint, IReadOnlyList<SimConnectObjectLivery>>();
            var messages = new List<SimConnectMessage>();
            Exception? callbackError = null;
            DispatchCallback callback = (data, size, _) =>
            {
                // No managed exception may cross the unmanaged callback boundary.
                try { messages.Add(SimConnectMessageDecoder.Decode(data, size)); }
                catch (Exception ex) { callbackError ??= ex; }
            };

            while (!token.IsCancellationRequested)
            {
                messages.Clear();
                result = _api.CallDispatch(handle, callback);
                if (callbackError is not null)
                {
                    _logger.LogWarning(callbackError, "Invalid SimConnect response; reopening connection.");
                    return SimulatorConnectionIssue.InvalidResponse;
                }
                // Treat an empty E_FAIL conservatively; a correlated system-state request detects
                // a silent transport loss without mistaking an empty dispatch queue for a disconnect.
                if (result < 0 && (result != unchecked((int)0x80004005) || messages.Count != 0))
                {
                    _logger.LogDebug("SimConnect dispatch failed with HRESULT {HResult:X8}.", result);
                    return SimulatorConnectionIssue.ConnectionLost;
                }
                if (token.IsCancellationRequested)
                    return SimulatorConnectionIssue.None;

                foreach (var message in messages)
                {
                    switch (message.Kind)
                    {
                        case SimConnectMessageKind.Open when !acknowledged:
                            if (!ConfigureTelemetry(handle))
                                return SimulatorConnectionIssue.SimulatorError;

                            airportFacilityConfigured = ConfigureAirportFacilities(handle);
                            acknowledged = true;
                            everConnected = true;
                            failures = 0;
                            lastHeartbeatAt = _clock.GetTimestamp();
                            Publish(new(SimulatorConnectionState.Connected, Simulator: message.Simulator));
                            RequestAircraftCatalog(handle);
                            break;
                        case SimConnectMessageKind.Quit:
                            return SimulatorConnectionIssue.ConnectionLost;
                        case SimConnectMessageKind.Event
                            when acknowledged && message.EventId == SimConnectTelemetryDefinition.PauseEventId:
                            paused = message.EventData != 0;
                            if (Latest is { } currentTelemetry)
                                PublishTelemetry(currentTelemetry with
                                {
                                    Timestamp = _clock.GetUtcNow(),
                                    Paused = paused
                                });
                            break;
                        case SimConnectMessageKind.SimObjectData
                            when acknowledged
                                && message.RequestId == SimConnectTelemetryDefinition.RequestId
                                && message.DefinitionId == SimConnectTelemetryDefinition.DefinitionId:
                            var telemetry = SimConnectTelemetryMapper.Map(
                                message.Data,
                                _clock.GetUtcNow(),
                                paused);
                            if (telemetry is not null)
                                PublishTelemetry(telemetry);
                            else
                                _logger.LogDebug("Ignored invalid aircraft telemetry packet.");
                            break;
                        case SimConnectMessageKind.SystemState when pendingHeartbeat == message.RequestId:
                            pendingHeartbeat = null;
                            break;
                        case SimConnectMessageKind.EnumerateSimObjectAndLiveryList
                            when acknowledged && message.RequestId == SimConnectAircraftCatalog.RequestId:
                            if (!AcceptAircraftCatalogPage(
                                    message,
                                    ref aircraftCatalogPageCount,
                                    aircraftCatalogPages))
                            {
                                return SimulatorConnectionIssue.InvalidResponse;
                            }
                            break;
                        case SimConnectMessageKind.FacilityData
                            when acknowledged
                                && activeAirportFacilityRequest is not null
                                && message.RequestId == activeAirportFacilityRequest.RequestId:
                            if (!activeAirportFacilityRequest.Accept(message))
                            {
                                _logger.LogWarning(
                                    "Ignored inconsistent SimConnect airport facility response for {Icao}.",
                                    activeAirportFacilityRequest.Query.Icao);
                                activeAirportFacilityRequest.Query.Completion.TrySetResult(null);
                                activeAirportFacilityRequest = null;
                            }
                            break;
                        case SimConnectMessageKind.FacilityDataEnd
                            when acknowledged
                                && activeAirportFacilityRequest is not null
                                && message.RequestId == activeAirportFacilityRequest.RequestId:
                            activeAirportFacilityRequest.Query.Completion.TrySetResult(
                                activeAirportFacilityRequest.Build());
                            activeAirportFacilityRequest = null;
                            break;
                        case SimConnectMessageKind.Exception
                            when activeAirportFacilityRequest is not null
                                && message.SendId == activeAirportFacilityRequest.SendId:
                            _logger.LogWarning(
                                "SimConnect airport facility request for {Icao} failed with exception {Code}, send {SendId}, parameter {Index}.",
                                activeAirportFacilityRequest.Query.Icao,
                                message.ExceptionCode,
                                message.SendId,
                                message.ParameterIndex);
                            activeAirportFacilityRequest.Query.Completion.TrySetResult(null);
                            activeAirportFacilityRequest = null;
                            break;
                        case SimConnectMessageKind.Exception:
                            _logger.LogWarning("SimConnect exception {Code}, send {SendId}, parameter {Index}.",
                                message.ExceptionCode, message.SendId, message.ParameterIndex);
                            return message.ExceptionCode == 5
                                ? SimulatorConnectionIssue.VersionMismatch : SimulatorConnectionIssue.SimulatorError;
                    }
                }

                if (!acknowledged && _clock.GetElapsedTime(openedAt) >= _options.HandshakeTimeout)
                    return SimulatorConnectionIssue.HandshakeTimeout;

                if (activeAirportFacilityRequest is not null)
                {
                    if (activeAirportFacilityRequest.Query.CancellationToken.IsCancellationRequested)
                    {
                        activeAirportFacilityRequest.Query.Completion.TrySetCanceled(
                            activeAirportFacilityRequest.Query.CancellationToken);
                        activeAirportFacilityRequest = null;
                    }
                    else if (_clock.GetElapsedTime(activeAirportFacilityRequest.StartedAt)
                        >= SimConnectAirportFacilityDefinition.ResponseTimeout)
                    {
                        _logger.LogWarning(
                            "Timed out waiting for SimConnect airport facility data for {Icao}.",
                            activeAirportFacilityRequest.Query.Icao);
                        activeAirportFacilityRequest.Query.Completion.TrySetResult(null);
                        activeAirportFacilityRequest = null;
                    }
                }

                if (acknowledged && activeAirportFacilityRequest is null)
                {
                    activeAirportFacilityRequest = StartNextAirportFacilityRequest(
                        handle,
                        airportFacilityConfigured,
                        ref nextAirportFacilityRequestId);
                }

                if (acknowledged)
                {
                    if (pendingHeartbeat.HasValue && _clock.GetElapsedTime(lastHeartbeatAt) >= _options.HeartbeatTimeout)
                        return SimulatorConnectionIssue.ResponseTimeout;

                    if (!pendingHeartbeat.HasValue && _clock.GetElapsedTime(lastHeartbeatAt) >= _options.HeartbeatInterval)
                    {
                        pendingHeartbeat = nextRequestId++;
                        result = _api.RequestSystemState(handle, pendingHeartbeat.Value);
                        if (result < 0)
                            return SimulatorConnectionIssue.ConnectionLost;
                        lastHeartbeatAt = _clock.GetTimestamp();
                    }
                }

                if (WaitHandle.WaitAny(waits, _options.DispatchInterval) == 0)
                    break;
            }
            return SimulatorConnectionIssue.None;
        }
        finally
        {
            activeAirportFacilityRequest?.Query.Completion.TrySetResult(null);
            CompleteQueuedAirportFacilityRequestsUnavailable();

            if (handle != nint.Zero)
                Close(handle);
        }
    }

    private bool ConfigureAirportFacilities(nint handle)
    {
        foreach (string field in SimConnectAirportFacilityDefinition.Fields)
        {
            int result = _api.AddToFacilityDefinition(
                handle,
                SimConnectAirportFacilityDefinition.DefinitionId,
                field);

            if (result < 0)
            {
                _logger.LogWarning(
                    "SimConnect rejected airport facility definition field {Field} with HRESULT {HResult:X8}.",
                    field,
                    result);
                return false;
            }
        }

        return true;
    }

    private ActiveSimConnectAirportFacilityRequest? StartNextAirportFacilityRequest(
        nint handle,
        bool facilityConfigured,
        ref uint nextRequestId)
    {
        while (_airportFacilityQueries.TryDequeue(out SimConnectAirportFacilityQuery? query))
        {
            if (query.CancellationToken.IsCancellationRequested)
            {
                query.Completion.TrySetCanceled(query.CancellationToken);
                continue;
            }

            if (!facilityConfigured)
            {
                query.Completion.TrySetResult(null);
                continue;
            }

            uint requestId = nextRequestId;
            nextRequestId = nextRequestId == uint.MaxValue
                ? SimConnectAirportFacilityDefinition.FirstRequestId
                : nextRequestId + 1;

            int result = _api.RequestFacilityData(
                handle,
                SimConnectAirportFacilityDefinition.DefinitionId,
                requestId,
                query.Icao,
                string.Empty);

            if (result < 0)
            {
                _logger.LogWarning(
                    "SimConnect rejected airport facility request for {Icao} with HRESULT {HResult:X8}.",
                    query.Icao,
                    result);
                query.Completion.TrySetResult(null);
                continue;
            }

            int sendIdResult = _api.GetLastSentPacketId(handle, out uint sendId);
            if (sendIdResult < 0 || sendId == 0)
            {
                _logger.LogWarning(
                    "Could not correlate SimConnect airport facility request for {Icao}; treating local airport data as unavailable.",
                    query.Icao);
                query.Completion.TrySetResult(null);
                continue;
            }

            return new(
                query,
                requestId,
                sendId,
                _clock.GetTimestamp());
        }

        return null;
    }

    private void CompleteQueuedAirportFacilityRequestsUnavailable()
    {
        while (_airportFacilityQueries.TryDequeue(out SimConnectAirportFacilityQuery? query))
            query.Completion.TrySetResult(null);
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

    private void RequestAircraftCatalog(nint handle)
    {
        int result = _api.EnumerateSimObjectsAndLiveries(
            handle,
            SimConnectAircraftCatalog.RequestId,
            SimConnectSimObjectType.User);

        if (result < 0)
        {
            _logger.LogWarning(
                "SimConnect rejected installed-aircraft enumeration with HRESULT {HResult:X8}.",
                result);
        }
    }

    private bool AcceptAircraftCatalogPage(
        SimConnectMessage message,
        ref uint? expectedPageCount,
        IDictionary<uint, IReadOnlyList<SimConnectObjectLivery>> pages)
    {
        if (message.ObjectLiveries is null
            || (expectedPageCount.HasValue && expectedPageCount.Value != message.ListOutOf)
            || pages.ContainsKey(message.ListEntryNumber))
        {
            _logger.LogWarning("Ignored inconsistent installed-aircraft enumeration page.");
            return false;
        }

        expectedPageCount ??= message.ListOutOf;
        pages.Add(message.ListEntryNumber, message.ObjectLiveries);

        if (pages.Count != expectedPageCount.Value)
            return true;

        string[] titles = pages
            .OrderBy(static pair => pair.Key)
            .SelectMany(static pair => pair.Value)
            .Select(static item => item.AircraftTitle)
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static title => title, StringComparer.Ordinal)
            .ToArray();

        PublishAircraftCatalog(new(true, titles));
        return true;
    }

    private void Close(nint handle)
    {
        try
        {
            int result = _api.Close(handle);
            if (result < 0)
                _logger.LogWarning("SimConnect close returned HRESULT {HResult:X8}.", result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SimConnect close failed after connection loss.");
        }
    }

    private void Publish(SimulatorConnectionSnapshot snapshot)
    {
        if (Current == snapshot)
            return;

        Volatile.Write(ref _current, snapshot);
        _logger.LogInformation("Simulator connection: {State}; issue: {Issue}.", snapshot.State, snapshot.Issue);
    }

    private void PublishTelemetry(AircraftTelemetrySnapshot? snapshot) =>
        Volatile.Write(ref _latestTelemetry, snapshot);

    private void PublishAircraftCatalog(SimConnectAircraftCatalogSnapshot snapshot) =>
        Volatile.Write(ref _aircraftCatalog, snapshot);
}
