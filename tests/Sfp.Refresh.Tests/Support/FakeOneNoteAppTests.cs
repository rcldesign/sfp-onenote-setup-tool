using System.Xml.Linq;
using Sfp.Core.Plan;
using Sfp.OneNote;
using Sfp.OneNote.Com;
using Sfp.OneNote.Xml;

namespace Sfp.Refresh.Tests.Support;

/// <summary>The fake models the OneNote behaviors Refresh depends on, so a test that passes against it means something.</summary>
public class FakeOneNoteAppTests
{
    private static readonly PageKey MonJan4 = PageKey.Day(new DateOnly(2027, 1, 4));

    [Fact]
    public void UpdatePageContent_keeps_an_outline_the_xml_leaves_out_and_only_DeletePageContent_removes_it()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        string pageId = nb.PageIds[MonJan4];
        nb.TypeUserText(MonJan4, new Sfp.Core.Manifests.RectPt(500, 400, 100, 14), "my note");
        PageXml page = nb.Read(MonJan4);
        OutlineInfo note = page.Outlines.Single(o => o.PlainText == "my note");
        XElement root = new(page.Document.Root!);
        root.Elements(root.Name.Namespace + "Outline").Single(o => (string?)o.Attribute("objectID") == note.ObjectId).Remove();

        nb.App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);

        nb.Read(MonJan4).Outlines.Should().Contain(o => o.ObjectId == note.ObjectId, "OneNote merges by objectID: leaving an object out of the XML does not delete it");

        nb.App.DeletePageContent(pageId, note.ObjectId, DateTime.MinValue);

        nb.Read(MonJan4).Outlines.Should().NotContain(o => o.ObjectId == note.ObjectId);
        nb.Read(MonJan4).Outlines.Should().HaveCount(page.Outlines.Count - 1);
    }

    [Fact]
    public void UpdatePageContent_replaces_an_object_by_its_id_in_place_and_adds_one_without_an_id()
    {
        FakeNotebook nb = FakeNotebook.Generate();
        PageXml page = nb.Read(MonJan4);
        XElement root = new(page.Document.Root!);
        XNamespace one = root.Name.Namespace;
        XElement first = root.Elements(one + "Outline").First(o => (string?)o.Attribute("objectID") != page.SheetOutline!.ObjectId);
        string firstId = (string)first.Attribute("objectID")!;
        first.Descendants(one + "T").First().ReplaceNodes(new XCData("changed"));
        root.Add(new XElement(one + "Outline", OneNoteXml.Position(one, 10, 600, 2), OneNoteXml.Size(one, 50, 13, false), new XElement(one + "OEChildren", new XElement(one + "OE", new XElement(one + "T", new XCData("added"))))));

        nb.App.UpdatePageContent(OneNoteXml.Serialize(root), DateTime.MinValue);

        PageXml after = nb.Read(MonJan4);
        after.Outlines.Select(o => o.ObjectId).Take(page.Outlines.Count).Should().Equal(page.Outlines.Select(o => o.ObjectId), "objects keep their place");
        after.Outlines.Single(o => o.ObjectId == firstId).PlainText.Should().Be("changed");
        after.Outlines.Last().PlainText.Should().Be("added");
        after.Outlines.Last().ObjectId.Should().NotBeEmpty("OneNote stamps an objectID on a new object");
        after.SfpKey.Should().Be(MonJan4.Value);
        after.Title.Should().Be("Mon Jan 4");
    }

    [Fact]
    public void DeletePageContent_of_an_object_that_is_not_there_fails_as_OneNote_does()
    {
        FakeNotebook nb = FakeNotebook.Generate();

        Action act = () => nb.App.DeletePageContent(nb.PageIds[MonJan4], "{p-0000}{99}{B0}", DateTime.MinValue);

        act.Should().Throw<OneNoteException>().Which.HResult.Should().Be(OneNoteHResults.PageObjectDoesNotExist);
    }
}
