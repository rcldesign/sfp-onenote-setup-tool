namespace Sfp.Measure;

/// <summary>
/// sfp-measure PDF [page ...]: prints every text run (text, x, top, bottom, font size, font name) and every line segment
/// of the given pages in PDF points with a top-left origin, so manifest coordinates can be checked against the real file.
/// Implemented with the geometry dump in Sfp.Core (M1.3 companion).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: sfp-measure <pdf> [page ...]");
            return 2;
        }

        Console.WriteLine("sfp-measure: geometry dump is added in M1.3.");
        return 0;
    }
}
