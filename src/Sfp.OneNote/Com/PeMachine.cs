using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace Sfp.OneNote.Com;

/// <summary>Whether an executable is 32-bit or 64-bit, as its PE header says (<see cref="PeMachine.Read"/>).</summary>
public enum Bitness
{
    /// <summary>Not known: no such file, a file that cannot be read, or not a PE image of a machine listed here.</summary>
    Unknown,

    /// <summary>A 32-bit image: IMAGE_FILE_MACHINE_I386 (0x014C).</summary>
    X86,

    /// <summary>A 64-bit image: IMAGE_FILE_MACHINE_AMD64 (0x8664) or IMAGE_FILE_MACHINE_ARM64 (0xAA64).</summary>
    X64,
}

/// <summary>
/// Reads the machine field of an executable's COFF header by hand, so the type library check can tell a 64-bit OneNote
/// from a 32-bit one without starting it: the "MZ" signature at offset 0, the PE header offset (e_lfanew, a 32-bit little
/// endian value) at 0x3C, the "PE\0\0" signature at that offset and the 16-bit machine field right after it.
/// </summary>
public static class PeMachine
{
    /// <summary>IMAGE_FILE_MACHINE_I386.</summary>
    private const ushort I386 = 0x014C;

    /// <summary>IMAGE_FILE_MACHINE_AMD64.</summary>
    private const ushort Amd64 = 0x8664;

    /// <summary>IMAGE_FILE_MACHINE_ARM64.</summary>
    private const ushort Arm64 = 0xAA64;

    /// <summary>Where the DOS header keeps e_lfanew, the offset of the PE header.</summary>
    private const int PeOffsetField = 0x3C;

    /// <summary>The "PE\0\0" signature and the machine field after it.</summary>
    private const int PeSignatureAndMachineLength = 6;

    /// <summary>
    /// The bitness the file's COFF machine field names: <see cref="Bitness.X64"/> for x64 (0x8664) and arm64 (0xAA64),
    /// <see cref="Bitness.X86"/> for x86 (0x014C). <see cref="Bitness.Unknown"/> for any other machine, for a file too
    /// short to hold the fields or without the MZ or PE signature, for a blank path or one the file system cannot take
    /// (an embedded null character), and when the file cannot be opened or read (<see cref="IOException"/>, a missing
    /// file included, or <see cref="UnauthorizedAccessException"/>). The file is opened read-only, sharing read, write
    /// and delete, so a running OneNote does not stop the read.
    /// </summary>
    public static Bitness Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            return Bitness.Unknown;
        }

        try
        {
            using SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return FromMachine(ReadMachine(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Bitness.Unknown;
        }
    }

    /// <summary>The bitness a COFF machine value names; <see cref="Bitness.Unknown"/> for null (no machine field read) and for any machine not listed.</summary>
    private static Bitness FromMachine(ushort? machine) => machine switch
    {
        Amd64 or Arm64 => Bitness.X64,
        I386 => Bitness.X86,
        _ => Bitness.Unknown,
    };

    /// <summary>The machine field, or null when a signature is missing or the file ends before a field it needs.</summary>
    private static ushort? ReadMachine(SafeFileHandle file)
    {
        Span<byte> dosHeader = stackalloc byte[PeOffsetField + sizeof(int)];
        if (!ReadFully(file, dosHeader, 0) || dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
        {
            return null;
        }

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader[PeOffsetField..]);
        Span<byte> peHeader = stackalloc byte[PeSignatureAndMachineLength];
        if (peOffset < 0 || !ReadFully(file, peHeader, peOffset) || !peHeader[..4].SequenceEqual("PE\0\0"u8))
        {
            return null;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(peHeader[4..]);
    }

    /// <summary>Fills <paramref name="buffer"/> from <paramref name="offset"/>; false when the file ends first.</summary>
    private static bool ReadFully(SafeFileHandle file, Span<byte> buffer, long offset)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = RandomAccess.Read(file, buffer[filled..], offset + filled);
            if (read == 0)
            {
                return false;
            }

            filled += read;
        }

        return true;
    }
}
