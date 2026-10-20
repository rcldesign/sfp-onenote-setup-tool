using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Sfp.Core.Manifests;
using SkiaSharp;

namespace Sfp.TestSupport;

/// <summary>Tesseract page segmentation mode for a crop.</summary>
public enum OcrLayout
{
    /// <summary>One line of text (the default for a slot crop).</summary>
    SingleLine = 7,

    /// <summary>One word.</summary>
    SingleWord = 8,

    /// <summary>A block of lines.</summary>
    Block = 6,
}

/// <summary>
/// Reads text from rendered crops with whatever Tesseract is on the machine: the tesseract CLI when it is on PATH, else
/// python3 with the tesserocr module (the build environment), else nothing. Tests that need it call
/// Skip.If(!Ocr.IsAvailable, ...). Pixel-based assertions never depend on this.
/// </summary>
public static class Ocr
{
    private const string DefaultTessdata = "/home/user/tessdata";
    private const int MinimumHeightPx = 48;

    private const string PythonScript =
        "import sys, tesserocr\n" +
        "print(tesserocr.file_to_text(sys.argv[1], lang='eng', psm=int(sys.argv[2]), path=sys.argv[3]), end='')\n";

    private static readonly Lazy<Engine?> EngineLazy = new(Detect);

    /// <summary>True when an OCR engine was found.</summary>
    public static bool IsAvailable => EngineLazy.Value is not null;

    /// <summary>"tesseract", "tesserocr" or null.</summary>
    public static string? EngineName => EngineLazy.Value?.Name;

    /// <summary>The tessdata folder: TESSDATA_PREFIX, or the build environment's default.</summary>
    public static string TessdataDir
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
            string dir = string.IsNullOrWhiteSpace(env) ? DefaultTessdata : env;
            return dir.EndsWith(Path.DirectorySeparatorChar) || dir.EndsWith('/') ? dir : dir + "/";
        }
    }

    /// <summary>Reads a PNG; false when no engine is available or it failed.</summary>
    public static bool TryRead(byte[] png, out string text, OcrLayout layout = OcrLayout.SingleLine)
    {
        text = "";
        Engine? engine = EngineLazy.Value;
        if (engine is null)
        {
            return false;
        }

        string dir = TestPaths.NewTempDir("ocr");
        string path = Path.Combine(dir, "crop.png");
        try
        {
            File.WriteAllBytes(path, png);
            string? result = engine.Run(path, (int)layout);
            if (result is null)
            {
                return false;
            }

            text = result;
            return true;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Reads a bitmap crop, upscaling small crops first so Tesseract has enough pixels per glyph.</summary>
    public static bool TryRead(SKBitmap crop, out string text, OcrLayout layout = OcrLayout.SingleLine)
    {
        using SKBitmap prepared = Prepare(crop);
        using SKData data = prepared.Encode(SKEncodedImageFormat.Png, 100);
        return TryRead(data.ToArray(), out text, layout);
    }

    /// <summary>
    /// A copy of the region of a rendered sheet given in points, grown by a padding in points, clipped to the bitmap. The
    /// pixels are read straight from the region (drawing the sheet through a canvas would snapshot the whole sheet, 32 MB
    /// at 220 DPI, for every crop).
    /// </summary>
    public static SKBitmap Crop(SKBitmap source, RectPt rectPt, int dpi, double paddingPt = 6)
    {
        ArgumentNullException.ThrowIfNull(source);
        double scale = dpi / 72.0;
        int left = Math.Clamp((int)Math.Floor((rectPt.X - paddingPt) * scale), 0, source.Width - 1);
        int top = Math.Clamp((int)Math.Floor((rectPt.Y - paddingPt) * scale), 0, source.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling((rectPt.Right + paddingPt) * scale), left + 1, source.Width);
        int bottom = Math.Clamp((int)Math.Ceiling((rectPt.Bottom + paddingPt) * scale), top + 1, source.Height);

        SKBitmap crop = new(new SKImageInfo(right - left, bottom - top, source.ColorType, source.AlphaType));
        try
        {
            using SKPixmap pixels = source.PeekPixels() ?? throw new InvalidOperationException("The sheet exposes no pixels to crop.");
            if (!pixels.ReadPixels(crop.Info, crop.GetPixels(), crop.RowBytes, left, top))
            {
                throw new InvalidOperationException($"Could not read the {crop.Width}x{crop.Height} px crop at ({left}, {top}).");
            }

            return crop;
        }
        catch
        {
            crop.Dispose();
            throw;
        }
    }

    /// <summary>Collapses runs of whitespace (including line breaks) to one space and trims.</summary>
    public static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static SKBitmap Prepare(SKBitmap crop)
    {
        int factor = crop.Height >= MinimumHeightPx ? 1 : (int)Math.Ceiling(MinimumHeightPx / (double)crop.Height);
        SKBitmap prepared = new(new SKImageInfo(crop.Width * factor, crop.Height * factor, SKColorType.Rgb888x, SKAlphaType.Opaque));
        using SKCanvas canvas = new(prepared);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(crop, new SKRect(0, 0, crop.Width, crop.Height), new SKRect(0, 0, prepared.Width, prepared.Height), new SKSamplingOptions(SKFilterMode.Linear));
        canvas.Flush();
        return prepared;
    }

    private static Engine? Detect()
    {
        if (Run("tesseract", ["--version"], TimeSpan.FromSeconds(30)) is not null)
        {
            return new Engine("tesseract", RunTesseractCli);
        }

        if (Run("python3", ["-I", "-c", "import tesserocr"], TimeSpan.FromSeconds(60)) is not null)
        {
            return new Engine("tesserocr", RunTesserocr);
        }

        return null;
    }

    private static string? RunTesseractCli(string pngPath, int psm) =>
        Run("tesseract", [pngPath, "stdout", "--psm", psm.ToString(CultureInfo.InvariantCulture), "-l", "eng"], TimeSpan.FromMinutes(2));

    private static string? RunTesserocr(string pngPath, int psm) =>
        Run("python3", ["-I", "-c", PythonScript, pngPath, psm.ToString(CultureInfo.InvariantCulture), TessdataDir], TimeSpan.FromMinutes(2));

    /// <summary>Runs a program and returns its standard output, or null when it is missing, fails or times out.</summary>
    private static string? Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ProcessStartInfo info = new(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["TESSDATA_PREFIX"] = TessdataDir;

        try
        {
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            Task.WaitAll(stdout, stderr);
            return process.ExitCode == 0 ? stdout.Result : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record Engine(string Name, Func<string, int, string?> Run);
}
