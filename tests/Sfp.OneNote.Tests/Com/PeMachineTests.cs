using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using Sfp.OneNote.Com;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Com;

/// <summary>
/// Fix round 5d: the bitness of an executable from its COFF machine field, read by hand (PeMachine), so the type library
/// check can tell a 64-bit OneNote from a 32-bit one without starting it. Synthetic images in a temporary folder cover
/// each machine and each way a file can fail to be a readable PE image; a real assembly is checked against the
/// framework's own PE reader.
/// </summary>
public sealed class PeMachineTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("pe-machine");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(SyntheticPe.Amd64, Bitness.X64)]
    [InlineData(SyntheticPe.Arm64, Bitness.X64)]
    [InlineData(SyntheticPe.I386, Bitness.X86)]
    public void The_machine_field_names_the_bitness(ushort machine, Bitness expected)
    {
        string path = SyntheticPe.Write(Path.Combine(_dir, "image.exe"), SyntheticPe.Image(machine));

        PeMachine.Read(path).Should().Be(expected);
    }

    /// <summary>The PE header is found through e_lfanew wherever it points, not at a fixed offset.</summary>
    [Theory]
    [InlineData(0x40)]
    [InlineData(0xF8)]
    [InlineData(0x400)]
    public void The_pe_header_is_read_where_e_lfanew_points(int peOffset)
    {
        string path = SyntheticPe.Write(Path.Combine(_dir, "image.exe"), SyntheticPe.Image(SyntheticPe.Amd64, peOffset));

        PeMachine.Read(path).Should().Be(Bitness.X64);
    }

    /// <summary>Any machine but x64, arm64 and x86 is unknown (here: Itanium, ARM Thumb-2, unknown, and x64 byte-swapped).</summary>
    [Theory]
    [InlineData((ushort)0x0200)]
    [InlineData((ushort)0x01C4)]
    [InlineData((ushort)0x0000)]
    [InlineData((ushort)0x6486)]
    public void Any_other_machine_is_unknown(ushort machine)
    {
        string path = SyntheticPe.Write(Path.Combine(_dir, "image.exe"), SyntheticPe.Image(machine));

        PeMachine.Read(path).Should().Be(Bitness.Unknown);
    }

    [Fact]
    public void A_file_that_is_not_an_executable_is_unknown()
    {
        string text = SyntheticPe.Write(Path.Combine(_dir, "notes.txt"), Encoding.ASCII.GetBytes(new string('x', 512)));
        string elf = SyntheticPe.Write(Path.Combine(_dir, "elf"), [0x7F, (byte)'E', (byte)'L', (byte)'F', .. new byte[256]]);

        PeMachine.Read(text).Should().Be(Bitness.Unknown);
        PeMachine.Read(elf).Should().Be(Bitness.Unknown);
    }

    /// <summary>Each field the read needs must be there: a file that ends before one of them is unknown.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(0x3C)]
    [InlineData(0x3F)]
    [InlineData(SyntheticPe.DefaultPeOffset)]
    [InlineData(SyntheticPe.DefaultPeOffset + 4)]
    [InlineData(SyntheticPe.DefaultPeOffset + 5)]
    public void A_file_cut_short_before_the_machine_field_is_unknown(int length)
    {
        byte[] image = SyntheticPe.Image(SyntheticPe.Amd64);
        string path = SyntheticPe.Write(Path.Combine(_dir, "short.exe"), image[..length]);

        PeMachine.Read(path).Should().Be(Bitness.Unknown);
        SyntheticPe.Write(path, image[..(SyntheticPe.DefaultPeOffset + 6)]);
        PeMachine.Read(path).Should().Be(Bitness.X64, "the machine field is the last byte the read needs");
    }

    [Fact]
    public void An_image_without_the_mz_signature_is_unknown()
    {
        byte[] image = SyntheticPe.Image(SyntheticPe.Amd64);
        image[0] = (byte)'Z';
        image[1] = (byte)'M';
        string path = SyntheticPe.Write(Path.Combine(_dir, "image.exe"), image);

        PeMachine.Read(path).Should().Be(Bitness.Unknown);
    }

    /// <summary>"PE\0\0" must be exact: a 16-bit NE header, or a signature with a nonzero tail, is not a PE header.</summary>
    [Theory]
    [InlineData("NE\0\0")]
    [InlineData("PE\0\u0001")]
    [InlineData("pe\0\0")]
    public void An_image_whose_pe_signature_is_wrong_is_unknown(string signature)
    {
        byte[] image = SyntheticPe.Image(SyntheticPe.Amd64);
        Encoding.Latin1.GetBytes(signature).CopyTo(image, SyntheticPe.DefaultPeOffset);
        string path = SyntheticPe.Write(Path.Combine(_dir, "image.exe"), image);

        PeMachine.Read(path).Should().Be(Bitness.Unknown);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(0x10000)]
    public void An_e_lfanew_that_is_negative_or_past_the_end_of_the_file_is_unknown(int peOffset)
    {
        byte[] image = SyntheticPe.Image(SyntheticPe.Amd64);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C, 4), peOffset);
        string path = SyntheticPe.Write(Path.Combine(_dir, "image.exe"), image);

        PeMachine.Read(path).Should().Be(Bitness.Unknown);
    }

    [Fact]
    public void A_missing_file_or_folder_is_unknown()
    {
        PeMachine.Read(Path.Combine(_dir, "ONENOTE.EXE")).Should().Be(Bitness.Unknown);
        PeMachine.Read(Path.Combine(_dir, "no such folder", "ONENOTE.EXE")).Should().Be(Bitness.Unknown);
    }

    /// <summary>A folder raises UnauthorizedAccessException when opened as a file, on Windows and elsewhere.</summary>
    [Fact]
    public void A_folder_is_unknown()
    {
        PeMachine.Read(_dir).Should().Be(Bitness.Unknown);
    }

    /// <summary>A file another handle holds without sharing raises IOException (a sharing violation, or its emulation off Windows).</summary>
    [Fact]
    public void A_file_held_open_without_sharing_is_unknown_and_reads_once_released()
    {
        string path = SyntheticPe.Write(Path.Combine(_dir, "ONENOTE.EXE"), SyntheticPe.Image(SyntheticPe.Amd64));

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            PeMachine.Read(path).Should().Be(Bitness.Unknown);
        }

        PeMachine.Read(path).Should().Be(Bitness.X64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ONENOTE\0.EXE")]
    public void A_blank_path_or_one_with_a_null_character_is_unknown(string path)
    {
        PeMachine.Read(path).Should().Be(Bitness.Unknown);
    }

    [Fact]
    public void A_null_path_is_refused()
    {
        Action read = () => PeMachine.Read(null!);

        read.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// A PE file a real toolchain wrote, checked against System.Reflection.PortableExecutable: this test assembly. Its
    /// COFF header names I386 because it is built for any CPU, so it reads as X86 (the machine field says nothing about a
    /// managed assembly's process; OneNote is a native executable, for which it is exact).
    /// </summary>
    [Fact]
    public void A_real_assembly_reads_as_the_machine_the_framework_pe_reader_finds()
    {
        string path = typeof(PeMachineTests).Assembly.Location;
        Machine machine;
        using (FileStream stream = File.OpenRead(path))
        using (PEReader reader = new(stream))
        {
            machine = reader.PEHeaders.CoffHeader.Machine;
        }

        Bitness expected = machine switch
        {
            Machine.Amd64 or Machine.Arm64 => Bitness.X64,
            Machine.I386 => Bitness.X86,
            _ => Bitness.Unknown,
        };

        PeMachine.Read(path).Should().Be(expected);
        expected.Should().NotBe(Bitness.Unknown, "the test assembly is an ordinary PE image");
    }

    /// <summary>
    /// The test host's own executable: on Windows a native PE image of the process's bitness (x64 and arm64 hosts both
    /// read as X64); elsewhere an ELF or Mach-O file, which has no MZ signature.
    /// </summary>
    [SkippableFact]
    public void The_test_hosts_executable_reads_as_the_process_bitness_on_windows_and_unknown_elsewhere()
    {
        string? host = Environment.ProcessPath;
        Skip.If(host is null, "The runtime does not know this process's executable.");

        Bitness expected = !OperatingSystem.IsWindows() ? Bitness.Unknown : Environment.Is64BitProcess ? Bitness.X64 : Bitness.X86;

        PeMachine.Read(host!).Should().Be(expected);
    }
}
