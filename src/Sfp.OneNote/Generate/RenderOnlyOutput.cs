using Sfp.Render;

namespace Sfp.OneNote.Generate;

/// <summary>
/// The render-only output names. Generate with --render-only writes through <see cref="RenderOnlyRunner"/>, the
/// render-only verb's runner, so both verbs produce the same files: one {key}.png per page that has an image and the
/// pages.json index (<see cref="RenderIndex"/>). Only the index file name is left here, for the generate verb's
/// summary line; new code uses <see cref="RenderOnlyRunner.IndexFileName"/>, and this class can go once nothing
/// references it.
/// </summary>
public static class RenderOnlyOutput
{
    /// <summary>"pages.json", the same constant as <see cref="RenderOnlyRunner.IndexFileName"/>.</summary>
    public const string IndexFileName = RenderOnlyRunner.IndexFileName;
}
