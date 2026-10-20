using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Sfp.TestSupport;

namespace Sfp.Render.Tests;

/// <summary>
/// The golden hash files under tests/Sfp.Render.Tests/golden, per the convention in golden/README.md: one file per
/// fixture and runtime identifier holding "sha256  key" lines sorted by page key. SFP_RECORD_GOLDENS=1 rewrites the
/// file for the current platform instead of comparing.
/// </summary>
internal static class Goldens
{
    public const string RecordEnvVar = "SFP_RECORD_GOLDENS";
    public const string SyntheticFixture = "synthetic";
    public const string SfpFixture = "sfp";

    /// <summary>"linux-x64", "win-x64", "osx-arm64": the platform part of a golden file name.</summary>
    public static string Rid
    {
        get
        {
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            return os + "-" + arch;
        }
    }

    public static bool Recording => Environment.GetEnvironmentVariable(RecordEnvVar) == "1";

    public static string Directory => Path.Combine(TestPaths.RepoRoot, "tests", "Sfp.Render.Tests", "golden");

    public static string PathFor(string fixture) => Path.Combine(Directory, fixture + "." + Rid + ".sha256");

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Reads a golden file into key to hash.</summary>
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            int split = line.IndexOf("  ", StringComparison.Ordinal);
            if (split < 0)
            {
                throw new InvalidDataException("Golden line without two spaces in " + path + ": " + line);
            }

            hashes[line[(split + 2)..]] = line[..split];
        }

        return hashes;
    }

    /// <summary>Writes a golden file, one "sha256  key" line per page sorted by key.</summary>
    public static void Write(string path, IReadOnlyDictionary<string, string> hashes)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        IEnumerable<string> lines = hashes.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value + "  " + kv.Key);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    /// <summary>The keys whose hash differs or is missing on either side, with both hashes, for a failure message.</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        List<string> differences = new();
        foreach (string key in expected.Keys.Union(actual.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            string e = expected.GetValueOrDefault(key, "(missing)");
            string a = actual.GetValueOrDefault(key, "(missing)");
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                differences.Add(string.Format(CultureInfo.InvariantCulture, "{0}: expected {1}, got {2}", key, Short(e), Short(a)));
            }
        }

        return differences;
    }

    private static string Short(string hash) => hash.Length > 16 ? hash[..16] : hash;
}
