using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenCareer.Application.Simulator;

namespace OpenCareer.SimConnect.Native;

internal static class SimConnectMessageDecoder
{
    private const int HeaderSize = 12;
    private const int OpenSize = HeaderSize + 256 + 10 * sizeof(uint);
    private const int ExceptionSize = HeaderSize + 3 * sizeof(uint);
    private const int EventSize = HeaderSize + 3 * sizeof(uint);
    private const int SimObjectDataHeaderSize = HeaderSize + 7 * sizeof(uint);
    private const int ListHeaderSize = HeaderSize + 4 * sizeof(uint);
    private const int SimObjectLiverySize = 512;

    internal static SimConnectMessage Decode(nint data, uint bufferSize)
    {
        if (data == nint.Zero || bufferSize < HeaderSize)
            throw new InvalidDataException("Missing or truncated SimConnect header.");

        uint declaredSize = unchecked((uint)Marshal.ReadInt32(data));
        if (declaredSize < HeaderSize || declaredSize > bufferSize)
            throw new InvalidDataException("Invalid SimConnect message length.");

        var kind = (SimConnectMessageKind)unchecked((uint)Marshal.ReadInt32(data, 8));
        return kind switch
        {
            SimConnectMessageKind.Open => DecodeOpen(data, declaredSize),
            SimConnectMessageKind.Exception => DecodeException(data, declaredSize),
            SimConnectMessageKind.Quit => new(kind),
            SimConnectMessageKind.Event => DecodeEvent(data, declaredSize),
            SimConnectMessageKind.SimObjectData => DecodeSimObjectData(data, declaredSize),
            SimConnectMessageKind.SystemState => DecodeSystemState(data, declaredSize),
            SimConnectMessageKind.EnumerateSimObjectAndLiveryList =>
                DecodeSimObjectAndLiveryList(data, declaredSize),
            _ => new(SimConnectMessageKind.None)
        };
    }

    private static SimConnectMessage DecodeOpen(nint data, uint size)
    {
        RequireSize(size, OpenSize);
        byte[] buffer = new byte[OpenSize];
        Marshal.Copy(data, buffer, 0, buffer.Length);
        int terminator = Array.IndexOf(buffer, (byte)0, HeaderSize, 256);
        if (terminator < 0)
            throw new InvalidDataException("Unterminated SimConnect application name.");

        string name = Marshal.PtrToStringAnsi(data + HeaderSize, terminator - HeaderSize) ?? string.Empty;
        var info = new SimulatorInfo(name, ReadVersion(buffer.AsSpan(268, 16)), ReadVersion(buffer.AsSpan(284, 16)));
        return new(SimConnectMessageKind.Open, info);
    }

    private static SimConnectMessage DecodeException(nint data, uint size)
    {
        RequireSize(size, ExceptionSize);
        return new(SimConnectMessageKind.Exception,
            ExceptionCode: unchecked((uint)Marshal.ReadInt32(data, 12)),
            SendId: unchecked((uint)Marshal.ReadInt32(data, 16)),
            ParameterIndex: unchecked((uint)Marshal.ReadInt32(data, 20)));
    }

    private static SimConnectMessage DecodeEvent(nint data, uint size)
    {
        RequireSize(size, EventSize);
        return new(SimConnectMessageKind.Event,
            EventId: unchecked((uint)Marshal.ReadInt32(data, 16)),
            EventData: unchecked((uint)Marshal.ReadInt32(data, 20)));
    }

    private static SimConnectMessage DecodeSimObjectData(nint data, uint size)
    {
        RequireSize(size, SimObjectDataHeaderSize);
        uint requestId = unchecked((uint)Marshal.ReadInt32(data, 12));
        uint definitionId = unchecked((uint)Marshal.ReadInt32(data, 20));
        uint defineCount = unchecked((uint)Marshal.ReadInt32(data, 36));

        long requiredSize = SimObjectDataHeaderSize + (long)defineCount * sizeof(double);
        if (requiredSize > size || defineCount > int.MaxValue)
            throw new InvalidDataException("Truncated SimConnect object data.");

        int count = checked((int)defineCount);
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            long bits = Marshal.ReadInt64(data, SimObjectDataHeaderSize + i * sizeof(double));
            values[i] = BitConverter.Int64BitsToDouble(bits);
        }

        return new(SimConnectMessageKind.SimObjectData,
            RequestId: requestId,
            DefinitionId: definitionId,
            Data: values);
    }

    private static SimConnectMessage DecodeSimObjectAndLiveryList(nint data, uint size)
    {
        RequireSize(size, ListHeaderSize);

        uint requestId = unchecked((uint)Marshal.ReadInt32(data, 12));
        uint arraySize = unchecked((uint)Marshal.ReadInt32(data, 16));
        uint entryNumber = unchecked((uint)Marshal.ReadInt32(data, 20));
        uint outOf = unchecked((uint)Marshal.ReadInt32(data, 24));

        if (outOf == 0 || entryNumber >= outOf || arraySize > int.MaxValue)
            throw new InvalidDataException("Invalid SimConnect aircraft enumeration page metadata.");

        long requiredSize = ListHeaderSize + (long)arraySize * SimObjectLiverySize;
        if (requiredSize > size)
            throw new InvalidDataException("Truncated SimConnect aircraft enumeration page.");

        var entries = new SimConnectObjectLivery[checked((int)arraySize)];
        for (int index = 0; index < entries.Length; index++)
        {
            int offset = ListHeaderSize + index * SimObjectLiverySize;
            string title = ReadFixedAnsi(data + offset, 256);
            string livery = ReadFixedAnsi(data + offset + 256, 256);

            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidDataException("SimConnect returned an aircraft entry without a title.");

            entries[index] = new(title.Trim(), livery.Trim());
        }

        return new(
            SimConnectMessageKind.EnumerateSimObjectAndLiveryList,
            RequestId: requestId,
            ListEntryNumber: entryNumber,
            ListOutOf: outOf,
            ObjectLiveries: entries);
    }

    private static string ReadFixedAnsi(nint data, int capacity)
    {
        byte[] buffer = new byte[capacity];
        Marshal.Copy(data, buffer, 0, capacity);
        int terminator = Array.IndexOf(buffer, (byte)0);
        int length = terminator >= 0 ? terminator : capacity;
        return Marshal.PtrToStringAnsi(data, length) ?? string.Empty;
    }

    private static Version ReadVersion(ReadOnlySpan<byte> data)
    {
        Span<int> parts = stackalloc int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            uint part = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * sizeof(uint), sizeof(uint)));
            if (part > int.MaxValue)
                throw new InvalidDataException("SimConnect version component is out of range.");
            parts[i] = (int)part;
        }
        return new(parts[0], parts[1], parts[2], parts[3]);
    }

    private static SimConnectMessage DecodeSystemState(nint data, uint size)
    {
        RequireSize(size, HeaderSize + 3 * sizeof(uint) + 260);
        return new(SimConnectMessageKind.SystemState, RequestId: unchecked((uint)Marshal.ReadInt32(data, 12)));
    }

    private static void RequireSize(uint actual, int required)
    {
        if (actual < required)
            throw new InvalidDataException("Truncated SimConnect message.");
    }
}
