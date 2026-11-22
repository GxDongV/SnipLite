using System.Runtime.InteropServices;
using System.Text.Json;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed record CatalogPlugin(string Id, string Name, string NameZhCn, string Author, string Description,
    string DescriptionZhCn, string Version, int ApiVersion, string MinHostVersion, string Architecture, Uri DownloadUrl, string Sha256)
{
    public bool IsCompatible => ApiVersion == PluginApi.MajorVersion &&
        PluginManager.CompareVersions(MinHostVersion, AppInfo.Version.Split('-')[0]) <= 0 &&
        (Architecture.Equals("any", StringComparison.OrdinalIgnoreCase) ||
            Architecture.Equals(RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase));

    public string DisplayName => Local(Name, NameZhCn);
    public string DisplayDescription => Local(Description, DescriptionZhCn);

    private static string Local(string english, string? chinese) =>
        Localization.LanguageService.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(chinese) ? chinese : english;

    public void VerifyManifest(PluginManifest manifest)
    {
        if (!Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase) ||
            PluginManager.CompareVersions(Version, manifest.Version) != 0 ||
            ApiVersion != manifest.ApiVersion ||
            PluginManager.CompareVersions(MinHostVersion, manifest.MinHostVersion) != 0 ||
            !Architecture.Equals(manifest.Architecture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.T("插件包与目录记录不匹配。"));
    }
}

internal static class PluginCatalog
{
    internal static readonly Uri Address = new("https://gxdongv.github.io/SnipLite-Plugin-Catalog/catalog.json");
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const int MaxBytes = 256 * 1024;
    private sealed record Document(int SchemaVersion, CatalogPlugin[] Plugins);

    public static async Task<IReadOnlyList<CatalogPlugin>> FetchAsync(CancellationToken token, HttpClient? client = null)
    {
        client ??= Client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync(Address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps ||
            response.Content.Headers.ContentLength > MaxBytes)
            throw new InvalidDataException(UiText.T("插件目录格式无效。"));
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var bytes = new byte[MaxBytes + 1];
        var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, timeout.Token);
        if (length > MaxBytes) throw new InvalidDataException(UiText.T("插件目录格式无效。"));
        return Parse(bytes.AsSpan(0, length));
    }

    internal static IReadOnlyList<CatalogPlugin> Parse(ReadOnlySpan<byte> json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document is not { SchemaVersion: 1, Plugins: { Length: <= 256 } entries } ||
            entries.Any(entry => !IsValid(entry) || !ids.Add(entry.Id)))
            throw new InvalidDataException(UiText.T("插件目录格式无效。"));
        return entries;
    }

    private static bool IsValid(CatalogPlugin? entry) =>
        entry is { Id: { Length: > 0 }, Name: { Length: > 0 and <= 100 }, Author: { Length: > 0 and <= 100 },
            Description: { Length: > 0 and <= 500 }, Architecture: { Length: > 0 },
            Sha256: { Length: 64 }, DownloadUrl: { IsAbsoluteUri: true } url } &&
        PluginManifest.ValidId(entry.Id) && Version.TryParse(entry.Version, out _) &&
        Version.TryParse(entry.MinHostVersion, out _) && entry.Sha256.All(Uri.IsHexDigit) &&
        url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0;
}
