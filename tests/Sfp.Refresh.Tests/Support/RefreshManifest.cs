using Sfp.Core.Manifests;
using Sfp.TestSupport;

namespace Sfp.Refresh.Tests.Support;

/// <summary>The shipped manifest through the production loader (M1.2), loaded once per test process.</summary>
public static class RefreshManifest
{
    private static readonly Lazy<Manifest> ShippedLazy = new(() => new ManifestLoader().LoadShipped(TestPaths.TemplatesDir));

    public static Manifest Shipped => ShippedLazy.Value;
}
