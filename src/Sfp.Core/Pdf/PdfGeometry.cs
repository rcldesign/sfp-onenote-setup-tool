namespace Sfp.Core.Pdf;

/// <summary>
/// One word of a PDF page in points with a top-left origin, the convention of the manifest and the PRD measurements.
/// <see cref="X"/> and <see cref="Right"/> are the advance box (glyph origin to end of advance, what pdfplumber reports);
/// <see cref="Top"/> and <see cref="Bottom"/> are the ink bounds of the glyphs; <see cref="Baseline"/> is the y the
/// manifest's baselineY values refer to.
/// </summary>
/// <param name="Text">The word.</param>
/// <param name="X">Left edge, the origin of the first glyph.</param>
/// <param name="Top">Top of the glyph ink, measured down from the page top.</param>
/// <param name="Bottom">Bottom of the glyph ink, measured down from the page top.</param>
/// <param name="Right">End of the last glyph's advance.</param>
/// <param name="FontSizePt">Font size in points after page transforms.</param>
/// <param name="FontName">Font resource name, subset prefix included ("ABCDEF+BookmanOldStyle").</param>
/// <param name="Baseline">Baseline y, measured down from the page top.</param>
public sealed record WordGeometry(string Text, double X, double Top, double Bottom, double Right, double FontSizePt, string FontName, double Baseline);

/// <summary>A straight line-like path segment in points with a top-left origin.</summary>
/// <param name="X1">Start x.</param>
/// <param name="Y1">Start y, measured down from the page top.</param>
/// <param name="X2">End x.</param>
/// <param name="Y2">End y, measured down from the page top.</param>
/// <param name="StrokeWidth">Stroke width in points; for a thin filled rectangle reported as a rule, its thickness.</param>
public sealed record LineGeometry(double X1, double Y1, double X2, double Y2, double StrokeWidth);

/// <summary>Every word and line of one PDF page.</summary>
/// <param name="PageNumber">1-based page number.</param>
/// <param name="WidthPt">Page width in points.</param>
/// <param name="HeightPt">Page height in points.</param>
/// <param name="Words">Words ordered by baseline (top to bottom), then left to right.</param>
/// <param name="Lines">Segments ordered by start y, then start x.</param>
public sealed record PageGeometry(int PageNumber, double WidthPt, double HeightPt, IReadOnlyList<WordGeometry> Words, IReadOnlyList<LineGeometry> Lines);
