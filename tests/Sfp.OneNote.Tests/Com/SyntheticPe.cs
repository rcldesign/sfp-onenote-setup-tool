using System.Buffers.Binary;

namespace Sfp.OneNote.Tests.Com;

/// <summary>
/// Minimal PE images for the bitness tests, laid out from the PE format's own offsets rather than from the code under
/// test: "MZ" at 0, e_lfanew (the PE header's offset, a 32-bit little endian value) at 0x3C, "PE\0\0" at that offset and
/// the COFF header's 16-bit machine field right after it, followed by the rest of a 20-byte COFF header left zero.
/// </summary>
internal static class SyntheticPe
{
    /// <summary>IMAGE_FILE_MACHINE_I386.</summary>
    public const ushort I386 = 0x014C;

    /// <summary>IMAGE_FILE_MACHINE_AMD64.</summary>
    public const ushort Amd64 = 0x8664;

    /// <summary>IMAGE_FILE_MACHINE_ARM64.</summary>
    public const ushort Arm64 = 0xAA64;

    /// <summary>Where the PE header starts unless a test says otherwise (past the 64-byte DOS header, as linkers place it).</summary>
    public const int DefaultPeOffset = 0x80;

    /// <summary>The bytes of a minimal image for <paramref name="machine"/> with its PE header at <paramref name="peOffset"/>.</summary>
    public static byte[] Image(ushort machine, int peOffset = DefaultPeOffset)
    {
        byte[] image = new byte[peOffset + 4 + 20];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C, 4), peOffset);
        image[peOffset] = (byte)'P';
        image[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(peOffset + 4, 2), machine);
        return image;
    }

    /// <summary>Writes <paramref name="bytes"/> to <paramref name="path"/>, creating its folder, and returns the path.</summary>
    public static string Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
