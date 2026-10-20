using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Xml;

public class HyperlinkParserTests
{
    [Fact]
    public void Page_link_yields_section_id_page_id_and_the_decoded_title()
    {
        OneNoteHyperlink link = HyperlinkParser.Parse(MonJan4.OverviewHref);

        link.SectionId.Should().Be("{7A0B1C2D-3E4F-4A5B-8C6D-9E0F1A2B3C4D}{1}{B0}");
        link.PageId.Should().Be("{8B1C2D3E-4F5A-4B6C-9D7E-0F1A2B3C4D5E}{1}{B0}");
        link.Title.Should().Be("Weekly Overview");
        link.Path.Should().BeEmpty();
        link.BasePath.Should().BeNull();
        link.IsPageLink.Should().BeTrue();
        link.Url.Should().Be(MonJan4.OverviewHref);
    }

    [Fact]
    public void Section_link_with_a_one_file_path_has_no_page_id_or_title()
    {
        OneNoteHyperlink link = HyperlinkParser.Parse("onenote:Week%2002.one#section-id={6A9B}{1}{B0}&end");

        link.SectionId.Should().Be("{6A9B}{1}{B0}");
        link.PageId.Should().BeNull();
        link.Title.Should().BeNull();
        link.Path.Should().Be("Week%2002.one");
        link.IsPageLink.Should().BeFalse();
    }

    [Fact]
    public void Link_after_a_move_to_onedrive_keeps_its_ids_and_exposes_the_base_path()
    {
        const string url = "onenote:https://d.docs.live.net/abc123/Documents/SFP%20Q1%202027/Week%2002.one#Mon%20Jan%204&section-id={S}{1}{B0}&page-id={P}{1}{B0}&base-path=https://d.docs.live.net/abc123/Documents&end";

        OneNoteHyperlink link = HyperlinkParser.Parse(url);

        link.Path.Should().Be("https://d.docs.live.net/abc123/Documents/SFP%20Q1%202027/Week%2002.one");
        link.SectionId.Should().Be("{S}{1}{B0}");
        link.PageId.Should().Be("{P}{1}{B0}");
        link.Title.Should().Be("Mon Jan 4");
        link.BasePath.Should().Be("https://d.docs.live.net/abc123/Documents");
    }

    [Fact]
    public void Scheme_match_is_case_insensitive_and_a_link_without_a_fragment_parses_to_empty_ids()
    {
        OneNoteHyperlink link = HyperlinkParser.Parse("OneNote:Notes.one");

        link.Path.Should().Be("Notes.one");
        link.SectionId.Should().BeNull();
        link.PageId.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/#section-id={s}")]
    [InlineData("onenote#missing-colon")]
    public void Anything_else_is_not_an_onenote_link(string? url)
    {
        HyperlinkParser.TryParse(url).Should().BeNull();
        Action act = () => HyperlinkParser.Parse(url ?? "");
        act.Should().Throw<FormatException>();
    }
}
