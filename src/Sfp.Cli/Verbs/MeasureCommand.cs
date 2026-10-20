using System.CommandLine;
using Sfp.Core.Pdf;

namespace Sfp.Cli.Verbs;

/// <summary>
/// sfp2onenote measure --pdf PATH [--pages 1 13 16 17 24]: the geometry dump behind the measure tool, as a verb. Prints
/// every word and line-like segment of the given pages in PDF points with a top-left origin, the manifest's convention.
/// The integrator adds the command to Program.cs with <see cref="Build"/>.
/// </summary>
public static class MeasureCommand
{
    public static Command Build()
    {
        Option<string> pdf = new("--pdf")
        {
            Description = "Path of the planner PDF.",
            Required = true,
        };
        Option<int[]> pages = new("--pages")
        {
            Description = "1-based page numbers to dump. Default: " + string.Join(" ", PdfGeometryDump.DefaultPages)
                + " (cover, first calendar, first overview, first Monday, first reflection).",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => PdfGeometryDump.DefaultPages.ToArray(),
        };

        Command command = new("measure", "Prints the words and lines of PDF pages in points (top-left origin) to compare against templates/sfp-quarterly.json.");
        command.Options.Add(pdf);
        command.Options.Add(pages);
        command.SetAction(parseResult => Run(
            parseResult.GetValue(pdf) ?? "",
            parseResult.GetValue(pages) ?? Array.Empty<int>(),
            Console.Out,
            Console.Error));
        return command;
    }

    /// <summary>Runs the verb; returns an exit code from <see cref="ExitCodes"/>.</summary>
    public static int Run(string pdfPath, IReadOnlyList<int> pages, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            stderr.WriteLine($"PDF not found: {pdfPath}");
            return ExitCodes.ValidationError;
        }

        IReadOnlyList<int> wanted = pages.Count == 0 ? PdfGeometryDump.DefaultPages : pages;
        if (wanted.Any(p => p < 1))
        {
            stderr.WriteLine("Page numbers must be positive integers.");
            return ExitCodes.ValidationError;
        }

        try
        {
            IReadOnlyList<PageGeometry> geometry = new PdfGeometryDump().Dump(pdfPath, wanted);
            PdfGeometryTable.Write(stdout, geometry);
            return ExitCodes.Success;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            stderr.WriteLine(ex.Message);
            return ExitCodes.ValidationError;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Top-level handler of a CLI verb: a damaged PDF is reported in one line with the validation exit code.
            stderr.WriteLine($"Could not read PDF {pdfPath}: {ex.Message}");
            return ExitCodes.ValidationError;
        }
    }
}
