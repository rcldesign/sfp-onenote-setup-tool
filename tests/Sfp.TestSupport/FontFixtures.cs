using System.Buffers.Binary;
using System.Text;

namespace Sfp.TestSupport;

/// <summary>
/// Makes font files for tests. Bookman Old Style is not available off the owner's PC, so a test that needs "a machine
/// with Bookman installed" gets a copy of the bundled TeX Gyre Bonum whose OpenType name table says it is Bookman Old
/// Style. Only the name table is rewritten (appended at the end of the file and re-pointed from the table directory),
/// so the outlines and metrics are unchanged.
/// </summary>
public static class FontFixtures
{
    /// <summary>Writes a copy of an OpenType font with its family name replaced and returns the destination path.</summary>
    public static string WriteRenamedCopy(string sourcePath, string familyName, string destinationPath)
    {
        byte[] source = File.ReadAllBytes(sourcePath);
        ushort numTables = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(4));
        const int directoryStart = 12;
        int nameRecord = -1;
        for (int i = 0; i < numTables; i++)
        {
            int record = directoryStart + (16 * i);
            if (Encoding.ASCII.GetString(source, record, 4) == "name")
            {
                nameRecord = record;
                break;
            }
        }

        if (nameRecord < 0)
        {
            throw new InvalidDataException("No name table in " + sourcePath);
        }

        int nameOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(nameRecord + 8));
        int nameLength = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(nameRecord + 12));
        byte[] newName = RebuildNameTable(source.AsSpan(nameOffset, nameLength), familyName);

        int appendAt = (source.Length + 3) & ~3;
        byte[] result = new byte[appendAt + ((newName.Length + 3) & ~3)];
        Buffer.BlockCopy(source, 0, result, 0, source.Length);
        Buffer.BlockCopy(newName, 0, result, appendAt, newName.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(nameRecord + 4), TableChecksum(result.AsSpan(appendAt, (newName.Length + 3) & ~3)));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(nameRecord + 8), (uint)appendAt);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(nameRecord + 12), (uint)newName.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        File.WriteAllBytes(destinationPath, result);
        return destinationPath;
    }

    /// <summary>Writes renamed copies of the four bundled fallback faces into a folder, named like a Windows font install.</summary>
    public static string WriteRenamedFallbackFamily(string folder, string familyName)
    {
        Directory.CreateDirectory(folder);
        foreach ((string file, string suffix) in new[] { ("regular", ""), ("bold", "bd"), ("italic", "i"), ("bolditalic", "bi") })
        {
            string source = Path.Combine(TestPaths.TemplatesDir, "fonts", "texgyrebonum-" + file + ".otf");
            WriteRenamedCopy(source, familyName, Path.Combine(folder, "renamed" + suffix + ".otf"));
        }

        return folder;
    }

    private static byte[] RebuildNameTable(ReadOnlySpan<byte> table, string familyName)
    {
        ushort format = BinaryPrimitives.ReadUInt16BigEndian(table);
        if (format != 0)
        {
            throw new NotSupportedException("Only name table format 0 is handled; this font uses format " + format + ".");
        }

        ushort count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
        ushort stringOffset = BinaryPrimitives.ReadUInt16BigEndian(table[4..]);

        List<(ushort Platform, ushort Encoding, ushort Language, ushort NameId, byte[] Data)> records = [];
        Dictionary<(ushort, ushort, ushort), string> subfamilies = [];
        for (int r = 0; r < count; r++)
        {
            ReadOnlySpan<byte> rec = table[(6 + (12 * r))..];
            ushort platform = BinaryPrimitives.ReadUInt16BigEndian(rec);
            ushort encoding = BinaryPrimitives.ReadUInt16BigEndian(rec[2..]);
            ushort language = BinaryPrimitives.ReadUInt16BigEndian(rec[4..]);
            ushort nameId = BinaryPrimitives.ReadUInt16BigEndian(rec[6..]);
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(rec[8..]);
            ushort offset = BinaryPrimitives.ReadUInt16BigEndian(rec[10..]);
            byte[] data = table.Slice(stringOffset + offset, length).ToArray();
            records.Add((platform, encoding, language, nameId, data));
            if (nameId == 2)
            {
                subfamilies[(platform, encoding, language)] = Decode(platform, data);
            }
        }

        for (int i = 0; i < records.Count; i++)
        {
            (ushort platform, ushort encoding, ushort language, ushort nameId, byte[] data) = records[i];
            string? subfamily = subfamilies.GetValueOrDefault((platform, encoding, language));
            string? replacement = nameId switch
            {
                1 or 16 => familyName,
                4 => subfamily is null or "Regular" ? familyName : familyName + " " + subfamily,
                6 => familyName.Replace(" ", "", StringComparison.Ordinal) + "-" + (subfamily ?? "Regular").Replace(" ", "", StringComparison.Ordinal),
                _ => null,
            };
            if (replacement is not null)
            {
                records[i] = (platform, encoding, language, nameId, Encode(platform, replacement));
            }
        }

        using MemoryStream header = new();
        using MemoryStream storage = new();
        WriteU16(header, 0);
        WriteU16(header, (ushort)records.Count);
        WriteU16(header, (ushort)(6 + (12 * records.Count)));
        foreach ((ushort platform, ushort encoding, ushort language, ushort nameId, byte[] data) in records)
        {
            WriteU16(header, platform);
            WriteU16(header, encoding);
            WriteU16(header, language);
            WriteU16(header, nameId);
            WriteU16(header, (ushort)data.Length);
            WriteU16(header, (ushort)storage.Length);
            storage.Write(data);
        }

        storage.WriteTo(header);
        return header.ToArray();
    }

    private static string Decode(ushort platform, byte[] data) =>
        platform is 0 or 3 ? Encoding.BigEndianUnicode.GetString(data) : Encoding.Latin1.GetString(data);

    private static byte[] Encode(ushort platform, string text) =>
        platform is 0 or 3 ? Encoding.BigEndianUnicode.GetBytes(text) : Encoding.Latin1.GetBytes(text);

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static uint TableChecksum(ReadOnlySpan<byte> padded)
    {
        uint sum = 0;
        for (int i = 0; i + 4 <= padded.Length; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(padded[i..]));
        }

        return sum;
    }
}
