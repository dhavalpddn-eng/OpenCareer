using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenCareer.Domain.Simulation;

public static class DeterministicSeed
{
    /// <summary>
    /// Derives stable independent streams from one career seed. A new unrelated subsystem can
    /// be added without consuming random numbers from existing systems and reshuffling a save.
    /// </summary>
    public static ulong Derive(ulong masterSeed, string streamKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);

        var keyBytes = Encoding.UTF8.GetBytes(streamKey);
        var input = new byte[sizeof(ulong) + keyBytes.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(input, masterSeed);
        keyBytes.CopyTo(input.AsSpan(sizeof(ulong)));

        var hash = SHA256.HashData(input);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    public static DeterministicRandom CreateStream(ulong masterSeed, string streamKey) =>
        new(Derive(masterSeed, streamKey));
}
