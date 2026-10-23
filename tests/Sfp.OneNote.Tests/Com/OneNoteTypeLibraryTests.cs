using Serilog.Events;
using Sfp.OneNote.Com;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Writer;

namespace Sfp.OneNote.Tests.Com;

/// <summary>
/// Fix round 5b: OneNote's type library registration (HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-...}\1.1\0, win64 and win32),
/// which the Generate preflight checks before it starts OneNote. On the owner's PC OneNote started, then refused its
/// first call with a bare E_FAIL; a registration pointing at a file that no longer exists does that (the documented case
/// pointed at Office16\ONENOTE.EXE\3 without the root folder). Only registered entries whose files are all missing are an
/// error; nothing registered, or a stale entry beside a sound one, is a warning. The file checks run against a fake file
/// system.
/// </summary>
public class OneNoteTypeLibraryTests
{
    private const string Installed = @"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE";
    private const string WithoutRoot = @"C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE\3";

    private const string NotRegisteredWarning =
        @"OneNote's type library is not registered in this computer's registry (HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0 has no win64 or win32 entry). A virtualized Office may still carry it; if OneNote refuses the next call, run an Online Repair of Office (Settings, Apps, Microsoft Office, Modify).";

    [Fact]
    public void The_registration_key_is_onenotes_type_library_version_1_1()
    {
        OneNoteTypeLibrary.Guid.Should().Be("{0EA692EE-BB50-4E3C-AEF0-356D91732725}");
        OneNoteTypeLibrary.Version.Should().Be("1.1");
        OneNoteTypeLibrary.KeyPath.Should().Be(@"TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0");
        TypeLibraryCheck.None.Should().Be(new TypeLibraryCheck(null, null));
    }

    /// <summary>
    /// Review F3: nothing registered is a warning, not an error. An App-V or packaged Office can keep the type library in
    /// a virtual registry this process cannot see, so a missing key is not proof that OneNote cannot load it.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, "  ")]
    [InlineData("", "")]
    public void Neither_entry_registered_is_a_warning_not_an_error(string? win64, string? win32)
    {
        TypeLibraryCheck check = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(win64, win32), _ => throw new InvalidOperationException("no file to check"));

        check.Error.Should().BeNull();
        check.Warning.Should().Be(NotRegisteredWarning);
    }

    [Fact]
    public void A_win64_entry_whose_file_is_missing_with_win32_absent_is_an_error_naming_win64_and_its_value()
    {
        List<string> asked = new();

        TypeLibraryCheck check = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(WithoutRoot, null), Exists(asked, Installed));

        check.Error.Should().Be(
            @"OneNote's type library registration points at a file that does not exist: HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\win64 = C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE\3, so every automation call fails. Run an Online Repair of Office (Settings, Apps, Microsoft Office, Modify), or set that value to the installed ONENOTE.EXE, for example C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE\3, then retry.");
        check.Warning.Should().BeNull();
        asked.Should().Equal(new[] { @"C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE" }, "the file is checked without its resource index");
    }

    [Fact]
    public void A_win32_entry_whose_file_is_missing_with_win64_absent_is_an_error_naming_win32_only()
    {
        TypeLibraryCheck check = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(null, WithoutRoot), Exists(new List<string>(), Installed));

        check.Error.Should().Contain(@"\1.1\0\win32 = " + WithoutRoot + ", so every automation call fails.")
            .And.Contain("or set that value to the installed ONENOTE.EXE")
            .And.NotContain("win64");
        check.Warning.Should().BeNull();
    }

    [Fact]
    public void Both_entries_registered_with_both_files_missing_is_an_error_naming_both()
    {
        const string win32 = @"C:\Program Files (x86)\Microsoft Office\Office16\ONENOTE.EXE\3";
        List<string> asked = new();

        TypeLibraryCheck check = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(WithoutRoot, win32), Exists(asked, Installed));

        check.Error.Should().Be(
            @"OneNote's type library registration points at a file that does not exist: HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\win64 = C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE\3 and HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\win32 = C:\Program Files (x86)\Microsoft Office\Office16\ONENOTE.EXE\3, so every automation call fails. Run an Online Repair of Office (Settings, Apps, Microsoft Office, Modify), or set those values to the installed ONENOTE.EXE, for example C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE\3, then retry.");
        check.Warning.Should().BeNull();
        asked.Should().Equal(@"C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE", @"C:\Program Files (x86)\Microsoft Office\Office16\ONENOTE.EXE");
    }

    /// <summary>
    /// Review F3: a stale entry beside a sound one is a warning naming the stale entry and its value. A 32-bit OneNote reads
    /// only win32 and a 64-bit one may read a stale win64 entry; the bitness is not known, so the run goes on.
    /// </summary>
    [Fact]
    public void A_stale_win64_entry_beside_a_sound_win32_entry_is_a_warning_naming_the_stale_one()
    {
        TypeLibraryCheck check = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(WithoutRoot, Installed + @"\3"), Exists(new List<string>(), Installed));

        check.Error.Should().BeNull();
        check.Warning.Should().Be(
            @"OneNote's type library entry HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\win64 = C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE\3 points at a file that does not exist; the win32 entry is sound. If OneNote refuses the next call, set the stale value to the installed ONENOTE.EXE, for example C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE\3, or run an Online Repair of Office.");
    }

    [Fact]
    public void A_stale_win32_entry_beside_a_sound_win64_entry_is_a_warning_naming_win32()
    {
        TypeLibraryCheck check = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(Installed + @"\3", WithoutRoot), Exists(new List<string>(), Installed));

        check.Error.Should().BeNull();
        check.Warning.Should().StartWith(@"OneNote's type library entry HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\win32 = " + WithoutRoot + " points at a file that does not exist; the win64 entry is sound.");
    }

    [Theory]
    [InlineData(Installed + @"\3", null)]
    [InlineData(null, Installed + @"\3")]
    [InlineData(Installed + @"\3", Installed + @"\3")]
    public void Registered_entries_whose_files_all_exist_give_nothing_to_report(string? win64, string? win32)
    {
        TypeLibraryCheck check = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(win64, win32), Exists(new List<string>(), Installed));

        check.Should().Be(TypeLibraryCheck.None);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE\3", @"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE")]
    [InlineData(@"C:\Office\ONENOTE.EXE\12", @"C:\Office\ONENOTE.EXE")]
    [InlineData(@"""C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE""", @"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE")]
    [InlineData(@"  ""C:\Office\ONENOTE.EXE\3""  ", @"C:\Office\ONENOTE.EXE")]
    [InlineData(@"""C:\Office\ONENOTE.EXE""\3", @"C:\Office\ONENOTE.EXE")]
    [InlineData(@"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE", @"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE")]
    [InlineData(@"C:\Office\msonote.tlb", @"C:\Office\msonote.tlb")]
    [InlineData(@"C:\Office\ONENOTE.EXE\3a", @"C:\Office\ONENOTE.EXE\3a")]
    [InlineData(@"C:\Office\ONENOTE.EXE\", @"C:\Office\ONENOTE.EXE\")]
    public void File_path_trims_quotes_and_white_space_and_drops_only_a_resource_index(string value, string expected)
    {
        OneNoteTypeLibrary.FilePath(value).Should().Be(expected);
    }

    [Fact]
    public void File_path_expands_environment_variables()
    {
        const string variable = "SFP_ONENOTE_TYPELIB_TEST_ROOT";
        Environment.SetEnvironmentVariable(variable, @"C:\Program Files\Microsoft Office\root");
        try
        {
            OneNoteTypeLibrary.FilePath("%" + variable + @"%\Office16\ONENOTE.EXE\3").Should().Be(Installed);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [SkippableFact]
    public void Read_returns_the_registration_without_throwing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "The type library registration is read from the Windows registry.");

        // Skip.IfNot is not a guard the platform analyzer knows, so the check is repeated for it.
        if (OperatingSystem.IsWindows())
        {
            OneNoteTypeLibrary.Registration registration = OneNoteTypeLibrary.Read();

            registration.Should().NotBeNull();
        }
    }

    [Fact]
    public void The_production_locator_logs_what_it_read_on_windows_and_reports_nothing_elsewhere()
    {
        CapturingSink log = new();
        using OneNoteLocator locator = new(log.CreateLogger());

        TypeLibraryCheck check = locator.TypeLibrary;

        if (OperatingSystem.IsWindows())
        {
            LogEvent line = log.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Information).Subject;
            line.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Should().StartWith(@"OneNote type library HKEY_CLASSES_ROOT\")
                .And.Contain("win64 = ").And.Contain("win32 = ").And.Contain("file exists: ");
            check.Should().Be(OneNoteTypeLibrary.Check(OneNoteTypeLibrary.Read(), File.Exists));
            log.Events.Count(e => e.Level == LogEventLevel.Warning).Should().Be(check.Warning is null ? 0 : 1, "a warning is logged as well as reported");
        }
        else
        {
            check.Should().Be(TypeLibraryCheck.None, "off Windows there is no registry to read and no OneNote to start");
            log.Events.Should().BeEmpty();
        }
    }

    /// <summary>A file check that records every path it is asked about and finds only <paramref name="existing"/>.</summary>
    private static Func<string, bool> Exists(List<string> asked, params string[] existing) => path =>
    {
        asked.Add(path);
        return existing.Contains(path, StringComparer.Ordinal);
    };
}
