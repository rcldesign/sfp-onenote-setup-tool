using Sfp.Core.Manifests;

namespace Sfp.Core.Tests.Manifests;

public class TokenExpanderTests
{
    [Fact]
    public void Validate_accepts_a_pattern_whose_tokens_are_all_allowed()
    {
        Action act = () => TokenExpander.Validate("Week {NN} ({start} to {end})", Tokens.All);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_accepts_literal_text_without_tokens()
    {
        Action act = () => TokenExpander.Validate("Weekly Overview", Tokens.All);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_throws_naming_the_unknown_token_and_the_pattern()
    {
        Action act = () => TokenExpander.Validate("Objective {Foo}", Tokens.All);

        act.Should().Throw<ManifestException>()
            .WithMessage("*{Foo}*")
            .WithMessage("*'Objective {Foo}'*");
    }

    [Fact]
    public void Validate_is_case_sensitive_so_mon_is_not_Mon()
    {
        Action act = () => TokenExpander.Validate("{Ddd} {mon} {D}", Tokens.All);

        act.Should().Throw<ManifestException>().WithMessage("*{mon}*");
    }

    [Fact]
    public void Validate_rejects_a_token_outside_the_allowed_subset()
    {
        string[] withoutNote = Tokens.All.Where(t => t != Tokens.NoteName).ToArray();

        Action act = () => TokenExpander.Validate("{Note}", withoutNote);

        act.Should().Throw<ManifestException>().WithMessage("*{Note}*");
    }

    [Theory]
    [InlineData("{Month {YYYY}")]
    [InlineData("Week {NN")]
    [InlineData("Week NN}")]
    [InlineData("{}")]
    [InlineData("{Week 1}")]
    public void Validate_rejects_stray_or_malformed_braces(string pattern)
    {
        Action act = () => TokenExpander.Validate(pattern, Tokens.All);

        act.Should().Throw<ManifestException>().WithMessage($"*'{pattern}'*");
    }

    [Fact]
    public void TokensIn_lists_each_token_once_in_order_of_first_use()
    {
        TokenExpander.TokensIn("{Mon} {D} to {Mon} {D}, {YYYY}").Should().Equal("Mon", "D", "YYYY");
    }

    [Fact]
    public void Expand_replaces_every_occurrence_and_keeps_literal_text()
    {
        Dictionary<string, string> values = new() { ["NN"] = "02", ["start"] = "Jan 4", ["end"] = "Jan 10" };

        TokenExpander.Expand("Week {NN} ({start} to {end}) [{NN}]", values).Should().Be("Week 02 (Jan 4 to Jan 10) [02]");
    }

    [Fact]
    public void Expand_returns_literal_text_unchanged()
    {
        TokenExpander.Expand("Weekly Reflection", new Dictionary<string, string>()).Should().Be("Weekly Reflection");
    }

    [Fact]
    public void Expand_throws_naming_the_token_that_has_no_value()
    {
        Dictionary<string, string> values = new() { ["Month"] = "January" };

        Action act = () => TokenExpander.Expand("{Month} {YYYY}", values);

        act.Should().Throw<ManifestException>()
            .WithMessage("*{YYYY}*")
            .WithMessage("*'{Month} {YYYY}'*");
    }

    [Fact]
    public void Expand_allows_an_empty_value()
    {
        Dictionary<string, string> values = new() { ["Qn"] = "" };

        TokenExpander.Expand("SFP {Qn}", values).Should().Be("SFP ");
    }
}
