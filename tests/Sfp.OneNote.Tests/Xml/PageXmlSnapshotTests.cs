using System.Text.RegularExpressions;
using Sfp.OneNote.Tests.Support;

namespace Sfp.OneNote.Tests.Xml;

/// <summary>The Mon Jan 4 page XML as a reviewed snapshot (tests/Sfp.OneNote.Tests/snapshots), base64 image data elided.</summary>
public partial class PageXmlSnapshotTests
{
    [Fact]
    public async Task Mon_Jan_4_page_xml_matches_the_snapshot()
    {
        string xml = MonJan4.Build(TestManifest.Load());
        string elided = DataElement().Replace(xml, "$1[base64 PNG elided]$2");

        VerifySettings settings = new();
        settings.UseDirectory("../snapshots");
        await Verify(elided, "xml", settings);
    }

    [GeneratedRegex(@"(<one:Data>)[^<]*(</one:Data>)")]
    private static partial Regex DataElement();
}
