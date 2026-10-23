using Sfp.OneNote.Com;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Com;

/// <summary>
/// Fix round 5d: the executable registered as OneNote's COM server (the LocalServer32 of OneNote.Application's CLSID) and
/// its bitness, which the type library check needs. On the owner's PC LocalServer32 is
/// C:\Program Files\Microsoft Office\Root\Office16\ONENOTE.EXE, a 64-bit OneNote. ServerPath takes the executable from the
/// registered value against a fake file system; FromLocalServer32 runs it against synthetic executables on disk;
/// PreferOwnView decides when the 32-bit registry view is read.
/// </summary>
public sealed class OneNoteServerTests : IDisposable
{
    private const string Server = @"C:\Program Files\Microsoft Office\Root\Office16\ONENOTE.EXE";

    private readonly string _dir = TestPaths.NewTempDir("onenote-server");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(Server)]
    [InlineData(@"""" + Server + @"""")]
    [InlineData("  \"" + Server + "\"  ")]
    [InlineData("\t" + Server + " ")]
    public void A_value_naming_an_existing_file_gives_it_with_quotes_and_white_space_trimmed(string value)
    {
        List<string> asked = new();

        OneNoteServer.ServerPath(value, Exists(asked, Server)).Should().Be(Server);
        asked.Should().Equal(Server);
    }

    /// <summary>When the whole value is not a file, the part before its first " /" or " -" argument separator is tried.</summary>
    [Theory]
    [InlineData(Server + " /automation")]
    [InlineData(@"""" + Server + @""" /automation")]
    [InlineData(Server + " -Embedding")]
    [InlineData(@"  """ + Server + @""" -Embedding  ")]
    [InlineData(Server + " -Embedding /automation")]
    [InlineData(Server + " /automation -Embedding")]
    public void A_value_with_arguments_gives_the_executable_before_the_first_separator(string value)
    {
        OneNoteServer.ServerPath(value, Exists(new List<string>(), Server)).Should().Be(Server);
    }

    /// <summary>
    /// The first separator decides, " /" or " -", whichever comes first; the whole value is asked about first and the part
    /// before that separator second, and nothing else.
    /// </summary>
    [Fact]
    public void The_cut_is_at_the_first_separator_of_either_kind()
    {
        const string exe = @"C:\Office\ONENOTE.EXE";
        List<string> slashFirst = new();
        List<string> dashFirst = new();

        OneNoteServer.ServerPath(exe + " /a -b", Exists(slashFirst, exe)).Should().Be(exe);
        OneNoteServer.ServerPath(exe + " -b /a", Exists(dashFirst, exe)).Should().Be(exe);

        slashFirst.Should().Equal(exe + " /a -b", exe);
        dashFirst.Should().Equal(exe + " -b /a", exe);
    }

    /// <summary>A value that names an existing file as a whole is never cut, even when it holds a separator.</summary>
    [Fact]
    public void A_whole_value_that_exists_is_kept_although_it_holds_a_separator()
    {
        const string withDash = @"C:\Program Files\Office - Copy\ONENOTE.EXE";
        List<string> asked = new();

        OneNoteServer.ServerPath(withDash, Exists(asked, withDash, @"C:\Program Files\Office")).Should().Be(withDash);
        asked.Should().Equal(withDash);
    }

    /// <summary>When neither the whole value nor its part before the separator exists, the trimmed value is given.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE", @"C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE")]
    [InlineData(@" ""C:\Office16\ONENOTE.EXE"" ", @"C:\Office16\ONENOTE.EXE")]
    [InlineData(@"C:\Office16\ONENOTE.EXE /automation", @"C:\Office16\ONENOTE.EXE /automation")]
    public void A_value_naming_no_existing_file_gives_the_trimmed_value(string value, string expected)
    {
        OneNoteServer.ServerPath(value, Exists(new List<string>(), Server)).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData(" \" \" ")]
    public void A_blank_value_names_no_server_and_no_file_is_checked(string value)
    {
        OneNoteServer.ServerPath(value, _ => throw new InvalidOperationException("no file to check")).Should().BeNull();
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        Action noValue = () => OneNoteServer.ServerPath(null!, _ => true);
        Action noCheck = () => OneNoteServer.ServerPath(Server, null!);

        noValue.Should().Throw<ArgumentNullException>();
        noCheck.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The registered value through to the bitness, on disk: a quoted path with an argument, in a folder whose name has a
    /// space, reads as the synthetic executable's bitness.
    /// </summary>
    [Theory]
    [InlineData(SyntheticPe.Amd64, Bitness.X64)]
    [InlineData(SyntheticPe.I386, Bitness.X86)]
    [InlineData(SyntheticPe.Arm64, Bitness.X64)]
    public void A_registered_value_gives_the_executable_and_its_bitness(ushort machine, Bitness expected)
    {
        string exe = SyntheticPe.Write(Path.Combine(_dir, "Microsoft Office", "ONENOTE.EXE"), SyntheticPe.Image(machine));

        OneNoteServer.FromLocalServer32(exe).Should().Be(new OneNoteServer(exe, expected));
        OneNoteServer.FromLocalServer32("\"" + exe + "\" /automation").Should().Be(new OneNoteServer(exe, expected));
        OneNoteServer.FromLocalServer32(exe + " -Embedding").Should().Be(new OneNoteServer(exe, expected));
    }

    [Fact]
    public void A_registered_value_whose_file_is_missing_or_not_an_executable_keeps_the_path_with_an_unknown_bitness()
    {
        string missing = Path.Combine(_dir, "Office16", "ONENOTE.EXE");
        string text = SyntheticPe.Write(Path.Combine(_dir, "Office 16", "ONENOTE.EXE"), "not an executable"u8.ToArray());

        OneNoteServer.FromLocalServer32(missing).Should().Be(new OneNoteServer(missing, Bitness.Unknown));
        OneNoteServer.FromLocalServer32(text).Should().Be(new OneNoteServer(text, Bitness.Unknown));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\"\"")]
    public void No_registered_value_is_no_server(string? value)
    {
        OneNoteServer.FromLocalServer32(value).Should().Be(OneNoteServer.NotFound);
        OneNoteServer.NotFound.Should().Be(new OneNoteServer(null, Bitness.Unknown));
    }

    /// <summary>
    /// The 32-bit registry view (where a 32-bit Office on 64-bit Windows can register its COM server) is read only when
    /// this process's own view has no LocalServer32 value for OneNote (no CLSID value or no LocalServer32, both read as
    /// null, or a blank value); then its value is the one used. A value in the own view is used as it is.
    /// </summary>
    [Fact]
    public void The_32_bit_view_is_read_only_when_the_processs_own_view_has_no_server()
    {
        const string x86 = @"C:\Program Files (x86)\Microsoft Office\root\Office16\ONENOTE.EXE";
        int asked = 0;

        string? Registry32()
        {
            asked++;
            return x86;
        }

        OneNoteServer.PreferOwnView(Server, Registry32).Should().Be(Server);
        asked.Should().Be(0, "a server in the process's own view is used without looking at the 32-bit view");
        OneNoteServer.PreferOwnView(null, Registry32).Should().Be(x86);
        OneNoteServer.PreferOwnView(" ", Registry32).Should().Be(x86);
        asked.Should().Be(2);
        OneNoteServer.PreferOwnView(null, () => null).Should().BeNull("neither view has a server");
    }

    [SkippableFact]
    public void Read_returns_the_server_without_throwing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "OneNote's COM server is read from the Windows registry.");

        // Skip.IfNot is not a guard the platform analyzer knows, so the check is repeated for it.
        if (OperatingSystem.IsWindows())
        {
            OneNoteServer server = OneNoteServer.Read();

            if (server.Path is null)
            {
                server.Should().Be(OneNoteServer.NotFound, "without a registered server the bitness is unknown");
            }
        }
    }

    /// <summary>A file check that records every path it is asked about and finds only <paramref name="existing"/>.</summary>
    private static Func<string, bool> Exists(List<string> asked, params string[] existing) => path =>
    {
        asked.Add(path);
        return existing.Contains(path, StringComparer.Ordinal);
    };
}
