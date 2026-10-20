using System.Globalization;
using PDFtoImage;
using SkiaSharp;
using PdfiumOptions = PDFtoImage.RenderOptions;

namespace Sfp.Render;

/// <summary>
/// PDFium rasterizer (through PDFtoImage). Pages render with antialiasing on, annotations and form fill off, and the
/// manifest background color behind the page. The pixel size is set explicitly from the page size so that
/// 1112 x 770 pt at 220 DPI is exactly 3398 x 2353 px. Rendered pages are kept in a small least-recently-used cache
/// keyed by (path, page, dpi) and handed out as pixel copies (<see cref="BitmapCopy"/>, never SKBitmap.Copy, whose
/// shader would keep an evicted page alive), so callers can draw on their copy without touching the cache.
/// PDFium is not thread-safe, so every render runs under one lock.
/// </summary>
public sealed class Rasterizer : IRasterizer
{
    /// <summary>Default number of rendered pages kept in memory (about 32 MB each at 220 DPI).</summary>
    public const int DefaultCacheCapacity = 12;

    private readonly object gate = new();
    private readonly SKColor background;
    private readonly int cacheCapacity;
    private readonly Dictionary<string, OpenDocument> documents = new(StringComparer.Ordinal);
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> cache = new();
    private readonly LinkedList<CacheEntry> recency = new();
    private bool disposed;

    /// <param name="background">Color painted behind the page (the manifest's colors.background).</param>
    /// <param name="cacheCapacity">Rendered pages kept in memory; 0 disables the cache.</param>
    public Rasterizer(SKColor background, int cacheCapacity = DefaultCacheCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cacheCapacity);
        this.background = background;
        this.cacheCapacity = cacheCapacity;
    }

    /// <summary>Pages currently held in the cache.</summary>
    public int CachedPages
    {
        get
        {
            lock (gate)
            {
                return cache.Count;
            }
        }
    }

    /// <summary>Points to pixels at a DPI, rounded to the nearest pixel: Math.Round(pt * dpi / 72).</summary>
    public static int PixelsFor(double pt, int dpi) => (int)Math.Round(pt * dpi / 72.0, MidpointRounding.AwayFromZero);

    /// <inheritdoc />
    public SKBitmap Render(string pdfPath, int pdfPage1Based, int dpi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);

        lock (gate)
        {
            ThrowIfDisposed();
            OpenDocument doc = Open(pdfPath);
            int index = doc.CheckPage(pdfPage1Based);
            CacheKey key = new(doc.Path, pdfPage1Based, dpi);

            if (cache.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                recency.Remove(node);
                recency.AddFirst(node);
                return BitmapCopy.Clone(node.Value.Bitmap);
            }

            PdfPageSize size = doc.PageSizes[index];
            (int widthPx, int heightPx) = size.PixelsAt(dpi);
            PdfiumOptions options = new(
                Dpi: dpi,
                Width: widthPx,
                Height: heightPx,
                WithAnnotations: false,
                WithFormFill: false,
                WithAspectRatio: false,
                Rotation: PdfRotation.Rotate0,
                AntiAliasing: PdfAntiAliasing.All,
                BackgroundColor: background,
                Bounds: null,
                UseTiling: false,
                DpiRelativeToBounds: false,
                Grayscale: false);

            SKBitmap rendered = Pdfium.Render(doc.Bytes, index, options);
            if (rendered.Width != widthPx || rendered.Height != heightPx)
            {
                rendered.Dispose();
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "PDFium rendered page {0} of '{1}' at {2}x{3} px, expected {4}x{5}.",
                    pdfPage1Based, pdfPath, rendered.Width, rendered.Height, widthPx, heightPx));
            }

            if (cacheCapacity == 0)
            {
                return rendered;
            }

            rendered.SetImmutable();
            Remember(key, rendered);
            return BitmapCopy.Clone(rendered);
        }
    }

    /// <inheritdoc />
    public int PageCount(string pdfPath)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            return Open(pdfPath).PageSizes.Count;
        }
    }

    /// <inheritdoc />
    public PdfPageSize PageSize(string pdfPath, int pdfPage1Based)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            OpenDocument doc = Open(pdfPath);
            return doc.PageSizes[doc.CheckPage(pdfPage1Based)];
        }
    }

    /// <summary>Drops every cached page but keeps the documents open.</summary>
    public void ClearCache()
    {
        lock (gate)
        {
            foreach (CacheEntry entry in recency)
            {
                entry.Bitmap.Dispose();
            }

            recency.Clear();
            cache.Clear();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            ClearCache();
            documents.Clear();
            disposed = true;
        }
    }

    private void Remember(CacheKey key, SKBitmap bitmap)
    {
        LinkedListNode<CacheEntry> node = new(new CacheEntry(key, bitmap));
        recency.AddFirst(node);
        cache[key] = node;
        while (recency.Count > cacheCapacity)
        {
            LinkedListNode<CacheEntry> oldest = recency.Last!;
            recency.RemoveLast();
            cache.Remove(oldest.Value.Key);
            oldest.Value.Bitmap.Dispose();
        }
    }

    private OpenDocument Open(string pdfPath)
    {
        string fullPath = Path.GetFullPath(pdfPath);
        if (!documents.TryGetValue(fullPath, out OpenDocument? doc))
        {
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("PDF not found: " + fullPath, fullPath);
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            IReadOnlyList<PdfPageSize> sizes = Pdfium.PageSizes(bytes);
            doc = new OpenDocument(fullPath, bytes, sizes);
            documents[fullPath] = doc;
        }

        return doc;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private readonly record struct CacheKey(string Path, int Page, int Dpi);

    private sealed record CacheEntry(CacheKey Key, SKBitmap Bitmap);

    private sealed class OpenDocument
    {
        public OpenDocument(string path, byte[] bytes, IReadOnlyList<PdfPageSize> pageSizes)
        {
            Path = path;
            Bytes = bytes;
            PageSizes = pageSizes;
        }

        public string Path { get; }

        public byte[] Bytes { get; }

        public IReadOnlyList<PdfPageSize> PageSizes { get; }

        /// <summary>Validates a 1-based page number and returns its 0-based index.</summary>
        public int CheckPage(int pdfPage1Based)
        {
            if (pdfPage1Based < 1 || pdfPage1Based > PageSizes.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pdfPage1Based),
                    pdfPage1Based,
                    string.Format(CultureInfo.InvariantCulture, "'{0}' has {1} pages; page numbers are 1-based.", Path, PageSizes.Count));
            }

            return pdfPage1Based - 1;
        }
    }

    /// <summary>
    /// The PDFium calls, guarded by the platform checks that PDFtoImage declares support for (it ships native PDFium for
    /// Windows, Linux and macOS). The guards keep the platform-compatibility analyzer (CA1416) satisfied without
    /// annotating every caller up to the CLI.
    /// </summary>
    private static class Pdfium
    {
        public static IReadOnlyList<PdfPageSize> PageSizes(byte[] pdf)
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return Conversion.GetPageSizes(pdf, password: null)
                    .Select(s => new PdfPageSize(s.Width, s.Height))
                    .ToArray();
            }

            throw Unsupported();
        }

        public static SKBitmap Render(byte[] pdf, int pageIndex, PdfiumOptions options)
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return Conversion.ToImage(pdf, new Index(pageIndex), password: null, options: options);
            }

            throw Unsupported();
        }

        private static PlatformNotSupportedException Unsupported() =>
            new("PDF rasterization needs PDFium, which ships for Windows, Linux and macOS only.");
    }
}
