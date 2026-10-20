namespace Sfp.OneNote.Com;

/// <summary>The raw OneNote.Application object plus the version string read beside it.</summary>
/// <param name="Target">The late-bound COM object (or a fake with the same method shape in tests). Called through dynamic.</param>
/// <param name="Version">The installed OneNote version, or "unknown".</param>
internal sealed record ActivatedApp(object Target, string Version);

/// <summary>
/// Creates the raw OneNote.Application target. Production resolves the ProgID on the COM thread; tests inject a fake
/// so the single-thread scheduler, the retry rule and the HRESULT mapping run without OneNote.
/// Always invoked on the wrapper's dedicated thread.
/// </summary>
internal interface IOneNoteActivator
{
    ActivatedApp Activate();
}
