using System.Runtime.InteropServices;
using System.Text;

namespace OpenCareer.LiveProbe;

internal static class NativeRuntimeDiagnostics
{
    public static void ReportSimConnect()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string path = Path.Combine(AppContext.BaseDirectory, "SimConnect.dll");
        Console.WriteLine($"SimConnect native runtime: {path}");

        if (!File.Exists(path))
        {
            Console.Error.WriteLine("SimConnect native load: FILE MISSING");
            return;
        }

        try
        {
            PeImportInfo info = ReadImports(path);
            Console.WriteLine($"SimConnect PE machine: {info.Machine}");

            nint handle = NativeLibrary.Load(path);
            try
            {
                Console.WriteLine("SimConnect native load: OK");
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"SimConnect native load: FAILED ({ex.GetType().Name}) {ex.Message}");

            try
            {
                PeImportInfo info = ReadImports(path);
                Console.Error.WriteLine($"SimConnect PE machine: {info.Machine}");
                Console.Error.WriteLine("Imported native dependencies:");

                foreach (string dependency in info.Imports)
                {
                    nint dependencyHandle = nint.Zero;
                    try
                    {
                        dependencyHandle = NativeLibrary.Load(dependency);
                        Console.Error.WriteLine($"  OK   {dependency}");
                    }
                    catch (Exception dependencyError)
                    {
                        Console.Error.WriteLine(
                            $"  FAIL {dependency} ({dependencyError.GetType().Name}) {dependencyError.Message}");
                    }
                    finally
                    {
                        if (dependencyHandle != nint.Zero)
                            NativeLibrary.Free(dependencyHandle);
                    }
                }
            }
            catch (Exception inspectionError)
            {
                Console.Error.WriteLine(
                    $"Unable to inspect SimConnect imports: {inspectionError.Message}");
            }
        }
    }

    private static PeImportInfo ReadImports(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (reader.ReadUInt16() != 0x5A4D)
            throw new InvalidDataException("File does not have an MZ header.");

        stream.Position = 0x3C;
        int peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 24)
            throw new InvalidDataException("Invalid PE header offset.");

        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("File does not have a PE header.");

        ushort machine = reader.ReadUInt16();
        ushort sectionCount = reader.ReadUInt16();
        stream.Position += 12;
        ushort optionalHeaderSize = reader.ReadUInt16();
        stream.Position += 2;

        long optionalHeaderOffset = stream.Position;
        ushort magic = reader.ReadUInt16();
        int dataDirectoryOffset = magic switch
        {
            0x20B => 112,
            0x10B => 96,
            _ => throw new InvalidDataException(
                $"Unsupported PE optional-header magic 0x{magic:X4}.")
        };

        long importDirectoryEntry =
            optionalHeaderOffset + dataDirectoryOffset + 8;
        if (importDirectoryEntry + 8 > optionalHeaderOffset + optionalHeaderSize)
            throw new InvalidDataException("PE import directory is unavailable.");

        stream.Position = importDirectoryEntry;
        uint importRva = reader.ReadUInt32();
        _ = reader.ReadUInt32();

        long sectionTableOffset = optionalHeaderOffset + optionalHeaderSize;
        stream.Position = sectionTableOffset;

        var sections = new List<PeSection>(sectionCount);
        for (int i = 0; i < sectionCount; i++)
        {
            stream.Position += 8;
            uint virtualSize = reader.ReadUInt32();
            uint virtualAddress = reader.ReadUInt32();
            uint rawSize = reader.ReadUInt32();
            uint rawPointer = reader.ReadUInt32();
            stream.Position += 16;

            sections.Add(new(
                virtualAddress,
                virtualSize,
                rawSize,
                rawPointer));
        }

        string machineName = machine switch
        {
            0x8664 => "x64 (AMD64)",
            0x014C => "x86 (I386)",
            0xAA64 => "ARM64",
            _ => $"0x{machine:X4}"
        };

        if (importRva == 0)
            return new(machineName, []);

        long descriptorOffset = RvaToOffset(importRva, sections);
        var imports = new List<string>();

        for (int index = 0; index < 512; index++)
        {
            stream.Position = descriptorOffset + index * 20L;
            uint originalFirstThunk = reader.ReadUInt32();
            uint timeDateStamp = reader.ReadUInt32();
            uint forwarderChain = reader.ReadUInt32();
            uint nameRva = reader.ReadUInt32();
            uint firstThunk = reader.ReadUInt32();

            if (originalFirstThunk == 0
                && timeDateStamp == 0
                && forwarderChain == 0
                && nameRva == 0
                && firstThunk == 0)
            {
                break;
            }

            if (nameRva == 0)
                continue;

            long nameOffset = RvaToOffset(nameRva, sections);
            stream.Position = nameOffset;
            string name = ReadNullTerminatedAscii(reader);
            if (!string.IsNullOrWhiteSpace(name))
                imports.Add(name);
        }

        return new(
            machineName,
            imports.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static long RvaToOffset(
        uint rva,
        IReadOnlyList<PeSection> sections)
    {
        foreach (PeSection section in sections)
        {
            uint size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress
                && (ulong)rva < (ulong)section.VirtualAddress + size)
            {
                return checked(
                    (long)section.RawPointer
                    + (rva - section.VirtualAddress));
            }
        }

        throw new InvalidDataException(
            $"RVA 0x{rva:X8} does not map to a PE section.");
    }

    private static string ReadNullTerminatedAscii(BinaryReader reader)
    {
        var bytes = new List<byte>(64);
        while (bytes.Count < 4096)
        {
            byte value = reader.ReadByte();
            if (value == 0)
                return Encoding.ASCII.GetString(bytes.ToArray());

            bytes.Add(value);
        }

        throw new InvalidDataException("PE import name is unreasonably long.");
    }

    private sealed record PeImportInfo(
        string Machine,
        IReadOnlyList<string> Imports);

    private sealed record PeSection(
        uint VirtualAddress,
        uint VirtualSize,
        uint RawSize,
        uint RawPointer);
}
