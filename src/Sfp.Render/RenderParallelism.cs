namespace Sfp.Render;

/// <summary>
/// How many pages are rendered at once by default, for render-only and the generate pipeline alike: the processor
/// count, capped at <see cref="MaxDefault"/>. Each page in flight holds its rasterized sheet and the RGB copy the PNG
/// encoder makes (about 64 MB at 220 DPI, 119 MB at 300 DPI), so four workers plus the rasterizer's page cache stay
/// inside the PRD's 1.5 GB budget on any machine. PDFium is serialized by the rasterizer, so more workers would only
/// overlap more overlays and PNG encodes, not rasterization.
/// </summary>
public static class RenderParallelism
{
    /// <summary>The cap on the default worker count.</summary>
    public const int MaxDefault = 4;

    /// <summary>The default worker count on this machine: <see cref="For"/> of <see cref="Environment.ProcessorCount"/>.</summary>
    public static int Default => For(Environment.ProcessorCount);

    /// <summary>The default worker count for a processor count: at least one, at most <see cref="MaxDefault"/>.</summary>
    public static int For(int processorCount) => Math.Clamp(processorCount, 1, MaxDefault);
}
