using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

internal sealed class SimConnectTestTransport : ISimConnectApi
{
    internal const int Failure = unchecked((int)0x80004005);
    private readonly ConcurrentQueue<(byte[]? Packet, int Result, Action? Action)> _dispatch = new();
    private readonly ConcurrentBag<int> _threads = [];
    private int _attempts;
    private int _closed;
    private int _dispatched;
    private int _liveHandles;
    private int _activeCalls;

    internal ConcurrentQueue<int> OpenResults { get; } = new();
    internal Exception? OpenException { get; set; }
    internal bool ReturnHandleOnFailure { get; set; }
    internal bool OverlapDetected { get; private set; }
    internal bool OpenedBeforeClose { get; private set; }
    internal int Attempts => Volatile.Read(ref _attempts);
    internal int Closed => Volatile.Read(ref _closed);
    internal int Dispatched => Volatile.Read(ref _dispatched);
    internal int[] ThreadIds => _threads.Distinct().ToArray();
    internal ConcurrentQueue<uint> Heartbeats { get; } = new();
    internal int HeartbeatResult { get; set; }
    internal ConcurrentQueue<(uint DefinitionId, string DatumName, string Units)> DataDefinitions { get; } = new();
    internal int AddDefinitionResult { get; set; }
    internal ConcurrentQueue<(uint RequestId, uint DefinitionId, SimConnectPeriod Period)> TelemetryRequests { get; } = new();
    internal int TelemetryRequestResult { get; set; }
    internal ConcurrentQueue<(uint EventId, string EventName)> SystemEvents { get; } = new();
    internal int SubscribeResult { get; set; }

    internal void Enqueue(byte[]? packet = null, int result = 0, Action? action = null) =>
        _dispatch.Enqueue((packet, result, action));

    public int Open(out nint handle, nint notificationEvent)
    {
        Enter();
        try
        {
            int attempt = Interlocked.Increment(ref _attempts);
            handle = nint.Zero;
            if (OpenException is not null)
                throw OpenException;
            int result = OpenResults.TryDequeue(out int queued) ? queued : 0;
            if (result >= 0 || ReturnHandleOnFailure)
            {
                OpenedBeforeClose |= Interlocked.Increment(ref _liveHandles) != 1;
                handle = attempt;
            }
            return result;
        }
        finally { Exit(); }
    }

    public int CallDispatch(nint handle, DispatchCallback callback)
    {
        Enter();
        try
        {
            if (!_dispatch.TryDequeue(out var frame))
                return 0;
            frame.Action?.Invoke();
            if (frame.Packet is not null)
                SimConnectPackets.WithPointer(frame.Packet, (data, size) => callback(data, size, nint.Zero));
            return frame.Result;
        }
        finally
        {
            Interlocked.Increment(ref _dispatched);
            Exit();
        }
    }

    public int AddToDataDefinition(nint handle, uint definitionId, string datumName, string unitsName)
    {
        Enter();
        try
        {
            DataDefinitions.Enqueue((definitionId, datumName, unitsName));
            return AddDefinitionResult;
        }
        finally { Exit(); }
    }

    public int RequestDataOnUserAircraft(
        nint handle,
        uint requestId,
        uint definitionId,
        SimConnectPeriod period)
    {
        Enter();
        try
        {
            TelemetryRequests.Enqueue((requestId, definitionId, period));
            return TelemetryRequestResult;
        }
        finally { Exit(); }
    }

    public int SubscribeToSystemEvent(nint handle, uint eventId, string eventName)
    {
        Enter();
        try
        {
            SystemEvents.Enqueue((eventId, eventName));
            return SubscribeResult;
        }
        finally { Exit(); }
    }

    public int Close(nint handle)
    {
        Enter();
        try
        {
            Interlocked.Decrement(ref _liveHandles);
            Interlocked.Increment(ref _closed);
            return 0;
        }
        finally { Exit(); }
    }

    public int RequestSystemState(nint handle, uint requestId)
    {
        Enter();
        try
        {
            Heartbeats.Enqueue(requestId);
            return HeartbeatResult;
        }
        finally { Exit(); }
    }

    private void Enter()
    {
        _threads.Add(Environment.CurrentManagedThreadId);
        OverlapDetected |= Interlocked.Increment(ref _activeCalls) != 1;
    }

    private void Exit() => Interlocked.Decrement(ref _activeCalls);
}

internal static class SimConnectPackets
{
    internal static byte[] Header(uint kind, int size = 12)
    {
        byte[] bytes = new byte[size];
        BitConverter.GetBytes((uint)size).CopyTo(bytes, 0);
        BitConverter.GetBytes(1u).CopyTo(bytes, 4);
        BitConverter.GetBytes(kind).CopyTo(bytes, 8);
        return bytes;
    }

    internal static byte[] Open(string name = "Microsoft Flight Simulator 2024")
    {
        byte[] bytes = Header(2, 308);
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 12);
        uint[] version = [1, 7, 12, 3, 11, 2, 34, 5];
        for (int i = 0; i < version.Length; i++)
            BitConverter.GetBytes(version[i]).CopyTo(bytes, 268 + i * 4);
        return bytes;
    }

    internal static byte[] Exception(uint code)
    {
        byte[] bytes = Header(1, 24);
        BitConverter.GetBytes(code).CopyTo(bytes, 12);
        BitConverter.GetBytes(19u).CopyTo(bytes, 16);
        BitConverter.GetBytes(4u).CopyTo(bytes, 20);
        return bytes;
    }

    internal static byte[] Event(uint eventId, uint data)
    {
        byte[] bytes = Header(4, 24);
        BitConverter.GetBytes(eventId).CopyTo(bytes, 16);
        BitConverter.GetBytes(data).CopyTo(bytes, 20);
        return bytes;
    }

    internal static byte[] SimObjectData(uint requestId, uint definitionId, IReadOnlyList<double> values)
    {
        byte[] bytes = Header(8, 40 + values.Count * sizeof(double));
        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        BitConverter.GetBytes(definitionId).CopyTo(bytes, 20);
        BitConverter.GetBytes((uint)values.Count).CopyTo(bytes, 36);
        for (int i = 0; i < values.Count; i++)
            BitConverter.GetBytes(values[i]).CopyTo(bytes, 40 + i * sizeof(double));
        return bytes;
    }

    internal static byte[] SystemState(uint requestId, uint inFlight = 0)
    {
        byte[] bytes = Header(15, 284);
        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        BitConverter.GetBytes(inFlight).CopyTo(bytes, 16);
        return bytes;
    }

    internal static void WithPointer(byte[] bytes, Action<nint, uint> action)
    {
        nint data = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, data, bytes.Length);
            action(data, (uint)bytes.Length);
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
