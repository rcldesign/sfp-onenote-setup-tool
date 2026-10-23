using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.OneNote.Com;
using Sfp.OneNote.Generate;
using Sfp.OneNote.Tests.Com;
using Sfp.OneNote.Tests.Writer;
using Sfp.OneNote.Writer;
using Sfp.TestSupport;

namespace Sfp.OneNote.Tests.Generate;

/// <summary>Every row of the PRD's pre-run checks, each driven by an injected OneNote locator and file system.</summary>
public class GeneratePreflightTests
{
    private static readonly Manifest M = Support.TestManifest.Load();
    private static readonly string Out = Path.Combine("/notebooks", "out");

    private sealed class Harness
    {
        public FakeOneNoteApp App { get; } = new();

        public FakeOneNoteLocator Locator { get; set; }

        public FakePreflightFileSystem Fs { get; } = new();

        public FakeManifestValidator Validator { get; set; } = new();

        public List<string> FontWarnings { get; } = new();

        public Harness()
        {
            Locator = new FakeOneNoteLocator(true, App);
        }

        public GeneratePreflight Build() => new(Validator, Locator, Fs, new SidecarStore(), new DateEngine(), FontWarnings);

        public static GenerateRequest Request(GenerateOptions? options = null, bool renderOnly = false, string? renderDir = null) =>
            new(options ?? Options(), M, TestPaths.TemplatesDir, renderOnly, renderDir);

        public static GenerateOptions Options() => new() { Start = new QuarterSpec(2027, 1), PdfPath = "/planner/SFP_No_Dates.pdf", OutputFolder = Out };
    }

    [Fact]
    public void A_ready_machine_passes_with_no_errors_and_no_warnings()
    {
        Harness h = new();

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Ok.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        result.Warnings.Should().BeEmpty();
        result.NoOpenNotebooks.Should().BeFalse();
        h.Locator.TypeLibrary.Should().Be(TypeLibraryCheck.None, "the fake's type library check has nothing to report by default");
        h.Locator.OpenCalls.Should().Be(1);
        h.App.CallsOf("GetHierarchy").Should().Equal(":Notebooks");
        h.Validator.Paths.Should().Equal("/planner/SFP_No_Dates.pdf");
        h.Fs.EnsuredWritable.Should().Equal(Out);
    }

    [Fact]
    public void OneNote_not_installed_gives_the_prd_message_with_exit_3_and_never_opens_it()
    {
        Harness h = new() { Locator = new FakeOneNoteLocator(false, () => throw new InvalidOperationException("must not be called")) };

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Contain((OneNoteUnavailableException.UserMessage, 3));
        h.Locator.OpenCalls.Should().Be(0);
    }

    /// <summary>
    /// Fix round 5b: OneNote's type library registration points at a file that does not exist (the documented case: the
    /// Office16 folder without root), so OneNote would start and then refuse every call, as on the owner's PC. The
    /// preflight reports the check's error as its one error, exit 3, without starting OneNote. A machine without OneNote
    /// still gets the install message alone: the install check comes first.
    /// </summary>
    [Fact]
    public void A_broken_type_library_registration_is_the_one_error_with_exit_3_and_onenote_is_never_opened()
    {
        TypeLibraryCheck broken = OneNoteTypeLibrary.Check(
            new OneNoteTypeLibrary.Registration(@"C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE\3", null), _ => false);
        string error = broken.Error!;
        Harness h = new();
        h.Locator.TypeLibrary = broken;

        PreflightResult result = h.Build().Check(Harness.Request());

        error.Should().StartWith("OneNote's type library registration points at a file that does not exist: ");
        result.Errors.Should().Equal((error, 3));
        result.Warnings.Should().BeEmpty();
        result.NoOpenNotebooks.Should().BeFalse();
        h.Locator.OpenCalls.Should().Be(0, "OneNote is not started when no registered entry names a file that exists");
        h.App.CallsOf("GetHierarchy").Should().BeEmpty();

        Harness notInstalled = new() { Locator = new FakeOneNoteLocator(false, () => throw new InvalidOperationException("must not be called")) { TypeLibrary = broken } };
        notInstalled.Build().Check(Harness.Request()).Errors.Should().Equal((OneNoteUnavailableException.UserMessage, 3));
    }

    /// <summary>
    /// Review F3: nothing registered in the registry this process sees is a warning, not an error: an App-V or packaged
    /// Office can keep the type library in a virtual registry. The run goes on, OneNote is opened, and a OneNote that
    /// answers passes with the warning.
    /// </summary>
    [Fact]
    public void An_unregistered_type_library_is_a_warning_and_onenote_is_still_opened()
    {
        TypeLibraryCheck unregistered = OneNoteTypeLibrary.Check(new OneNoteTypeLibrary.Registration(null, null), _ => false);
        string warning = unregistered.Warning!;
        Harness h = new();
        h.Locator.TypeLibrary = unregistered;

        PreflightResult result = h.Build().Check(Harness.Request());

        warning.Should().StartWith("OneNote's type library is not registered in this computer's registry");
        result.Errors.Should().BeEmpty();
        result.Ok.Should().BeTrue();
        result.Warnings.Should().Equal(warning);
        h.Locator.OpenCalls.Should().Be(1);
        h.App.CallsOf("GetHierarchy").Should().Equal(":Notebooks");
    }

    /// <summary>
    /// Review F3: a stale entry beside a sound one is a warning and the run goes on. When OneNote then refuses the first
    /// call (a 64-bit OneNote may read the stale win64 entry), the first-call message is the error and the warning is
    /// reported beside it.
    /// </summary>
    [Fact]
    public void A_stale_type_library_entry_beside_a_sound_one_warns_and_a_refused_first_call_is_reported_beside_it()
    {
        TypeLibraryCheck stale = OneNoteTypeLibrary.Check(
            new OneNoteTypeLibrary.Registration(@"C:\Program Files\Microsoft Office\Office16\ONENOTE.EXE\3", @"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE\3"),
            path => path == @"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE");
        string warning = stale.Warning!;
        Harness answering = new();
        answering.Locator.TypeLibrary = stale;
        Harness refusing = new();
        refusing.Locator.TypeLibrary = stale;
        refusing.App.GetHierarchyFailure = () => OneNoteHResults.Map("GetHierarchy", new System.Runtime.InteropServices.COMException("refused", OneNoteHResults.Fail), null);

        PreflightResult passed = answering.Build().Check(Harness.Request());
        PreflightResult failed = refusing.Build().Check(Harness.Request());

        stale.Error.Should().BeNull();
        warning.Should().StartWith(@"OneNote's type library entry HKEY_CLASSES_ROOT\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\win64 = ");
        passed.Ok.Should().BeTrue();
        passed.Warnings.Should().Equal(warning);
        answering.Locator.OpenCalls.Should().Be(1);
        failed.Errors.Should().Equal((GenerateText.FirstCallFailedMessage + " OneNote call GetHierarchy failed: E_FAIL (0x80004005): refused", 3));
        failed.Warnings.Should().Equal(warning);
    }

    [Fact]
    public void No_open_notebook_is_recorded_but_does_not_block_on_its_own()
    {
        Harness h = new();
        h.App.PreexistingNotebooks.Clear();

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Ok.Should().BeTrue("a signed-in OneNote with every notebook closed lists none either; the first OpenHierarchy decides");
        result.Errors.Should().BeEmpty();
        result.NoOpenNotebooks.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void An_installed_onenote_that_fails_to_start_is_told_to_match_elevation_not_to_install_onenote()
    {
        FakeActivator activator = new(new FakeOneNoteTarget())
        {
            Failure = new System.Runtime.InteropServices.COMException("Server execution failed", OneNoteHResults.ServerExecFailed),
        };
        using OneNoteSession session = new(activator, new Support.CapturingSink().CreateLogger(), TimeSpan.Zero);
        Harness h = new() { Locator = new FakeOneNoteLocator(true, session) };

        PreflightResult result = h.Build().Check(Harness.Request());

        (string message, int exitCode) = result.Errors.Should().ContainSingle().Subject;
        exitCode.Should().Be(3);
        message.Should().StartWith(OneNoteUnavailableException.NotStartedMessage).And.Contain("CO_E_SERVER_EXEC_FAILURE (0x80080005)");
        message.Should().NotContain(OneNoteUnavailableException.InstallUrl, "the ProgID resolved, so OneNote is installed");
        activator.ActivationCount.Should().Be(1);
    }

    /// <summary>
    /// onenote-5: CoCreateInstance failing with E_ACCESSDENIED (a DCOM launch permission) or ERROR_FILE_NOT_FOUND (a
    /// registration whose server is gone) reaches the code as UnauthorizedAccessException or FileNotFoundException, not as
    /// a COMException; it still says OneNote is installed but could not be started, with exit 3. Round 4 onenote-6: so
    /// does E_OUTOFMEMORY (the server could not be started, as on an exhausted desktop heap), raised as OutOfMemoryException.
    /// </summary>
    [Theory]
    [InlineData(unchecked((int)0x80070005), typeof(UnauthorizedAccessException))]
    [InlineData(unchecked((int)0x80070002), typeof(FileNotFoundException))]
    [InlineData(unchecked((int)0x8007000E), typeof(OutOfMemoryException))]
    public void An_activation_failure_the_runtime_raises_as_a_dotnet_exception_says_onenote_could_not_be_started_with_exit_3(int hresult, Type raisedAs)
    {
        Exception raised = System.Runtime.InteropServices.Marshal.GetExceptionForHR(hresult)!;
        raised.Should().BeOfType(raisedAs, "the runtime raises this HRESULT as a .NET exception, not as a COMException");
        raised.HResult.Should().Be(hresult);
        FakeActivator activator = new(new FakeOneNoteTarget()) { Failure = raised };
        using OneNoteSession session = new(activator, new Support.CapturingSink().CreateLogger(), TimeSpan.Zero);
        Harness h = new() { Locator = new FakeOneNoteLocator(true, session) };

        PreflightResult result = h.Build().Check(Harness.Request());

        (string message, int exitCode) = result.Errors.Should().ContainSingle().Subject;
        exitCode.Should().Be(3);
        message.Should().StartWith(OneNoteUnavailableException.NotStartedMessage + " activate failed with " + OneNoteHResults.Describe(hresult));
        message.Should().NotContain(OneNoteUnavailableException.InstallUrl, "the ProgID resolved, so OneNote is installed");
        activator.ActivationCount.Should().Be(1);
    }

    [Fact]
    public void A_com_session_that_cannot_start_is_reported_as_unavailable()
    {
        Harness h = new() { Locator = new FakeOneNoteLocator(true, () => throw new OneNoteUnavailableException("activate", OneNoteHResults.ServerExecFailed, "OneNote desktop is required. CreateInstance failed", null)) };

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().ContainSingle(e => e.ExitCode == 3 && e.Message.Contains("CreateInstance failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hierarchy_call_that_fails_for_another_reason_is_reported_with_its_message()
    {
        Harness h = new();
        h.App.GetHierarchyFailure = () => new OneNoteException("GetHierarchy", OneNoteHResults.NotYetSynchronized, "OneNote call GetHierarchy failed: hrNotYetSynchronized (0x8004201D)", null);

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().ContainSingle(e => e.ExitCode == 3 && e.Message.StartsWith("OneNote could not be reached", StringComparison.Ordinal));
        result.Errors[0].Message.Should().NotContain(GenerateText.DialogOrSignInMessage);
    }

    /// <summary>
    /// Fix round 5: OneNote started (activation answered) and its first call, GetHierarchy, failed with a bare E_FAIL, as
    /// on the owner's PC, whose OneNote desktop (Office LTSC 2021) is signed in and in daily use. The preflight gives the
    /// restart and elevation remedy ahead of the call's own message, exit 3; not "could not be reached", not the sign-in
    /// text and not the install link, since OneNote is installed and running. Fix round 5b: the message names OneNote
    /// failing to load its own type library as the first suspect (pointing at the log's type library line), the
    /// elevation second, and an Online Repair of Office as the last remedy; it names no code, since the call's own
    /// message that follows carries it.
    /// </summary>
    [Fact]
    public void A_bare_e_fail_at_the_first_call_gives_the_restart_and_elevation_remedy_with_exit_3()
    {
        Harness h = new();
        h.App.GetHierarchyFailure = () => new OneNoteException("GetHierarchy", OneNoteHResults.Fail, "OneNote call GetHierarchy failed: E_FAIL (0x80004005)", null);

        PreflightResult result = h.Build().Check(Harness.Request());

        (string message, int exitCode) = result.Errors.Should().ContainSingle().Subject;
        exitCode.Should().Be(3);
        message.Should().Be(GenerateText.FirstCallFailedMessage + " OneNote call GetHierarchy failed: E_FAIL (0x80004005)");
        message.Should().NotContain("could not be reached")
            .And.NotContain(GenerateText.DialogOrSignInMessage)
            .And.NotContain(OneNoteUnavailableException.InstallUrl, "OneNote answered activation, so it is installed");
        GenerateText.FirstCallFailedMessage.Should().Be(
            "OneNote started but refused the first request. This is what OneNote answers when it cannot load its own type library (see the type library line in the log) or when this tool and OneNote run at different elevations. Close OneNote completely (also the OneNote icon in the notification area), run both at the same elevation, then retry. If it keeps failing, run an Online Repair of Office from Settings, Apps.");
    }

    /// <summary>
    /// Review F4: TYPE_E_LIBNOTREGISTERED and TYPE_E_CANTLOADLIBRARY at the first call, after OneNote started, take the
    /// E_FAIL branch: the first-call message, then the call's own message naming the code, exit 3.
    /// </summary>
    [Theory]
    [InlineData(unchecked((int)0x80004005), "E_FAIL (0x80004005)")]
    [InlineData(unchecked((int)0x8002801D), "TYPE_E_LIBNOTREGISTERED (0x8002801D)")]
    [InlineData(unchecked((int)0x80029C4A), "TYPE_E_CANTLOADLIBRARY (0x80029C4A)")]
    public void A_first_call_refused_with_e_fail_or_a_type_library_failure_gives_the_first_call_message_with_exit_3(int hresult, string described)
    {
        OneNoteException refusal = OneNoteHResults.Map("GetHierarchy", new System.Runtime.InteropServices.COMException("refused", hresult), null);
        Harness h = new();
        h.App.GetHierarchyFailure = () => refusal;

        PreflightResult result = h.Build().Check(Harness.Request());

        refusal.Should().BeOfType<OneNoteException>("Map leaves these a plain OneNoteException for the preflight to word");
        result.Errors.Should().Equal((GenerateText.FirstCallFailedMessage + " OneNote call GetHierarchy failed: " + described + ": refused", 3));
    }

    /// <summary>
    /// Fix round 5, the owner's run end to end: the COM session activates OneNote, GetHierarchy throws a COMException with
    /// E_FAIL, the session maps it to a plain OneNoteException without a retry, and the preflight gives the restart and
    /// elevation remedy with exit 3.
    /// </summary>
    [Fact]
    public void A_bare_e_fail_from_the_com_session_after_onenote_started_gives_the_restart_and_elevation_remedy()
    {
        FakeOneNoteTarget target = new();
        target.FailNext(OneNoteHResults.Fail);
        FakeActivator activator = new(target);
        using OneNoteSession session = new(activator, new Support.CapturingSink().CreateLogger(), TimeSpan.Zero);
        Harness h = new() { Locator = new FakeOneNoteLocator(true, session) };

        PreflightResult result = h.Build().Check(Harness.Request());

        (string message, int exitCode) = result.Errors.Should().ContainSingle().Subject;
        exitCode.Should().Be(3);
        message.Should().StartWith(GenerateText.FirstCallFailedMessage + " OneNote call GetHierarchy failed: E_FAIL (0x80004005)");
        activator.ActivationCount.Should().Be(1);
        target.CallCount("GetHierarchy").Should().Be(1, "E_FAIL is not retried");
    }

    /// <summary>
    /// Review F1: OneNote that cannot load its type library can fail GetIDsOfNames itself, which the COM binder raises as
    /// MissingMemberException ("Could not get dispatch ID for GetHierarchy (error: 0x80004005)."). The session maps it by
    /// the HRESULT it names, so the preflight gives the first-call message, not "no automation interface", exit 3.
    /// </summary>
    [Fact]
    public void A_dispatch_id_failure_naming_e_fail_from_the_com_binder_gives_the_first_call_message_with_exit_3()
    {
        FakeOneNoteTarget target = new();
        target.FailNextWith(new MissingMemberException("Could not get dispatch ID for GetHierarchy (error: 0x80004005)."));
        FakeActivator activator = new(target);
        using OneNoteSession session = new(activator, new Support.CapturingSink().CreateLogger(), TimeSpan.Zero);
        Harness h = new() { Locator = new FakeOneNoteLocator(true, session) };

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Equal((GenerateText.FirstCallFailedMessage + " OneNote call GetHierarchy failed: E_FAIL (0x80004005): Could not get dispatch ID for GetHierarchy (error: 0x80004005).", 3));
        result.Errors[0].Message.Should().NotContain(OneNoteUnavailableException.NoAutomationMessage);
        target.CallCount("GetHierarchy").Should().Be(1, "E_FAIL is not retried");
    }

    /// <summary>
    /// Fix round 5: a OneNote.Application object without the automation members (an object with no GetHierarchy, so the
    /// runtime binder finds no such member, as it does for a COM object whose IDispatch does not know the name) is
    /// reported as not OneNote desktop or a damaged install, naming the call, exit 3.
    /// </summary>
    [Fact]
    public void An_onenote_object_without_the_automation_members_says_it_is_not_onenote_desktop_with_exit_3()
    {
        FakeActivator activator = new(new NoAutomationTarget());
        using OneNoteSession session = new(activator, new Support.CapturingSink().CreateLogger(), TimeSpan.Zero);
        Harness h = new() { Locator = new FakeOneNoteLocator(true, session) };

        PreflightResult result = h.Build().Check(Harness.Request());

        (string message, int exitCode) = result.Errors.Should().ContainSingle().Subject;
        exitCode.Should().Be(3);
        message.Should().StartWith(OneNoteUnavailableException.NoAutomationMessage + " GetHierarchy failed with DISP_E_UNKNOWNNAME (0x80020006): ")
            .And.Contain("does not contain a definition for 'GetHierarchy'");
        message.Should().NotContain("could not be reached")
            .And.NotContain(GenerateText.FirstCallFailedMessage)
            .And.NotContain(OneNoteUnavailableException.NotStartedMessage);
        activator.ActivationCount.Should().Be(1);
    }

    public static TheoryData<string, OneNoteException> DialogRefusals() => new()
    {
        { "busy twice", new OneNoteBusyException("GetHierarchy", OneNoteHResults.RpcCallRejected, null) },
        { "a modal dialog twice", new OneNoteBusyException("GetHierarchy", OneNoteHResults.AppInModalUI, null) },
        { "hrAppInModalUI", new OneNoteException("GetHierarchy", OneNoteHResults.AppInModalUI, "OneNote call GetHierarchy failed: hrAppInModalUI (0x80042030)", null) },
    };

    [Theory]
    [MemberData(nameof(DialogRefusals))]
    public void A_dialog_holding_onenote_at_the_first_call_says_to_close_it_or_sign_in_with_exit_3(string why, OneNoteException refusal)
    {
        Harness h = new();
        h.App.PreexistingNotebooks.Clear();
        h.App.GetHierarchyFailure = () => refusal;

        PreflightResult result = h.Build().Check(Harness.Request());

        (string message, int exitCode) = result.Errors.Should().ContainSingle(why).Subject;
        exitCode.Should().Be(3);
        message.Should().StartWith(GenerateText.DialogOrSignInMessage + " ").And.Contain(OneNoteHResults.Hex(refusal.HResult));
        message.Should().NotContain("could not be reached");
        GenerateText.DialogOrSignInMessage.Should().Contain("sign in once, then retry");
    }

    [Fact]
    public void Pdf_mismatches_are_reported_one_per_line_with_exit_2()
    {
        Harness h = new() { Validator = new FakeManifestValidator(new ValidationResult(new[] { "Expected 132 pages, found 130", "Page 17 does not contain 'Monday'" }, new[] { "No probe text for weekly page 'sun'; page not checked." })) };

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Equal(("Expected 132 pages, found 130", 2), ("Page 17 does not contain 'Monday'", 2));
        result.Warnings.Should().Equal("No probe text for weekly page 'sun'; page not checked.");
    }

    [Theory]
    [InlineData(1899, 1)]
    [InlineData(2200, 1)]
    [InlineData(2027, 13)]
    [InlineData(2027, 0)]
    public void Start_month_outside_the_range_is_refused(int year, int month)
    {
        Harness h = new();

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { Start = new QuarterSpec(year, month) }));

        result.Errors.Should().Contain((GenerateText.StartMonthOutOfRange(year, month), 2));
    }

    [Theory]
    [InlineData("SFP:Q1")]
    [InlineData("a/b")]
    [InlineData("ThisNameIsFarTooLongToBeAcceptedBecauseItGoesWellPastSixtyFourCharacters")]
    public void A_notebook_name_breaking_the_rule_is_refused(string name)
    {
        Harness h = new();

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { NotebookName = name }));

        result.Errors.Should().Contain((NotebookNames.RuleMessage, 2));
    }

    [Fact]
    public void Output_folder_that_cannot_be_written_is_refused_and_the_default_is_used_when_none_is_given()
    {
        Harness h = new();
        h.Fs.Unwritable[h.Fs.DefaultOutputFolder] = "Access to the path is denied.";

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { OutputFolder = null }));

        result.Errors.Should().Contain((GenerateText.FolderNotWritable(h.Fs.DefaultOutputFolder, "Access to the path is denied."), 2));
        h.Fs.EnsuredWritable.Should().Equal(h.Fs.DefaultOutputFolder);
    }

    [Fact]
    public void A_folder_under_onedrive_only_warns()
    {
        Harness h = new();
        h.Fs.OneDrive.Add("/home/user/OneDrive");
        string folder = "/home/user/OneDrive/Documents/Notebooks";

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { OutputFolder = folder }));

        result.Ok.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Be(GenerateText.OneDriveWarning(folder));
    }

    [Fact]
    public void An_existing_notebook_folder_of_the_same_name_blocks_with_the_prd_message()
    {
        Harness h = new();
        h.Fs.Directories.Add(Path.Combine(Out, "SFP Q1 2027"));

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Should().Equal((GenerateText.Collision("SFP Q1 2027"), 2));
    }

    [Fact]
    public void An_existing_sidecar_of_the_same_name_blocks_too()
    {
        Harness h = new();
        h.Fs.Files.Add(new SidecarStore().PathFor(Path.Combine(Out, "My Quarter")));

        PreflightResult result = h.Build().Check(Harness.Request(Harness.Options() with { NotebookName = "My Quarter" }));

        result.Errors.Should().Equal((GenerateText.Collision("My Quarter"), 2));
    }

    [Fact]
    public void Font_fallback_warnings_are_passed_through()
    {
        Harness h = new();
        h.FontWarnings.Add("Font 'Bookman Old Style' is not installed; using the bundled 'TeX Gyre Bonum' instead.");

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Ok.Should().BeTrue();
        result.Warnings.Should().Equal(h.FontWarnings);
    }

    [Fact]
    public void Render_only_skips_the_onenote_and_notebook_checks_and_needs_a_writable_output_folder()
    {
        Harness h = new() { Locator = new FakeOneNoteLocator(false, () => throw new InvalidOperationException("must not be called")) };
        h.Fs.Directories.Add(Path.Combine(Out, "SFP Q1 2027"));

        PreflightResult ok = h.Build().Check(Harness.Request(renderOnly: true, renderDir: "/png"));
        PreflightResult missing = h.Build().Check(Harness.Request(renderOnly: true, renderDir: null));
        h.Fs.Unwritable["/png"] = "read only";
        PreflightResult unwritable = h.Build().Check(Harness.Request(renderOnly: true, renderDir: "/png"));

        ok.Ok.Should().BeTrue(string.Join("; ", ok.Errors.Select(e => e.Message)));
        ok.NoOpenNotebooks.Should().BeFalse("render-only never asks OneNote");
        missing.Errors.Should().ContainSingle(e => e.ExitCode == 2 && e.Message.Contains("output folder", StringComparison.Ordinal));
        unwritable.Errors.Should().Equal((GenerateText.FolderNotWritable("/png", "read only"), 2));
        h.Locator.OpenCalls.Should().Be(0);
    }

    [Fact]
    public void Errors_keep_the_prd_order_onenote_first_then_validation()
    {
        Harness h = new()
        {
            Locator = new FakeOneNoteLocator(false, () => throw new InvalidOperationException("must not be called")),
            Validator = new FakeManifestValidator(ValidationResult.Fail("Expected 132 pages, found 130")),
        };
        h.Fs.Directories.Add(Path.Combine(Out, "SFP Q1 2027"));

        PreflightResult result = h.Build().Check(Harness.Request());

        result.Errors.Select(e => e.ExitCode).Should().Equal(3, 2, 2);
        result.Errors[0].Message.Should().Be(OneNoteUnavailableException.UserMessage);
        result.Errors[1].Message.Should().Be("Expected 132 pages, found 130");
        result.Errors[2].Message.Should().Be(GenerateText.Collision("SFP Q1 2027"));
    }

    [Theory]
    [InlineData("<one:Notebooks xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\"/>", false)]
    [InlineData("<one:Notebooks xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\"><one:Notebook name=\"A\" ID=\"{a}\"/></one:Notebooks>", true)]
    [InlineData("", false)]
    [InlineData("not xml", false)]
    public void Has_open_notebook_reads_the_hierarchy(string xml, bool expected)
    {
        GeneratePreflight.HasOpenNotebook(xml).Should().Be(expected);
    }

    [Fact]
    public void Production_file_system_creates_the_folder_probes_a_write_and_lists_onedrive_folders()
    {
        PreflightFileSystem fs = new();
        string folder = Path.Combine(TestPaths.NewTempDir("preflight"), "new", "nested");

        fs.EnsureWritable(folder).Should().BeNull();
        fs.DirectoryExists(folder).Should().BeTrue();
        Directory.EnumerateFiles(folder).Should().BeEmpty("the probe file is removed");
        fs.FileExists(Path.Combine(folder, "nothing")).Should().BeFalse();
        fs.OneDriveFolders.Should().NotContainNulls();
        fs.DefaultOutputFolder.Should().EndWith(OutputFolders.DefaultFolderName);
    }
}
