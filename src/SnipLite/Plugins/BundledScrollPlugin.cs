using System.Security.Cryptography;

namespace SnipLite.Plugins;

/// <summary>Materializes the official plugin for the file-based plugin loader.</summary>
internal static class BundledScrollPlugin
{
    internal const string Id = "sniplite.scroll-capture";
    internal static string CacheRoot => Path.Combine(Path.GetDirectoryName(PluginManager.UserDirectory)!, "BundledPlugins", Id);
    private const string ResourcePrefix = "SnipLite.Bundled.ScrollCapture.";
    private static readonly (string Resource, string Path)[] Files =
    [
        ("dll", "SnipLite.ScrollCapture.dll"),
        ("deps.json", "SnipLite.ScrollCapture.deps.json"),
        ("plugin.json", "plugin.json"),
        ("icon.png", "assets/scroll-capture.png")
    ];

    internal static string Prepare(string cacheRoot)
    {
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = Files.Select(file =>
        {
            using var source = typeof(BundledScrollPlugin).Assembly.GetManifestResourceStream(ResourcePrefix + file.Resource)
                ?? throw new InvalidDataException($"Missing bundled plugin resource: {file.Resource}");
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var hash = SHA256.HashData(bytes);
            fingerprint.AppendData(hash);
            return (file.Path, Bytes: bytes, Hash: hash);
        }).ToArray();

        Directory.CreateDirectory(cacheRoot);
        var key = Convert.ToHexString(fingerprint.GetHashAndReset());
        var destination = Path.Combine(cacheRoot, key);
        bool Matches(string directory) => files.All(file =>
        {
            var path = Path.Combine(directory, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes.Length) return false;
            using var existing = File.OpenRead(path);
            return SHA256.HashData(existing).AsSpan().SequenceEqual(file.Hash);
        });

        foreach (var candidate in Directory.EnumerateDirectories(cacheRoot, key + "*"))
            if (Matches(candidate)) return candidate;

        // Stage files before making a complete directory visible. A damaged cache
        // can be bypassed even when another process still holds a file open.
        var nonce = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(cacheRoot, ".extract-" + nonce);
        if (Directory.Exists(destination)) destination += "-" + nonce;
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in files)
            {
                var path = Path.Combine(staging, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Bytes);
            }
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Matches(destination)) { }
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
