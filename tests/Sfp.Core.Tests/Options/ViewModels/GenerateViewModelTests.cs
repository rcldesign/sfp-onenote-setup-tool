using Sfp.App.ViewModels.Generate;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>M6.2: the Generate tab's fields, defaults, inline messages, size estimate and the hand-off to previews and the run.</summary>
public class GenerateViewModelTests
{
    private readonly Harness _h = new();

    private static async Task Settle(GenerateViewModel vm)
    {
        await vm.PendingPdfCheck;
        await vm.Previews.Pending;
    }

    [Fact]
    public async Task Defaults_follow_the_PRD_table_when_no_settings_exist()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        await Settle(vm);

        vm.Month.Should().Be(1);
        vm.YearText.Should().Be("2027");
        vm.Start.Should().Be(new QuarterSpec(2027, 1));
        vm.NotebookName.Should().Be("SFP Q1 2027");
        vm.DefaultNotebookName.Should().Be("SFP Q1 2027");
        vm.OutputFolder.Should().Be(Harness.DefaultFolder);
        vm.DateFormat.Label.Should().Be("MM / DD / YY");
        vm.WeeksRemaining.Should().Be(WeeksRemainingMode.Corrected);
        vm.CalendarMonths.Should().Be("Three");
        vm.Dpi.Should().Be(220);
        vm.SizeEstimate.Should().Be("About 42 MB for 144 pages at 220 DPI");
        vm.TimeBlockStart.Should().Be(6);
        vm.TimeBlockEnd.Should().Be(22);
        vm.PageScaleText.Should().Be("1");
        vm.Links.Should().BeTrue();
        vm.PdfError.Should().Be("Choose the SFP template PDF.");
        vm.CanGenerate.Should().BeFalse();
        vm.FieldsEnabled.Should().BeTrue();
        vm.Previews.Day.Status.Should().Contain("template PDF");
    }

    [Fact]
    public async Task Saved_settings_fill_every_field_and_the_pdf_check_starts()
    {
        AppSettings saved = new()
        {
            PdfPath = Harness.Pdf,
            Start = new QuarterSpec(2027, 4),
            NotebookName = "My planner",
            OutputFolder = @"D:\Notebooks",
            DateOrder = DateFieldOrder.DayMonthYear,
            PadDateFields = false,
            WeeksRemaining = WeeksRemainingMode.AsPrinted,
            Dpi = 300,
            GroupWeeks = true,
            Links = false,
            HideTitle = true,
            PageSize = PageSizeMode.Custom,
            PageScale = 0.72,
            SixthRow = SixthRowMode.Drop,
            OutOfMonthDays = OutOfMonthDayStyle.Blank,
            ReflectionDateIsWeekStart = true,
            IncludeOutOfQuarterDays = true,
            TimeBlocks = new TimeBlockWindow(7, 19),
        };
        using GenerateViewModel vm = _h.NewGenerate();

        vm.LoadFrom(saved);
        await Settle(vm);

        vm.PdfPath.Should().Be(Harness.Pdf);
        vm.Start.Should().Be(new QuarterSpec(2027, 4));
        vm.NotebookName.Should().Be("My planner");
        vm.OutputFolder.Should().Be(@"D:\Notebooks");
        vm.DateFormat.Label.Should().Be("D / M / YY");
        vm.WeeksRemaining.Should().Be(WeeksRemainingMode.AsPrinted);
        vm.Dpi.Should().Be(300);
        vm.GroupWeeks.Should().BeTrue();
        vm.Links.Should().BeFalse();
        vm.HideTitle.Should().BeTrue();
        vm.CustomPageSize.Should().BeTrue();
        vm.PageScaleText.Should().Be("0.72");
        vm.SixthRow.Should().Be(SixthRowMode.Drop);
        vm.OutOfMonthDays.Should().Be(OutOfMonthDayStyle.Blank);
        vm.ReflectionDateIsWeekStart.Should().BeTrue();
        vm.IncludeOutOfQuarterDays.Should().BeTrue();
        vm.TimeBlockStart.Should().Be(7);
        vm.TimeBlockEnd.Should().Be(19);
        vm.SizeEstimate.Should().Be("About 74 MB for 145 pages at 300 DPI (above the 60 MB target)");
        _h.PdfValidator.Calls.Should().Be(1);
        _h.PdfValidator.LastPath.Should().Be(Harness.Pdf);
        vm.PdfError.Should().BeNull();
        vm.PdfPending.Should().BeNull();
        vm.CanGenerate.Should().BeTrue();
        vm.ToSettings(Array.Empty<string>()).Should().BeEquivalentTo(saved, o => o.Excluding(s => s.KnownOutputFolders));
    }

    [Fact]
    public async Task Invalid_saved_values_fall_back_to_the_defaults()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.LoadFrom(new AppSettings { Start = new QuarterSpec(1850, 1), Dpi = 72, NotebookName = "  ", OutputFolder = "" });
        await Settle(vm);

        vm.Start.Should().Be(new QuarterSpec(2027, 1));
        vm.Dpi.Should().Be(220);
        vm.NotebookName.Should().Be("SFP Q1 2027");
        vm.OutputFolder.Should().Be(Harness.DefaultFolder);
    }

    [Fact]
    public async Task Notebook_name_follows_the_default_until_the_user_edits_it()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        await Settle(vm);

        vm.Month = 4;
        vm.NotebookName.Should().Be("SFP Q2 2027");
        vm.YearText = "2028";
        vm.NotebookName.Should().Be("SFP Q2 2028");
        vm.Month = 2;
        vm.NotebookName.Should().Be("SFP Feb to Apr 2028");

        vm.NotebookName = "Mine";
        vm.Month = 7;
        vm.NotebookName.Should().Be("Mine");
        vm.DefaultNotebookName.Should().Be("SFP Q3 2028");

        vm.NotebookName = "SFP Q3 2028";
        vm.Month = 10;
        vm.NotebookName.Should().Be("SFP Q4 2028", "typing the default again re-attaches the name to the date");
    }

    [Fact]
    public async Task A_bad_year_shows_the_start_message_and_blocks_generate_without_touching_the_name()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = Harness.Pdf;
        await Settle(vm);
        vm.CanGenerate.Should().BeTrue();

        vm.YearText = "20x7";

        vm.Start.Should().BeNull();
        vm.StartError.Should().Be("Year must be a whole number between 1900 and 2199.");
        vm.NotebookName.Should().Be("SFP Q1 2027");
        vm.CanGenerate.Should().BeFalse();
        vm.Previews.Day.Status.Should().Be("Fix the start month to see previews.");
        vm.ToOptions().Start.Should().Be(new QuarterSpec(2027, 1), "the last valid start stands in");

        vm.YearText = "2199";
        vm.StartError.Should().BeNull();
        vm.NotebookName.Should().Be("SFP Q1 2199");
        vm.CanGenerate.Should().BeTrue();
    }

    [Theory]
    [InlineData("1899")]
    [InlineData("2200")]
    [InlineData("1")]
    [InlineData("202")]
    public async Task A_year_outside_1900_to_2199_shows_the_start_message_and_blocks_generate_without_throwing(string year)
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = Harness.Pdf;
        await Settle(vm);
        vm.CanGenerate.Should().BeTrue();

        Action type = () => vm.YearText = year;

        type.Should().NotThrow("the date engine refuses these years, so the form must never hand them to it");
        vm.Start.Should().BeNull();
        vm.StartError.Should().Be("Year must be a whole number between 1900 and 2199.");
        vm.CanGenerate.Should().BeFalse();
        vm.GenerateCommand.CanExecute(null).Should().BeFalse();
        vm.NotebookName.Should().Be("SFP Q1 2027", "the name keeps the default of the last valid start");
        vm.SizeEstimate.Should().Be("About 42 MB for 144 pages at 220 DPI", "the estimate keeps the last valid start");
        vm.ToOptions().Start.Should().Be(new QuarterSpec(2027, 1));
        vm.Previews.Day.Status.Should().Be("Fix the start month to see previews.");

        await vm.GenerateCommand.ExecuteAsync(null);
        _h.Generator.Runs.Should().Be(0, "clicking Generate does nothing while the year is out of range");

        Action other = () => vm.Month = 4;
        other.Should().NotThrow();
        vm.StartError.Should().Be("Year must be a whole number between 1900 and 2199.");

        vm.YearText = "2027";
        vm.Start.Should().Be(new QuarterSpec(2027, 4));
        vm.StartError.Should().BeNull();
        vm.CanGenerate.Should().BeTrue();
        await Settle(vm);
    }

    [Fact]
    public async Task Pdf_check_runs_in_the_background_and_shows_the_mismatch()
    {
        _h.PdfValidator.Result = ValidationResult.Fail("Expected 132 pages, found 130");
        using GenerateViewModel vm = _h.NewGenerate();

        vm.PdfPath = Harness.Pdf;
        vm.PdfPending.Should().Be("Checking the PDF against the template manifest...");
        vm.CanGenerate.Should().BeFalse();
        await Settle(vm);

        vm.PdfPending.Should().BeNull();
        vm.PdfError.Should().Be("Expected 132 pages, found 130");
        vm.CanGenerate.Should().BeFalse();
        vm.Previews.Day.Status.Should().Contain("matches the manifest");

        _h.PdfValidator.Result = ValidationResult.Ok;
        _h.Paths.Files.Add(Harness.Pdf + " ");
        vm.PdfPath = Harness.Pdf + " ";
        await Settle(vm);
        vm.PdfError.Should().BeNull();
        vm.CanGenerate.Should().BeTrue();
        vm.Previews.Day.HasImage.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_pdf_is_reported_at_once_and_the_check_still_settles()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = @"C:\nope.pdf";

        vm.PdfError.Should().Be(@"PDF not found: C:\nope.pdf");
        await Settle(vm);
        vm.PdfError.Should().Be(@"PDF not found: C:\nope.pdf");
        _h.PdfValidator.Calls.Should().Be(0, "the validator is not opened for a missing file");
    }

    [Fact]
    public async Task Every_other_row_shows_its_message_inline()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = Harness.Pdf;
        await Settle(vm);

        vm.NotebookName = "SFP: Q1";
        vm.NameError.Should().Contain("cannot contain :");
        vm.NotebookName = "SFP Q1 2027";
        vm.NameError.Should().BeNull();

        _h.Paths.Directories.Add(Path.Combine(Harness.DefaultFolder, "SFP Q1 2027"));
        vm.NotebookName = "SFP Q1 2027 ";
        vm.NameError.Should().Be("A notebook named SFP Q1 2027 already exists here. Rename or choose another folder.");
        vm.NotebookName = "SFP Q1 2027 (2)";
        vm.NameError.Should().BeNull();

        vm.OutputFolder = @"C:\Users\me\OneDrive\Notebooks";
        vm.FolderWarning.Should().Contain("OneDrive");
        vm.FolderError.Should().BeNull();
        vm.CanGenerate.Should().BeTrue();
        vm.OutputFolder = "";
        vm.FolderError.Should().Be("Choose an output folder.");
        vm.OutputFolder = Harness.DefaultFolder;

        vm.PageScaleText = "abc";
        vm.PageScaleError.Should().Contain("Page scale");
        vm.PageScaleText = "0.72";
        vm.PageScaleError.Should().BeNull();

        vm.TimeBlockEnd = 6;
        vm.TimeBlocksError.Should().Contain("time-block window");
        vm.TimeBlockEnd = 22;
        vm.TimeBlocksError.Should().BeNull();

        vm.Dpi = 144;
        vm.SizeEstimate.Should().Be("About 20 MB for 144 pages at 144 DPI");
        vm.CanGenerate.Should().BeTrue();
    }

    [Fact]
    public async Task Input_changes_raise_InputChanged_but_loading_does_not()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        int raised = 0;
        vm.InputChanged += (_, _) => raised++;

        vm.LoadFrom(new AppSettings { Dpi = 144 });
        raised.Should().Be(0);

        vm.Dpi = 300;
        raised.Should().Be(1);
        vm.Dpi = 300;
        raised.Should().Be(1, "setting the same value is not a change");
        await Settle(vm);
    }

    [Fact]
    public async Task Previews_re_render_when_the_date_or_format_changes_but_not_for_dpi()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = Harness.Pdf;
        await Settle(vm);
        vm.Previews.RenderCount.Should().Be(1);
        vm.Previews.Day.Title.Should().Be("Fri Jan 1");
        vm.Previews.CalendarMonth.Title.Should().Be("January 2027");
        vm.Previews.Cover.Title.Should().Be("Cover");

        vm.Month = 4;
        await Settle(vm);
        vm.Previews.RenderCount.Should().Be(2);
        vm.Previews.Day.Title.Should().Be("Thu Apr 1");

        vm.DateFormat = DateFormatChoice.All[2];
        await Settle(vm);
        vm.Previews.RenderCount.Should().Be(3);
        _h.Plans.LastOptions!.DateOrder.Should().Be(DateFieldOrder.DayMonthYear);

        vm.Dpi = 300;
        await Settle(vm);
        vm.Previews.RenderCount.Should().Be(3, "the DPI does not change what the thumbnails show");
    }

    [Fact]
    public async Task Browse_buttons_set_the_field_only_when_a_choice_was_made()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        await Settle(vm);

        _h.Dialogs.NextFile = null;
        vm.BrowsePdfCommand.Execute(null);
        vm.PdfPath.Should().Be("");
        _h.Dialogs.NextFile = Harness.Pdf;
        vm.BrowsePdfCommand.Execute(null);
        vm.PdfPath.Should().Be(Harness.Pdf);
        _h.Dialogs.Titles.Should().AllSatisfy(t => t.Should().Contain("template PDF"));

        _h.Dialogs.NextFolder = @"D:\Notebooks";
        vm.BrowseFolderCommand.Execute(null);
        vm.OutputFolder.Should().Be(@"D:\Notebooks");
        _h.Dialogs.LastInitialPath.Should().Be(Harness.DefaultFolder);
        await Settle(vm);
    }

    [Fact]
    public async Task ToOptions_maps_every_field()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = " " + Harness.Pdf + " ";
        vm.DateFormat = DateFormatChoice.For(DateFieldOrder.DayMonthYear, padded: false);
        vm.CustomPageSize = true;
        vm.PageScaleText = "0.72";
        vm.TimeBlockStart = 8;
        vm.TimeBlockEnd = 18;
        vm.GroupWeeks = true;
        vm.HideTitle = true;
        vm.IncludeOutOfQuarterDays = true;

        GenerateOptions o = vm.ToOptions();

        o.PdfPath.Should().Be(Harness.Pdf);
        o.Start.Should().Be(new QuarterSpec(2027, 1));
        o.NotebookName.Should().Be("SFP Q1 2027");
        o.OutputFolder.Should().Be(Harness.DefaultFolder);
        o.DateOrder.Should().Be(DateFieldOrder.DayMonthYear);
        o.PadDateFields.Should().BeFalse();
        o.PageSize.Should().Be(PageSizeMode.Custom);
        o.PageScale.Should().Be(0.72);
        o.TimeBlocks.Should().Be(new TimeBlockWindow(8, 18));
        o.GroupWeeks.Should().BeTrue();
        o.HideTitle.Should().BeTrue();
        o.IncludeOutOfQuarterDays.Should().BeTrue();
        vm.ToRequest().Manifest.Should().BeSameAs(_h.Manifest);
        vm.ToRequest().TemplatesDir.Should().Be(_h.Env.TemplatesDir);
        vm.ToRequest().RenderOnly.Should().BeFalse();
        await Settle(vm);
    }

    [Fact]
    public async Task Generate_runs_the_orchestrator_with_the_request_and_locks_the_fields_meanwhile()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = Harness.Pdf;
        await Settle(vm);
        bool? enabledDuringRun = null;
        bool? canGenerateDuringRun = null;
        _h.Generator.OnRun = () =>
        {
            enabledDuringRun = vm.FieldsEnabled;
            canGenerateDuringRun = vm.CanGenerate;
        };

        vm.GenerateCommand.CanExecute(null).Should().BeTrue();
        await vm.GenerateCommand.ExecuteAsync(null);

        _h.Generator.Runs.Should().Be(1);
        _h.Generator.LastRequest!.Options.NotebookName.Should().Be("SFP Q1 2027");
        enabledDuringRun.Should().BeFalse();
        canGenerateDuringRun.Should().BeFalse();
        vm.FieldsEnabled.Should().BeTrue();
        vm.CanGenerate.Should().BeTrue();
        vm.Run.ShowSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Generate_is_refused_while_the_form_is_invalid()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        await Settle(vm);

        vm.GenerateCommand.CanExecute(null).Should().BeFalse();
        await vm.GenerateCommand.ExecuteAsync(null);
        _h.Generator.Runs.Should().Be(0);
    }

    [Fact]
    public async Task After_a_successful_run_the_name_collision_rule_blocks_a_second_run_into_the_same_folder()
    {
        using GenerateViewModel vm = _h.NewGenerate();
        vm.PdfPath = Harness.Pdf;
        await Settle(vm);
        _h.Generator.OnRun = () => _h.Paths.Directories.Add(Path.Combine(Harness.DefaultFolder, "SFP Q1 2027"));

        await vm.GenerateCommand.ExecuteAsync(null);

        vm.NameError.Should().Contain("already exists");
        vm.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void Dropdown_contents_match_the_PRD_table()
    {
        using GenerateViewModel vm = _h.NewGenerate();

        vm.Months.Select(m => m.Label).First().Should().Be("January");
        vm.Months.Should().HaveCount(12);
        vm.DateFormats.Select(f => f.Label).Should().Equal("MM / DD / YY", "M / D / YY", "DD / MM / YY", "D / M / YY");
        vm.WeeksRemainingChoices.Select(c => c.Value).Should().Equal(WeeksRemainingMode.Corrected, WeeksRemainingMode.AsPrinted);
        vm.CalendarMonthChoices.Should().Equal("Three");
        vm.DpiChoices.Should().Equal(144, 220, 300);
        vm.SixthRowChoices.Select(c => c.Value).Should().Equal(SixthRowMode.Stack, SixthRowMode.Drop);
        vm.OutOfMonthDayChoices.Select(c => c.Value).Should().Equal(OutOfMonthDayStyle.Greyed, OutOfMonthDayStyle.Blank);
        vm.StartHours.Should().Equal(Enumerable.Range(0, 24));
        vm.EndHours.Should().Equal(Enumerable.Range(1, 24));
        DateFormatChoice.For(DateFieldOrder.MonthDayYear, padded: true).Label.Should().Be("MM / DD / YY");
        new Choice<int>(1, "One").ToString().Should().Be("One");
    }
}
