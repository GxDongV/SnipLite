using System.IO.Compression;

namespace SnipLite.Plugins;

/// <summary>Installs a versioned ZIP into a new plugin folder without executing its code.</summary>
internal static class PluginInstaller
{
    internal const long MaxBytes = 64 * 1024 * 1024;

    public static string Import(string package, string pluginDirectory, Func<PluginManifest, string, string>? install = null)
    {
        Directory.CreateDirectory(pluginDirectory);
        var staging = Path.Combine(pluginDirectory, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var zip = ZipFile.OpenRead(package);
            if (zip.Entries.Count > 512) throw new InvalidDataException(UiText.T("插件包文件过多。"));
            long total = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                var parts = name.TrimEnd('/').Split('/');
                if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.EndsWith('.') || p.EndsWith(' ')) ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException(UiText.T("插件包包含无效路径。"));
                var target = Path.GetFullPath(Path.Combine(staging, name));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !paths.Add(target))
                    throw new InvalidDataException(UiText.T("插件包包含无效路径。"));
                total = checked(total + entry.Length);
                if (total > MaxBytes) throw new InvalidDataException(UiText.T("插件包超过 64 MB 限制。"));
                if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew);
                CopyLimited(input, output, entry.Length);
            }
            var manifests = Directory.GetFiles(staging, "plugin.json", SearchOption.AllDirectories);
            if (manifests.Length != 1) throw new InvalidDataException(UiText.T("插件包必须包含一个 plugin.json。"));
            var manifest = PluginManifest.Read(manifests[0]);
            var source = Path.GetDirectoryName(manifests[0])!;
            if (install is not null) return install(manifest, source);
            var destination = Path.Combine(Path.GetFullPath(pluginDirectory), manifest.Id);
            if (Directory.Exists(destination)) throw new IOException(UiText.T("该插件已安装。更新前请退出软件并移走旧插件目录。"));
            Directory.Move(source, destination);
            return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public static async Task<string> DownloadAsync(Uri address, string pluginDirectory, CancellationToken token, Func<PluginManifest, string, string>? install = null)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        return await DownloadAsync(address, pluginDirectory, token, client, install);
    }

    internal static async Task<string> DownloadAsync(Uri address, string pluginDirectory, CancellationToken token, HttpClient client, Func<PluginManifest, string, string>? install = null)
    {
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps || address.UserInfo.Length > 0)
            throw new ArgumentException(UiText.T("请输入 HTTPS 插件包下载地址。"));
        var temporary = Path.Combine(Path.GetTempPath(), "SnipLite-plugin-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps || response.Content.Headers.ContentLength > MaxBytes)
                throw new InvalidDataException(UiText.T("插件包超过 64 MB 限制。"));
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > MaxBytes) throw new InvalidDataException(UiText.T("插件包超过 64 MB 限制。"));
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }
            token.ThrowIfCancellationRequested();
            return Import(temporary, pluginDirectory, install);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void CopyLimited(Stream input, Stream output, long expected)
    {
        var buffer = new byte[81920]; long total = 0; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            total += count;
            if (total > expected || total > MaxBytes) throw new InvalidDataException(UiText.T("插件包超过 64 MB 限制。"));
            output.Write(buffer, 0, count);
        }
        if (total != expected) throw new InvalidDataException(UiText.T("插件包不完整。"));
    }
}
