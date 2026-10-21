using System.Globalization;
using Sfp.Core.Plan;

namespace Sfp.Render;

/// <summary>
/// A page could not be rendered. <see cref="Page"/> names it, the message is the PRD's render-error sentence
/// (<see cref="Describe"/>) and the inner exception is the renderer's. The sentence lives here, the one copy that
/// render-only and Generate share, because Sfp.OneNote references Sfp.Render and not the other way round.
/// </summary>
public sealed class PageRenderException : Exception
{
    public PageRenderException()
    {
    }

    public PageRenderException(string message)
        : base(message)
    {
    }

    public PageRenderException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <param name="page">The page whose render threw.</param>
    /// <param name="innerException">What the renderer threw; its message ends the sentence.</param>
    public PageRenderException(PageSpec page, Exception innerException)
        : base(MessageFor(page, innerException), innerException)
    {
        Page = page;
    }

    /// <summary>The page that failed; null only when the exception was built from a message alone.</summary>
    public PageSpec? Page { get; }

    /// <summary>
    /// The PRD error table's render error on a page: the page type, its title and PDF page ("none" for a page the
    /// tool draws itself), then the reason, as in "Rendering Day page 'Mon Jan 4' (PDF page 26) failed: {reason}".
    /// </summary>
    public static string Describe(PageSpec page, string reason)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(reason);
        return string.Format(
            CultureInfo.InvariantCulture,
            "Rendering {0} page '{1}' (PDF page {2}) failed: {3}",
            page.Type,
            page.Title,
            page.PdfPage?.ToString(CultureInfo.InvariantCulture) ?? "none",
            reason);
    }

    private static string MessageFor(PageSpec page, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        return Describe(page, innerException.Message);
    }
}
