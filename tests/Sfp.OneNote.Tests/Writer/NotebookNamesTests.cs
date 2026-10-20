using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.OneNote.Tests.Support;
using Sfp.OneNote.Writer;

namespace Sfp.OneNote.Tests.Writer;

public class NotebookNamesTests
{
    private static readonly Manifest M = TestManifest.Load();

    [Theory]
    [InlineData("SFP Q1 2027")]
    [InlineData("A")]
    [InlineData("Planning & Review (2027)")]
    public void Names_within_the_rule_are_valid(string name)
    {
        NotebookNames.IsValid(name).Should().BeTrue();
        NotebookNames.Problem(name).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("SFP\\Q1")]
    [InlineData("SFP/Q1")]
    [InlineData("SFP:Q1")]
    [InlineData("SFP*Q1")]
    [InlineData("SFP?Q1")]
    [InlineData("SFP\"Q1")]
    [InlineData("SFP<Q1")]
    [InlineData("SFP>Q1")]
    [InlineData("SFP|Q1")]
    [InlineData("SFP#Q1")]
    [InlineData("SFP%Q1")]
    public void Names_breaking_the_rule_are_refused_with_the_rule_message(string? name)
    {
        NotebookNames.IsValid(name).Should().BeFalse();
        NotebookNames.Problem(name).Should().Be(NotebookNames.RuleMessage);
    }

    [Fact]
    public void Sixty_four_characters_are_the_limit()
    {
        NotebookNames.IsValid(new string('a', 64)).Should().BeTrue();
        NotebookNames.IsValid(new string('a', 65)).Should().BeFalse();
    }

    [Theory]
    [InlineData("Quarter", "Quarter")]
    [InlineData("Week 02 (Jan 4 to Jan 10)", "Week 02 (Jan 4 to Jan 10)")]
    [InlineData("Q&A", "Q&A")]
    [InlineData("A/B:C?", "A-B-C-")]
    [InlineData("\\ / : * ? \" < > | # %", "- - - - - - - - - - -")]
    [InlineData("  trailing. ", "trailing")]
    [InlineData("", "Section")]
    [InlineData("...", "Section")]
    public void Section_names_are_sanitized_not_refused(string name, string expected)
    {
        NotebookNames.SanitizeSectionName(name).Should().Be(expected);
    }

    [Fact]
    public void Long_section_names_are_cut_to_the_limit()
    {
        NotebookNames.SanitizeSectionName(new string('x', 80)).Should().HaveLength(64);
    }

    [Fact]
    public void Default_name_expands_the_manifest_pattern_and_the_override_wins_when_given()
    {
        GenerateOptions plain = new() { Start = TestPlans.Q1.Spec };
        NotebookNames.Default(M, TestPlans.Q1).Should().Be("SFP Q1 2027");
        NotebookNames.Resolve(M, TestPlans.Q1, plain).Should().Be("SFP Q1 2027");
        NotebookNames.Resolve(M, TestPlans.Q1, plain with { NotebookName = "  My Quarter " }).Should().Be("My Quarter");
        NotebookNames.Resolve(M, TestPlans.Q1, plain with { NotebookName = "   " }).Should().Be("SFP Q1 2027");
    }
}
