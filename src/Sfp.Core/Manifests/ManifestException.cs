namespace Sfp.Core.Manifests;

/// <summary>
/// A manifest could not be loaded: the file is missing or malformed, a key is unknown, a structural check failed, or a
/// name or title pattern uses a token outside the PRD token set. The message names the file, the path inside it and the
/// offending value so a user manifest can be fixed without reading code.
/// </summary>
public sealed class ManifestException : Exception
{
    public ManifestException()
    {
    }

    public ManifestException(string message)
        : base(message)
    {
    }

    public ManifestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
