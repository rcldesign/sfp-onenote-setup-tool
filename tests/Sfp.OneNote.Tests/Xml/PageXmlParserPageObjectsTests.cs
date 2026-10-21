using Sfp.Core.Manifests;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Xml;

/// <summary>Round 2 refresh-5: pictures and files placed on the page outside any outline are read with their positions.</summary>
public class PageXmlParserPageObjectsTests
{
    [Fact]
    public void Page_level_pictures_attached_files_and_media_files_are_read_with_position_and_size()
    {
        const string xml = """
            <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{p}">
              <one:Outline objectID="{sheet}">
                <one:Position x="0" y="36" z="0"/>
                <one:Size width="1112" height="770" isSetByUser="true"/>
                <one:OEChildren><one:OE><one:Image backgroundImage="true" objectID="{bg}"><one:Size width="1112" height="770"/></one:Image></one:OE></one:OEChildren>
              </one:Outline>
              <one:Outline objectID="{note}">
                <one:Position x="10" y="20"/>
                <one:Size width="100" height="30"/>
                <one:OEChildren><one:OE><one:Image objectID="{inside}"><one:Size width="40" height="20"/></one:Image></one:OE></one:OEChildren>
              </one:Outline>
              <one:Image objectID="{photo}" format="png"><one:Position x="250" y="81" z="3"/><one:Size width="300" height="30"/></one:Image>
              <one:Image objectID="{stamp}" backgroundImage="true"><one:Position x="0" y="0"/><one:Size width="50" height="50"/></one:Image>
              <one:InsertedFile objectID="{file}" pathCache="C:\x.pdf" preferredName="x.pdf"><one:Position x="900" y="186.5"/><one:Size width="64" height="70"/></one:InsertedFile>
              <one:MediaFile objectID="{audio}"><one:Position x="40" y="400"/></one:MediaFile>
            </one:Page>
            """;

        PageXml page = PageXmlParser.Parse(xml);

        page.PageObjects.Should().Equal(
            new PageObjectInfo("{photo}", "Image", new RectPt(250, 81, 300, 30)),
            new PageObjectInfo("{file}", "InsertedFile", new RectPt(900, 186.5, 64, 70)),
            new PageObjectInfo("{audio}", "MediaFile", new RectPt(40, 400, 0, 0)));
        page.Images.Select(i => i.ObjectId).Should().Equal(new[] { "{bg}", "{inside}", "{photo}", "{stamp}" }, "every image is still listed in Images");
    }

    [Fact]
    public void A_page_level_future_object_is_read_with_position_and_size_and_one_inside_an_outline_is_not()
    {
        const string xml = """
            <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{p}">
              <one:Outline objectID="{note}">
                <one:Position x="10" y="20"/>
                <one:Size width="100" height="30"/>
                <one:OEChildren><one:OE><one:FutureObject objectID="{inside}"/></one:OE></one:OEChildren>
              </one:Outline>
              <one:FutureObject objectID="{future}"><one:Position x="250" y="81" z="3"/><one:Size width="300" height="30"/></one:FutureObject>
              <one:Image objectID="{photo}" format="png"><one:Position x="40" y="400"/><one:Size width="64" height="70"/></one:Image>
            </one:Page>
            """;

        PageXml page = PageXmlParser.Parse(xml);

        page.PageObjects.Should().Equal(
            new PageObjectInfo("{future}", "FutureObject", new RectPt(250, 81, 300, 30)),
            new PageObjectInfo("{photo}", "Image", new RectPt(40, 400, 64, 70)));
    }

    [Fact]
    public void A_page_without_page_level_objects_has_none()
    {
        PageXml page = PageXmlParser.Parse("""<one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="{p}"/>""");

        page.PageObjects.Should().BeEmpty();
    }
}
