// schema/OneNoteApplication_2013.xsd is Microsoft's OneNote 2013 application schema (0336.OneNoteApplication_2013.xsd) as published in the OneNote developer reference and shipped in the OneMore repository, unmodified.
using System.Xml;
using System.Xml.Schema;
using Sfp.OneNote.Xml;

namespace Sfp.OneNote.Tests.Support;

/// <summary>
/// Validates XML against tests/Sfp.OneNote.Tests/schema/OneNoteApplication_2013.xsd with a strict callback: every
/// warning (an element or attribute the schema does not know) counts as an error, so an attribute OneNote would reject
/// fails the test here first. Object IDs must have the shape OneNote writes ({GUID}{n}{Xn}), as the schema's ObjectID
/// pattern requires.
/// </summary>
internal static class SchemaValidator
{
    public static string SchemaPath => Path.Combine(AppContext.BaseDirectory, "schema", "OneNoteApplication_2013.xsd");

    private static readonly Lazy<XmlSchemaSet> Schemas = new(() =>
    {
        XmlSchemaSet set = new();
        set.Add(OneNoteXml.Namespace2013, SchemaPath);
        set.Compile();
        return set;
    });

    /// <summary>Every validation message for <paramref name="xml"/>, empty when it is valid.</summary>
    public static IReadOnlyList<string> Problems(string xml)
    {
        List<string> problems = new();
        XmlReaderSettings settings = new()
        {
            ValidationType = ValidationType.Schema,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings | XmlSchemaValidationFlags.ProcessIdentityConstraints,
            Schemas = Schemas.Value,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
        settings.ValidationEventHandler += (_, e) =>
            problems.Add($"{e.Severity} at line {e.Exception?.LineNumber}, position {e.Exception?.LinePosition}: {e.Message}");

        using StringReader text = new(xml);
        using XmlReader reader = XmlReader.Create(text, settings);
        while (reader.Read())
        {
        }

        return problems;
    }

    /// <summary>Fails the test with every message when <paramref name="xml"/> does not validate.</summary>
    public static void AssertValid(string xml)
    {
        IReadOnlyList<string> problems = Problems(xml);
        problems.Should().BeEmpty("the XML must validate against the official 2013 schema:\n" + xml);
    }
}
