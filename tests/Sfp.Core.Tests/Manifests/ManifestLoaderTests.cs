using System.Text.Json;
using System.Text.Json.Nodes;
using Sfp.Core.Manifests;
using Sfp.TestSupport;

namespace Sfp.Core.Tests.Manifests;

public class ManifestLoaderTests : IDisposable
{
    private readonly string _tempDir = TestPaths.NewTempDir("manifest");
    private readonly ManifestLoader _loader = new();

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LoadShipped_finds_the_manifest_in_the_templates_folder()
    {
        Manifest m = _loader.LoadShipped(TestPaths.TemplatesDir);

        m.TemplateId.Should().Be("sfp-quarterly-2023");
        m.PageCount.Should().Be(132);
        m.PageWidthPt.Should().Be(1112);
        m.PageHeightPt.Should().Be(770);
        m.PageTypes.Weekly.Pages.Should().HaveCount(9);
        m.PageTypes.Weekly.PdfPageFor(1, "mon").Should().Be(17);
        m.Notebook.Sections.Should().HaveCount(6);
    }

    [Fact]
    public void Enum_fields_read_camel_case_names()
    {
        Manifest m = _loader.Load(TestPaths.ManifestPath);

        m.Slots.Date.Fields.Select(f => f.Align).Should().Equal(TextAlign.Right, TextAlign.Center, TextAlign.Left);
        m.Links.DailyDate.Align.Should().Be(TextAlign.Right);
        m.Links.CalendarDay.Align.Should().Be(TextAlign.Center);
    }

    [Fact]
    public void Shipped_manifest_round_trips_through_the_json_options_without_losing_a_key()
    {
        Manifest loaded = _loader.Load(TestPaths.ManifestPath);
        string serialized = ManifestLoader.Serialize(loaded);

        JsonNode original = JsonNode.Parse(File.ReadAllText(TestPaths.ManifestPath))!;
        JsonNode written = JsonNode.Parse(serialized)!;
        JsonTrees.MissingOrDifferent(original, written).Should().BeEmpty("every key of the shipped file must survive a load and serialize");

        Manifest reloaded = ManifestLoader.Parse(serialized, "serialized");
        string again = ManifestLoader.Serialize(reloaded);
        JsonTrees.Differences(written, JsonNode.Parse(again)!).Should().BeEmpty("serialization must be stable");
        again.Should().Be(serialized);
    }

    [Fact]
    public void Serialized_manifest_uses_camel_case_keys_and_camel_case_enum_names()
    {
        string serialized = ManifestLoader.Serialize(_loader.Load(TestPaths.ManifestPath));
        JsonObject root = JsonNode.Parse(serialized)!.AsObject();

        root.ContainsKey("pageSizePt").Should().BeTrue();
        root["slots"]!["date"]!["fields"]![0]!["align"]!.GetValue<string>().Should().Be("right");
        root["links"]!["dailyDate"]!.AsObject().ContainsKey("rect").Should().BeFalse("computed properties are not part of the file");
        serialized.Should().Contain("Day {dayOfQuarter} of {quarterLength} · Week", "non-ASCII text is written readable, not escaped");
    }

    [Fact]
    public void Json_options_are_shared_and_read_only()
    {
        ManifestLoader.JsonOptions.IsReadOnly.Should().BeTrue();
        ManifestLoader.JsonOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        ManifestLoader.JsonOptions.ReadCommentHandling.Should().Be(JsonCommentHandling.Skip);
        ManifestLoader.JsonOptions.AllowTrailingCommas.Should().BeTrue();
    }

    [Fact]
    public void Unknown_token_in_a_page_title_throws_naming_the_token_and_the_pattern()
    {
        string path = WriteModified(root => root["notebook"]!["sections"]![1]!["pages"]![3]!["title"] = "Objective {Foo}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>()
            .WithMessage("*{Foo}*")
            .WithMessage("*'Objective {Foo}'*")
            .WithMessage("*notebook.sections[1].pages[3].title*");
    }

    [Fact]
    public void Unknown_token_in_a_section_name_throws_naming_the_token()
    {
        string path = WriteModified(root => root["notebook"]!["sections"]![3]!["name"] = "Week {WW}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*{WW}*").WithMessage("*notebook.sections[3].name*");
    }

    [Fact]
    public void Unknown_token_in_the_notebook_name_pattern_throws()
    {
        string path = WriteModified(root => root["notebook"]!["namePattern"] = "SFP {Quarter}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*{Quarter}*").WithMessage("*notebook.namePattern*");
    }

    [Fact]
    public void Note_token_is_allowed_on_each_note_pages_only()
    {
        Manifest shipped = _loader.Load(TestPaths.ManifestPath);
        shipped.Notebook.Sections[5].Pages[0].EachNote.Should().BeTrue("the Notes section uses {Note} with eachNote");

        string path = WriteModified(root => root["notebook"]!["sections"]![0]!["pages"]![0]!["title"] = "Cover {Note}");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*{Note}*");
    }

    [Fact]
    public void User_manifest_with_one_changed_baseline_merges_over_the_shipped_one_and_changes_nothing_else()
    {
        string userPath = Path.Combine(_tempDir, "user.json");
        File.WriteAllText(userPath, "{ \"$comment\": \"nudge the date baseline\", \"slots\": { \"date\": { \"baselineY\": 38.5 } } }");

        Manifest merged = _loader.Merge(TestPaths.ManifestPath, userPath);

        merged.Slots.Date.BaselineY.Should().Be(38.5);
        JsonNode shippedTree = JsonNode.Parse(ManifestLoader.Serialize(_loader.Load(TestPaths.ManifestPath)))!;
        JsonNode mergedTree = JsonNode.Parse(ManifestLoader.Serialize(merged))!;
        JsonTrees.Differences(shippedTree, mergedTree).Should().ContainSingle().Which.Should().StartWith("$.slots.date.baselineY");
    }

    [Fact]
    public void Merge_nodes_merges_objects_recursively_and_replaces_arrays_and_scalars()
    {
        JsonNode shipped = JsonNode.Parse("{ \"a\": { \"x\": 1, \"y\": [1, 2, 3], \"z\": { \"p\": true } }, \"b\": \"keep\" }")!;
        JsonNode user = JsonNode.Parse("{ \"a\": { \"x\": 2, \"y\": [9], \"z\": { \"q\": false } }, \"c\": null }")!;

        JsonNode merged = ManifestLoader.MergeNodes(shipped, user)!;

        merged["a"]!["x"]!.GetValue<int>().Should().Be(2);
        merged["a"]!["y"]!.AsArray().Select(n => n!.GetValue<int>()).Should().Equal(9);
        merged["a"]!["z"]!["p"]!.GetValue<bool>().Should().BeTrue();
        merged["a"]!["z"]!["q"]!.GetValue<bool>().Should().BeFalse();
        merged["b"]!.GetValue<string>().Should().Be("keep");
        merged.AsObject().ContainsKey("c").Should().BeTrue();
        merged["c"].Should().BeNull();
        shipped["a"]!["x"]!.GetValue<int>().Should().Be(1, "inputs are not modified");
    }

    [Fact]
    public void Merge_with_a_user_array_replaces_the_shipped_array_whole()
    {
        string userPath = Path.Combine(_tempDir, "user.json");
        File.WriteAllText(userPath, "{ \"pageTypes\": { \"calendar\": { \"pdfPages\": [13] } } }");

        Manifest merged = _loader.Merge(TestPaths.ManifestPath, userPath);

        merged.PageTypes.Calendar.PdfPages.Should().Equal(13);
        merged.PageTypes.Calendar.Probe.Should().Be("Monthly Calendar", "sibling keys of the merged object are kept");
    }

    [Fact]
    public void Comment_keys_are_ignored_anywhere_in_a_manifest()
    {
        string path = WriteModified(root =>
        {
            root["slots"]!["date"]!.AsObject()["$comment"] = "nested note";
            root["links"]!["prev"]!.AsObject()["$comment"] = "another";
        });

        Manifest m = _loader.Load(path);
        Manifest shipped = _loader.Load(TestPaths.ManifestPath);

        m.Slots.Date.BaselineY.Should().Be(shipped.Slots.Date.BaselineY);
        ManifestLoader.Serialize(m).Should().NotContain("$comment");
    }

    [Fact]
    public void Trailing_commas_and_comments_are_tolerated()
    {
        string json = File.ReadAllText(TestPaths.ManifestPath).TrimEnd().TrimEnd('}') + "  // trailing note\n /* block */ , }";
        string path = Path.Combine(_tempDir, "commented.json");
        File.WriteAllText(path, json);

        Manifest m = _loader.Load(path);

        m.PageCount.Should().Be(132);
    }

    [Fact]
    public void Unknown_property_throws_naming_the_property_and_its_path()
    {
        string path = WriteModified(root => root["slots"]!["date"]!.AsObject()["baslineY"] = 38);

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*baslineY*").WithMessage("*$.slots.date*");
    }

    [Fact]
    public void Missing_required_property_throws_a_manifest_exception()
    {
        string path = WriteModified(root => root["slots"]!["date"]!.AsObject().Remove("baselineY"));

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*baselineY*");
    }

    [Theory]
    [InlineData("colors", "greyedInkOpacity")]
    [InlineData("colors", "prefillInkOpacity")]
    [InlineData("colors", "eventInkOpacity")]
    [InlineData("colors", "counterInkOpacity")]
    [InlineData("slots.dayGrid", "columnPitch")]
    [InlineData("slots.dayGrid", "rowPitch")]
    [InlineData("slots.dayGrid", "dayHeaderY")]
    [InlineData("slots.timeBlocks", "printedLabelSizePt")]
    [InlineData("slots.timeBlocks", "printedRows")]
    [InlineData("slots.timeBlocks", "printedPitch")]
    [InlineData("slots.timeBlocks", "eventLineHeight")]
    [InlineData("slots.habitLegend", "columns")]
    [InlineData("slots.habitLegend", "lineHeight")]
    [InlineData("onenote", "pageDateTimeHour")]
    [InlineData("slots.date", "slashPrinted")]
    [InlineData("slots.weeksRemaining", "printed")]
    [InlineData("slots.timeBlocks", "printed")]
    public void Calibrated_geometry_has_no_code_default_so_a_missing_key_is_a_load_error(string parent, string key)
    {
        string path = WriteModified(root =>
        {
            JsonNode node = parent.Split('.').Aggregate(root, (n, part) => n[part]!);
            node.AsObject().Remove(key).Should().BeTrue("the shipped manifest carries the key");
        });

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage($"*{key}*");
    }

    [Fact]
    public void Calibrated_geometry_comes_from_the_manifest_values()
    {
        Manifest m = _loader.Load(TestPaths.ManifestPath);

        m.Slots.TimeBlocks.PrintedRows.Should().Be(25, "the real PDF prints 25 hour rules (12, 1 ... 12, 1 ... 12)");
        m.Slots.TimeBlocks.PrintedPitch.Should().Be(22.6);
        m.Slots.TimeBlocks.PrintedLabelSizePt.Should().Be(7);
        m.Slots.DayGrid.ColumnPitch.Should().Be(121.1);
        m.Slots.DayGrid.RowPitch.Should().Be(120);
        m.Slots.DayGrid.DayHeaderY.Should().Be(92);
        m.Colors.GreyedInkOpacity.Should().Be(0.4);
        m.Colors.PrefillInkOpacity.Should().Be(0.6);
        m.Colors.EventInkOpacity.Should().Be(0.7);
        m.Colors.CounterInkOpacity.Should().Be(0.6);
        m.Slots.TimeBlocks.EventLineHeight.Should().Be(10);
        m.Slots.HabitLegend.Columns.Should().Be(8);
        m.Slots.HabitLegend.LineHeight.Should().Be(11);
        m.OneNote.PageDateTimeHour.Should().Be(6);
        m.Slots.Date.SlashPrinted.Should().Be(new RectPt(899.7, 16.5, 71.5, 24.9));
        m.Slots.WeeksRemaining.Printed.Should().Be(new RectPt(536.6, 28.9, 189.7, 13.9));
        m.Slots.TimeBlocks.Printed.Should().Be(new RectPt(886.3, 92.4, 196.5, 548.3));
    }

    /// <summary>
    /// The adversarial review's mutations: each erase rectangle shrunk a few points so that a sliver of the printed art
    /// it removes would survive (B: the slash erase starts at 903; E: the time-block erase ends at 1075; F: the strip
    /// erase ends at 723; A and C: an erase starts too low). Each is a load error naming both keys before anything renders.
    /// </summary>
    [Theory]
    [InlineData("slots.date", "slashErase", 903, 12, 73, 34, "slashPrinted", "its left edge 903 is right of 899.7")]
    [InlineData("slots.timeBlocks", "erase", 870, 88, 205, 560, "printed", "its right edge 1075 is left of 1082.8")]
    [InlineData("slots.weeksRemaining", "erase", 530, 28, 193, 16, "printed", "its right edge 723 is left of 726.3")]
    [InlineData("slots.timeBlocks", "erase", 870, 99, 214, 549, "printed", "its top 99 is below 92.4")]
    [InlineData("slots.weeksRemaining", "erase", 530, 33, 198, 11, "printed", "its top 33 is below 28.9")]
    [InlineData("slots.date", "slashErase", 896, 12, 80, 29, "slashPrinted", "its bottom 41 is above 41.4")]
    public void An_erase_that_does_not_contain_its_printed_extent_is_a_load_error_naming_both_keys(
        string parent, string eraseKey, double x, double y, double w, double h, string printedKey, string side)
    {
        string path = WriteModified(root =>
        {
            JsonNode node = parent.Split('.').Aggregate(root, (n, part) => n[part]!);
            node[eraseKey] = new JsonObject { ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h };
        });

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>()
            .WithMessage($"*{parent}.{eraseKey} (x {x}, y {y}, w {w}, h {h}) must contain {parent}.{printedKey} (*")
            .WithMessage($"*{side}*");
    }

    [Fact]
    public void A_user_manifest_whose_erase_misses_part_of_the_printed_art_fails_the_merge()
    {
        string userPath = Path.Combine(_tempDir, "user.json");
        File.WriteAllText(userPath, "{ \"slots\": { \"weeksRemaining\": { \"erase\": { \"x\": 530, \"y\": 28, \"w\": 193, \"h\": 16 } } } }");

        Action act = () => _loader.Merge(TestPaths.ManifestPath, userPath);

        act.Should().Throw<ManifestException>().WithMessage("*slots.weeksRemaining.erase*must contain slots.weeksRemaining.printed*");
    }

    [Fact]
    public void A_printed_extent_needs_a_positive_size()
    {
        string path = WriteModified(root => root["slots"]!["timeBlocks"]!["printed"] = new JsonObject { ["x"] = 886.3, ["y"] = 92.4, ["w"] = 0, ["h"] = 548.3 });

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*slots.timeBlocks.printed*positive width and height*");
    }

    [Fact]
    public void The_shipped_erase_rectangles_contain_their_printed_extents()
    {
        Manifest m = _loader.Load(TestPaths.ManifestPath);

        Contains(m.Slots.Date.SlashErase, m.Slots.Date.SlashPrinted).Should().BeTrue();
        Contains(m.Slots.WeeksRemaining.Erase, m.Slots.WeeksRemaining.Printed).Should().BeTrue();
        Contains(m.Slots.TimeBlocks.Erase, m.Slots.TimeBlocks.Printed).Should().BeTrue();

        static bool Contains(RectPt outer, RectPt inner) =>
            outer.X <= inner.X && outer.Y <= inner.Y && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;
    }

    [Fact]
    public void Missing_manifest_file_throws_a_manifest_exception()
    {
        string path = Path.Combine(_tempDir, "nope.json");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage($"*{path}*");
    }

    [Fact]
    public void Malformed_json_throws_a_manifest_exception()
    {
        string path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "{ \"templateId\": ");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*not valid JSON*");
    }

    [Fact]
    public void Page_size_must_hold_exactly_two_entries()
    {
        string path = WriteModified(root => root["pageSizePt"] = new JsonArray(1112, 770, 1));

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*pageSizePt*").WithMessage("*3 entries*");
    }

    [Fact]
    public void Page_count_must_be_positive()
    {
        string path = WriteModified(root => root["pageCount"] = 0);

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*pageCount*");
    }

    [Fact]
    public void Weekly_run_must_list_nine_page_names()
    {
        string path = WriteModified(root =>
        {
            JsonArray pages = root["pageTypes"]!["weekly"]!["pages"]!.AsArray();
            pages.RemoveAt(pages.Count - 1);
        });

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*weekly.pages*").WithMessage("*found 8*");
    }

    [Fact]
    public void Weekly_run_must_end_inside_the_document()
    {
        string path = WriteModified(root => root["pageTypes"]!["weekly"]!["count"] = 14);

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*weekly*").WithMessage("*beyond pageCount*");
    }

    [Fact]
    public void Slot_applies_to_must_name_known_pages()
    {
        string path = WriteModified(root => root["slots"]!["weeksRemaining"]!["appliesTo"]!.AsArray().Add("monday"));

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*slots.weeksRemaining.appliesTo*").WithMessage("*'monday'*");
    }

    [Fact]
    public void Page_template_type_must_be_known()
    {
        string path = WriteModified(root => root["notebook"]!["sections"]![0]!["pages"]![0]!["type"] = "frontpage");

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*'frontpage'*");
    }

    [Fact]
    public void Fixed_page_type_pages_must_lie_inside_the_document()
    {
        string path = WriteModified(root => root["pageTypes"]!["cover"]!["pdfPages"] = new JsonArray(0));

        Action act = () => _loader.Load(path);

        act.Should().Throw<ManifestException>().WithMessage("*pageTypes.cover.pdfPages*");
    }

    [Fact]
    public void Every_slot_applies_to_list_is_enumerated()
    {
        Manifest m = _loader.Load(TestPaths.ManifestPath);

        var slots = m.Slots.AppliesToBySlot();

        slots.Should().HaveCount(16);
        slots.Select(s => s.Slot).Should().Contain(new[] { "date", "weeksRemaining", "dayGrid", "timeBlocks", "calendarEventLines", "annualTitle", "objectiveHeading" });
        slots.Should().OnlyContain(s => s.AppliesTo.Count > 0);
    }

    [Fact]
    public void Known_page_names_cover_the_prd_list()
    {
        PageNames.All.Should().BeEquivalentTo(new[]
        {
            "cover", "contents", "annual", "objective", "calendar", "overview", "mon", "tue", "wed", "thu", "fri", "sat", "sun",
            "reflection", "setup", "scorecard", "monthlyReview", "quarterlyReview", "notes", "blank",
        });
        PageNames.IsTemplateType("day").Should().BeTrue();
        PageNames.IsTemplateType("mon").Should().BeFalse();
    }

    private string WriteModified(Action<JsonNode> mutate)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(TestPaths.ManifestPath))!;
        mutate(root);
        string path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }
}
