using Sfp.OneNote.Com;

namespace Sfp.OneNote.Tests.Com;

public class LogSafeTests
{
    [Fact]
    public void Data_payloads_are_elided_before_the_text_is_cut()
    {
        string data = new('A', 5000);
        string xml = "<one:Page><one:Data>" + data + "</one:Data></one:Page>";

        string safe = LogSafe.Xml(xml);

        safe.Should().StartWith("<one:Page><one:Data>[5000 chars elided]</one:Data></one:Page>");
        safe.Should().NotContain("AAAAAAAA");
    }

    [Fact]
    public void Long_xml_is_cut_to_two_hundred_characters_with_the_full_length_noted()
    {
        string xml = "<one:Page>" + new string('x', 1000) + "</one:Page>";

        string safe = LogSafe.Xml(xml);

        safe.Should().StartWith(xml[..200]);
        safe.Should().EndWith("... (1021 chars)");
    }

    [Fact]
    public void Short_xml_and_empty_input_pass_through()
    {
        LogSafe.Xml("<one:Section/>").Should().Be("<one:Section/>");
        LogSafe.Xml(null).Should().Be("");
        LogSafe.Xml("").Should().Be("");
    }
}
