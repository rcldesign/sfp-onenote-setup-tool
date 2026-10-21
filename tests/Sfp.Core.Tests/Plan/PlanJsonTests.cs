using System.Text.Json;
using Sfp.Core.Plan;
using static Sfp.Core.Tests.Plan.PlanFixture;

namespace Sfp.Core.Tests.Plan;

/// <summary>M1.8: the JSON shape of a plan serializes with the shared options and round-trips back to an equal plan.</summary>
public class PlanJsonTests
{
    [Fact]
    public void Json_round_trips_the_Q1_2027_plan()
    {
        NotebookPlan plan = Build(Q1_2027, o => o with { GroupWeeks = true });
        string json = PlanJson.From(plan).Serialize();

        PlanJson parsed = PlanJson.Parse(json);
        NotebookPlan back = parsed.ToPlan();

        parsed.Pages.Should().HaveCount(144);
        parsed.Sections.Should().HaveCount(19);
        parsed.Links.Should().HaveCount(plan.Links.Count);
        back.Name.Should().Be(plan.Name);
        back.Quarter.Should().Be(plan.Quarter);
        back.Sections.Select(s => (s.Name, s.ColorHex, s.GroupName)).Should().Equal(plan.Sections.Select(s => (s.Name, s.ColorHex, s.GroupName)));
        back.Pages.Should().Equal(plan.Pages, (a, b) => a.Key == b.Key && a.Type == b.Type && a.Title == b.Title && a.Level == b.Level
            && a.PdfPage == b.PdfPage && a.Date == b.Date && a.Tokens.OrderBy(t => t.Key, StringComparer.Ordinal).SequenceEqual(b.Tokens.OrderBy(t => t.Key, StringComparer.Ordinal)));
        back.Links.Should().Equal(plan.Links);
        PlanJson.From(back).Serialize().Should().Be(json, "a second pass produces the same text");
    }

    [Fact]
    public void Json_uses_camel_case_names_iso_dates_and_camel_case_enums()
    {
        string json = PlanJson.From(Build(Q1_2027)).Serialize();

        json.Replace("\r\n", "\n", StringComparison.Ordinal).Should().StartWith("{\n  \"name\": \"SFP Q1 2027\",\n  \"quarter\": {\n    \"start\": \"2027-01-01\",\n    \"end\": \"2027-03-31\",\n    \"weekCount\": 14,\n    \"length\": 90,\n    \"label\": \"Q1 2027\"\n  },");
        json.Should().Contain("\"type\": \"weeklyOverview\"");
        json.Should().Contain("\"key\": \"day:2027-01-04\"");
        json.Should().Contain("\"section\": \"Week 02 (Jan 4 to Jan 10)\"");
        json.Should().Contain("\"pdfPage\": null");
        json.Should().Contain("\"groupName\": null");
        json.Should().Contain("\"anchorId\": \"calendarDay:2027-01-04\"");
        json.Should().Contain("\"text\": \"‹ Sun Jan 3\"", "the shared options keep non-ASCII text readable");
        json.Should().NotContain("\\u2039", "the single angle quote is written as itself, not as a JSON escape");
    }

    [Fact]
    public void Parse_rejects_an_unknown_key_and_an_empty_document()
    {
        Action unknown = () => PlanJson.Parse("{ \"name\": \"x\", \"quarter\": { \"start\": \"2027-01-01\", \"end\": \"2027-03-31\", \"weekCount\": 14, \"length\": 90, \"label\": \"Q1 2027\" }, \"sections\": [], \"pages\": [], \"links\": [], \"extra\": 1 }");
        Action empty = () => PlanJson.Parse("null");

        unknown.Should().Throw<JsonException>().WithMessage("*extra*");
        empty.Should().Throw<JsonException>();
    }

    [Fact]
    public void To_plan_fails_clearly_when_a_section_lists_a_missing_page()
    {
        PlanJson broken = new(
            "x",
            new QuarterJson(D(2027, 1, 1), D(2027, 3, 31), 14, 90, "Q1 2027"),
            new[] { new SectionJson("Quarter", null, null, new[] { "cover" }) },
            Array.Empty<PageJson>(),
            Array.Empty<LinkJson>());

        Action act = () => broken.ToPlan();

        act.Should().Throw<KeyNotFoundException>();
    }
}
