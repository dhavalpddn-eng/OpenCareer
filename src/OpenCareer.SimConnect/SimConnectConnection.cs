using Microsoft.Extensions.Logging;
using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.SimConnect;

public sealed class SimConnectConnection : ISimulatorConnection
{
    private readonly object _lifecycleGate = new();
    private readonly ISimConnectApi _api;
    private readonly ILogger<SimConnectConnection> _logger;
    private readonly SimConnectConnectionOptions _options;
    private readonly TimeProvider _clock;
    private SimulatorConnectionSnapshot _current = new(SimulatorConnectionState.Disconnected);
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
        Publish(new(SimulatorConnectionState.WaitingForSimulator));
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    var issue = RunSession(token, ref everConnected, ref failures);
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
            _logger.LogError(ex, "SimConnect connection worker stopped unexpectedly.");
            Publish(new(SimulatorConnectionState.Faulted, SimulatorConnectionIssue.UnexpectedError));
        }
        finally
        {
            if (token.IsCancellationRequested)
                Publish(new(SimulatorConnectionState.Disconnected));
        }
    }

    private SimulatorConnectionIssue RunSession(CancellationToken token, ref bool everConnected, ref int failures)
    {
        using var notification = new AutoResetEvent(false);
        WaitHandle[] waits = [token.WaitHandle, notification];
        nint handle = nint.Zero;
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
                            acknowledged = true;
                            everConnected = true;
                            failures = 0;
                            lastHeartbeatAt = _clock.GetTimestamp();
                            Publish(new(SimulatorConnectionState.Connected, Simulator: message.Simulator));
                            break;
                        case SimConnectMessageKind.Quit:
                            return SimulatorConnectionIssue.ConnectionLost;
                        case SimConnectMessageKind.SystemState when pendingHeartbeat == message.RequestId:
                            pendingHeartbeat = null;
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
            if (handle != nint.Zero)
                Close(handle);
        }
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
}
