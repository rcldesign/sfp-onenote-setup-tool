using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Sfp.OneNote.Xml;

/// <summary>Names and formatting rules shared by every XML builder and the parser.</summary>
public static class OneNoteXml
{
    /// <summary>The 2013 schema namespace, the only one the tool writes. The manifest's onenote.schemaNamespace overrides it per run.</summary>
    public const string Namespace2013 = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    /// <summary>The prefix OneNote itself uses.</summary>
    public const string Prefix = "one";

    /// <summary>The page-level one:Meta name that carries the page key.</summary>
    public const string PageKeyMetaName = "sfp.key";

    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        OmitXmlDeclaration = true,
        Encoding = new System.Text.UTF8Encoding(false),
    };

    /// <summary>A point value as OneNote likes it: up to three decimals, no trailing zeros, invariant culture.</summary>
    public static string Pt(double value)
    {
        double rounded = Math.Round(value, 3);
        if (rounded == 0)
        {
            rounded = 0; // normalizes a negative zero
        }

        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A length in points as the inches one:Dimensions takes: points / 72, two decimals, invariant culture
    /// (1112 pt gives "15.44").
    /// </summary>
    public static string Inches(double pt) =>
        Math.Round(pt / 72.0, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A font size for a style attribute: "10.0pt".</summary>
    public static string FontSize(double pt) => pt.ToString("0.0", CultureInfo.InvariantCulture) + "pt";

    /// <summary>"true" or "false".</summary>
    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Serializes an element with two-space indentation, LF line ends and no XML declaration.</summary>
    public static string Serialize(XElement element)
    {
        System.Text.StringBuilder sb = new();
        using (XmlWriter writer = XmlWriter.Create(sb, WriterSettings))
        {
            element.WriteTo(writer);
        }

        return sb.ToString();
    }

    /// <summary>A one:Position element.</summary>
    public static XElement Position(XNamespace one, double x, double y, int z) =>
        new(one + "Position", new XAttribute("x", Pt(x)), new XAttribute("y", Pt(y)), new XAttribute("z", z.ToString(CultureInfo.InvariantCulture)));

    /// <summary>A one:Size element.</summary>
    public static XElement Size(XNamespace one, double width, double height, bool isSetByUser) =>
        new(one + "Size", new XAttribute("width", Pt(width)), new XAttribute("height", Pt(height)), new XAttribute("isSetByUser", Bool(isSetByUser)));
}
