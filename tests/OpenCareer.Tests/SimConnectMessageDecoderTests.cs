using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

public sealed class SimConnectMessageDecoderTests
{
    [Fact]
    public void OpenDecodesNameAndVersionAtSdkOffsets()
    {
        var message = Decode(SimConnectPackets.Open());
        Assert.Equal(SimConnectMessageKind.Open, message.Kind);
        Assert.Equal("Microsoft Flight Simulator 2024", message.Simulator!.Name);
        Assert.Equal(new Version(1, 7, 12, 3), message.Simulator.ApplicationVersion);
        Assert.Equal(new Version(11, 2, 34, 5), message.Simulator.SimConnectVersion);
    }

    [Fact]
    public void ExceptionRetainsServerCodeAndRequestDetails()
    {
        var message = Decode(SimConnectPackets.Exception(5));
        Assert.Equal(SimConnectMessageKind.Exception, message.Kind);
        Assert.Equal(5u, message.ExceptionCode);
        Assert.Equal(19u, message.SendId);
        Assert.Equal(4u, message.ParameterIndex);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TruncatedKnownMessagesAreRejected(uint kind) =>
        Assert.Throws<InvalidDataException>(() => Decode(SimConnectPackets.Header(kind)));

    [Fact]
    public void InvalidHeaderSizesAndNullPointersAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SimConnectMessageDecoder.Decode(nint.Zero, 12));
        Assert.Throws<InvalidDataException>(() => Decode(new byte[8]));
        var packet = SimConnectPackets.Header(3);
        BitConverter.GetBytes(100u).CopyTo(packet, 0);
        Assert.Throws<InvalidDataException>(() => Decode(packet));
        BitConverter.GetBytes(4u).CopyTo(packet, 0);
        Assert.Throws<InvalidDataException>(() => Decode(packet));
    }

    [Fact]
    public void UnterminatedNameAndInvalidVersionAreRejected()
    {
        var packet = SimConnectPackets.Open();
        Array.Fill(packet, (byte)'A', 12, 256);
        Assert.Throws<InvalidDataException>(() => Decode(packet));
        packet = SimConnectPackets.Open();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(packet, 268);
        Assert.Throws<InvalidDataException>(() => Decode(packet));
    }

    [Fact]
    public void UnknownMessageIsIgnoredAndQuitHasNoPayload()
    {
        Assert.Equal(SimConnectMessageKind.None, Decode(SimConnectPackets.Header(999)).Kind);
        Assert.Equal(SimConnectMessageKind.Quit, Decode(SimConnectPackets.Header(3)).Kind);
    }

    private static SimConnectMessage Decode(byte[] packet)
    {
        SimConnectMessage? message = null;
        SimConnectPackets.WithPointer(packet, (data, size) => message = SimConnectMessageDecoder.Decode(data, size));
        return message!;
    }
}
