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
    internal ConcurrentQueue<(uint DefinitionId, string DatumName)> StringDataDefinitions { get; } = new();
    internal int AddDefinitionResult { get; set; }
    internal int AddStringDefinitionResult { get; set; }
    internal HashSet<uint> FailedDataDefinitionIds { get; } = [];
    internal ConcurrentQueue<(uint RequestId, uint DefinitionId, SimConnectPeriod Period)> TelemetryRequests { get; } = new();
    internal int TelemetryRequestResult { get; set; }
    internal ConcurrentQueue<(uint EventId, string EventName)> SystemEvents { get; } = new();
    internal int SubscribeResult { get; set; }
    internal ConcurrentQueue<(uint RequestId, SimConnectSimObjectType Type)> AircraftEnumerations { get; } = new();
    internal int AircraftEnumerationResult { get; set; }
    internal ConcurrentQueue<(uint DefinitionId, string FieldName)> FacilityDefinitions { get; } = new();
    internal int FacilityDefinitionResult { get; set; }
    internal ConcurrentQueue<(uint DefinitionId, uint RequestId, string Icao, string Region)> FacilityRequests { get; } = new();
    internal int FacilityRequestResult { get; set; }
    internal int GetLastSentPacketIdResult { get; set; }
    internal uint LastSentPacketId { get; set; } = 700;

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
            return FailedDataDefinitionIds.Contains(definitionId)
                ? Failure
                : AddDefinitionResult;
        }
        finally { Exit(); }
    }

    public int AddStringToDataDefinition(
        nint handle,
        uint definitionId,
        string datumName)
    {
        Enter();
        try
        {
            StringDataDefinitions.Enqueue((definitionId, datumName));
            return AddStringDefinitionResult;
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

    public int EnumerateSimObjectsAndLiveries(
        nint handle,
        uint requestId,
        SimConnectSimObjectType type)
    {
        Enter();
        try
        {
            AircraftEnumerations.Enqueue((requestId, type));
            return AircraftEnumerationResult;
        }
        finally { Exit(); }
    }

    public int AddToFacilityDefinition(
        nint handle,
        uint definitionId,
        string fieldName)
    {
        Enter();
        try
        {
            FacilityDefinitions.Enqueue((definitionId, fieldName));
            return FacilityDefinitionResult;
        }
        finally { Exit(); }
    }

    public int RequestFacilityData(
        nint handle,
        uint definitionId,
        uint requestId,
        string icao,
        string region)
    {
        Enter();
        try
        {
            FacilityRequests.Enqueue((definitionId, requestId, icao, region));
            return FacilityRequestResult;
        }
        finally { Exit(); }
    }

    public int GetLastSentPacketId(nint handle, out uint sendId)
    {
        Enter();
        try
        {
            sendId = LastSentPacketId;
            return GetLastSentPacketIdResult;
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

    internal static byte[] Exception(
        uint code,
        uint sendId = 19,
        uint parameterIndex = 4)
    {
        byte[] bytes = Header(1, 24);
        BitConverter.GetBytes(code).CopyTo(bytes, 12);
        BitConverter.GetBytes(sendId).CopyTo(bytes, 16);
        BitConverter.GetBytes(parameterIndex).CopyTo(bytes, 20);
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

    internal static byte[] StringSimObjectData(
        uint requestId,
        uint definitionId,
        string value)
    {
        const int headerSize = 40;
        const int stringSize = 128;
        byte[] bytes =
            Header(
                8,
                headerSize + stringSize);

        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        BitConverter.GetBytes(definitionId).CopyTo(bytes, 20);
        BitConverter.GetBytes(1u).CopyTo(bytes, 36);
        WriteFixedString(bytes, headerSize, value, stringSize);
        return bytes;
    }

    internal static byte[] EnumeratedSimObjects(
        uint requestId,
        uint entryNumber,
        uint outOf,
        params (string AircraftTitle, string LiveryName)[] entries)
    {
        const int headerSize = 28;
        const int entrySize = 512;
        byte[] bytes = Header(39, headerSize + entries.Length * entrySize);
        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        BitConverter.GetBytes((uint)entries.Length).CopyTo(bytes, 16);
        BitConverter.GetBytes(entryNumber).CopyTo(bytes, 20);
        BitConverter.GetBytes(outOf).CopyTo(bytes, 24);

        for (int index = 0; index < entries.Length; index++)
        {
            WriteFixedString(bytes, headerSize + index * entrySize, entries[index].AircraftTitle);
            WriteFixedString(bytes, headerSize + index * entrySize + 256, entries[index].LiveryName);
        }

        return bytes;
    }

    internal static byte[] AirportFacility(
        uint requestId,
        uint uniqueRequestId,
        string name,
        string icao,
        double latitudeDegrees = 0,
        double longitudeDegrees = 0)
    {
        const int payloadOffset = 40;
        byte[] bytes = Header(29, payloadOffset + 88);
        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        BitConverter.GetBytes(uniqueRequestId).CopyTo(bytes, 16);
        BitConverter.GetBytes(0u).CopyTo(bytes, 20);
        BitConverter.GetBytes(0u).CopyTo(bytes, 24);
        bytes[28] = 0;
        BitConverter.GetBytes(0u).CopyTo(bytes, 32);
        BitConverter.GetBytes(0u).CopyTo(bytes, 36);
        BitConverter.GetBytes(latitudeDegrees).CopyTo(bytes, payloadOffset);
        BitConverter.GetBytes(longitudeDegrees).CopyTo(bytes, payloadOffset + 8);
        WriteFixedString(bytes, payloadOffset + 16, name, 64);
        WriteFixedString(bytes, payloadOffset + 80, icao, 8);
        return bytes;
    }

    internal static byte[] RunwayFacility(
        uint requestId,
        uint uniqueRequestId,
        uint parentUniqueRequestId,
        uint itemIndex,
        uint listSize,
        float lengthMeters,
        float widthMeters,
        int surface,
        int primaryNumber,
        int primaryDesignator,
        int secondaryNumber,
        int secondaryDesignator,
        bool primaryClosed = false,
        bool secondaryClosed = false,
        double centerLatitudeDegrees = 0,
        double centerLongitudeDegrees = 0,
        float headingTrueDegrees = 0)
    {
        const int payloadOffset = 40;
        byte[] bytes = Header(29, payloadOffset + 52);
        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        BitConverter.GetBytes(uniqueRequestId).CopyTo(bytes, 16);
        BitConverter.GetBytes(parentUniqueRequestId).CopyTo(bytes, 20);
        BitConverter.GetBytes(1u).CopyTo(bytes, 24);
        bytes[28] = 1;
        BitConverter.GetBytes(itemIndex).CopyTo(bytes, 32);
        BitConverter.GetBytes(listSize).CopyTo(bytes, 36);
        BitConverter.GetBytes(centerLatitudeDegrees).CopyTo(bytes, payloadOffset);
        BitConverter.GetBytes(centerLongitudeDegrees).CopyTo(bytes, payloadOffset + 8);
        BitConverter.GetBytes(headingTrueDegrees).CopyTo(bytes, payloadOffset + 16);
        BitConverter.GetBytes(lengthMeters).CopyTo(bytes, payloadOffset + 20);
        BitConverter.GetBytes(widthMeters).CopyTo(bytes, payloadOffset + 24);
        BitConverter.GetBytes(surface).CopyTo(bytes, payloadOffset + 28);
        BitConverter.GetBytes(primaryNumber).CopyTo(bytes, payloadOffset + 32);
        BitConverter.GetBytes(primaryDesignator).CopyTo(bytes, payloadOffset + 36);
        BitConverter.GetBytes(secondaryNumber).CopyTo(bytes, payloadOffset + 40);
        BitConverter.GetBytes(secondaryDesignator).CopyTo(bytes, payloadOffset + 44);
        bytes[payloadOffset + 48] = primaryClosed ? (byte)1 : (byte)0;
        bytes[payloadOffset + 49] = secondaryClosed ? (byte)1 : (byte)0;
        return bytes;
    }

    internal static byte[] FacilityDataEnd(uint requestId)
    {
        byte[] bytes = Header(30, 16);
        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        return bytes;
    }

    internal static byte[] SystemState(uint requestId, uint inFlight = 0)
    {
        byte[] bytes = Header(15, 284);
        BitConverter.GetBytes(requestId).CopyTo(bytes, 12);
        BitConverter.GetBytes(inFlight).CopyTo(bytes, 16);
        return bytes;
    }

    private static void WriteFixedString(
        byte[] bytes,
        int offset,
        string value,
        int capacity = 256)
    {
        byte[] encoded = Encoding.ASCII.GetBytes(value);
        if (encoded.Length >= capacity)
            throw new ArgumentOutOfRangeException(nameof(value));

        encoded.CopyTo(bytes, offset);
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
