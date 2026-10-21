using Sfp.App.ViewModels.Services;
using Sfp.Core.Dates;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using Sfp.OneNote.Generate;
using Sfp.Refresh;
using Sfp.Render;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Options.ViewModels;

/// <summary>The stand-ins the composition root uses until the integrator wires the real modules.</summary>
public class NotAvailableServicesTests
{
    [Fact]
    public void Generate_orchestrator_returns_a_failed_result_naming_the_card()
    {
        GenerateRequest request = new(new GenerateOptions { Start = new QuarterSpec(2027, 1) }, TestManifest.Shipped, TestPaths.TemplatesDir, false, null);

        GenerateResult result = new NotAvailableGenerateOrchestrator().Run(request, null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Phase.Should().Be(GeneratePhase.Failed);
        result.ExitCode.Should().Be(5);
        result.Error.Should().Be("The Generate orchestrator (M3.9) is not wired into the app yet.");
    }

    [Fact]
    public void Refresh_orchestrator_returns_a_failed_result_naming_the_card()
    {
        RefreshRequest request = new("SFP Q1 2027", null, false, 14, true, false, new DateOnly(2026, 10, 20));

        RefreshResult result = new NotAvailableRefreshOrchestrator().Run(request, null, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(5);
        result.Error.Should().Contain("M4.7");
    }

    [Fact]
    public void Registry_task_service_plan_builder_and_renderer_throw_with_the_card_named()
    {
        Action list = () => new NotAvailableNotebookRegistry().List(new[] { "x" });
        list.Should().Throw<NotSupportedException>().WithMessage("*M4.3*");

        NotAvailableScheduledTaskService tasks = new();
        tasks.IsRegistered("SFP Q1 2027").Should().BeFalse();
        Action register = () => tasks.Register("SFP Q1 2027", "x.exe", 14);
        Action remove = () => tasks.Remove("SFP Q1 2027");
        register.Should().Throw<NotSupportedException>().WithMessage("*M5.4*");
        remove.Should().Throw<NotSupportedException>().WithMessage("*M5.4*");

        Quarter q = new DateEngine().Build(new QuarterSpec(2027, 1));
        GenerateOptions o = new() { Start = q.Spec };
        Action plan = () => new NotAvailablePlanBuilder().Build(TestManifest.Shipped, q, o);
        plan.Should().Throw<NotSupportedException>().WithMessage("*M1.6*");

        PageSpec page = new(PageKey.Cover, PageType.Cover, "Cover", 1, 1, q.Start, new Dictionary<string, string>());
        NotebookPlan emptyPlan = new("SFP", q, Array.Empty<SectionSpec>(), Array.Empty<LinkSpec>());
        Action render = () => new NotAvailablePageRenderer().Render(page, emptyPlan, new RenderOptions("x.pdf", 220, o));
        render.Should().Throw<NotSupportedException>().WithMessage("*M2.9*");
    }

    [UiFact]
    public async Task Ui_progress_posts_every_report_through_the_ui_thread()
    {
        TestDispatcher ui = TestDispatcher.OfThisThread;
        List<int> seen = new();
        UiProgress<int> progress = new(ui, seen.Add);

        progress.Report(1);
        progress.Report(2);
        seen.Should().BeEmpty("a report waits in the dispatcher's queue until the UI thread is free");
        await ui.Drain();

        seen.Should().Equal(1, 2);
        ui.Posted.Should().Be(2);
    }

    [Theory]
    [InlineData(@"C:\Notebooks\SFP Q1 2027\", @"C:\Notebooks", "SFP Q1 2027")]
    [InlineData(@"C:\Notebooks\SFP Q1 2027", @"C:\Notebooks", "SFP Q1 2027")]
    [InlineData("C:/Notebooks/SFP Q1 2027", "C:/Notebooks", "SFP Q1 2027")]
    [InlineData(@"C:\SFP", @"C:\", "SFP")]
    [InlineData("/home/me/SFP", "/home/me", "SFP")]
    [InlineData("/SFP", "/", "SFP")]
    [InlineData("SFP", null, "SFP")]
    [InlineData(@"C:\logs\run.log", @"C:\logs", "run.log")]
    public void Path_text_splits_windows_and_posix_paths_the_same_way_on_every_os(string path, string? parent, string name)
    {
        PathText.ParentOf(path).Should().Be(parent);
        PathText.FileNameOf(path).Should().Be(name);
    }

    [Fact]
    public void Silent_run_log_factory_numbers_its_files()
    {
        SilentRunLogFactory factory = new();
        using IRunLog first = factory.Start("generate");
        using IRunLog second = factory.Start("refresh");

        first.Path.Should().EndWith("sfp2onenote-generate-1.log");
        second.Path.Should().EndWith("sfp2onenote-refresh-2.log");
        first.Logger.Should().NotBeNull();
    }
}
