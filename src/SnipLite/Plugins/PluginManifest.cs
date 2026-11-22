using System.Text.Json;
using System.Text.RegularExpressions;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public int ApiVersion { get; set; }
    public string MinHostVersion { get; set; } = "0.3.14";
    public string EntryAssembly { get; set; } = "";
    public string EntryType { get; set; } = "";
    public string Architecture { get; set; } = "any";
    public string Icon { get; set; } = "";

    internal static bool ValidId(string id) => Regex.IsMatch(id, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}\z");

    internal static PluginManifest Read(string path)
    {
        var manifest = ReadIdentity(path);
        Validate(manifest, path);
        return manifest;
    }

    internal static PluginManifest ReadIdentity(string path) => JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty plugin manifest.");

    internal static void Validate(PluginManifest manifest, string path)
    {
        if (!ValidId(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name) || !System.Version.TryParse(manifest.Version, out _) ||
            string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidDataException("Invalid plugin identity or version.");
        if (manifest.ApiVersion != PluginApi.MajorVersion) throw new InvalidDataException("Unsupported plugin API version.");
        if (!System.Version.TryParse(manifest.MinHostVersion, out var minimum) ||
            minimum > System.Version.Parse(AppInfo.Version.Split('-')[0])) throw new InvalidDataException("This plugin requires a newer SnipLite version.");
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
        if (!manifest.Architecture.Equals("any", StringComparison.OrdinalIgnoreCase) &&
            !manifest.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Incompatible plugin architecture.");
        if (Path.GetFileName(manifest.EntryAssembly) != manifest.EntryAssembly ||
            !manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || manifest.EntryAssembly.Contains(':'))
            throw new InvalidDataException("Plugin entry assembly must be a DLL in its own directory.");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(path)!, manifest.EntryAssembly))) throw new FileNotFoundException("Plugin entry assembly is missing.");
        if (!string.IsNullOrEmpty(manifest.Icon)) PluginIcon.AssetPath(Path.GetDirectoryName(path)!, manifest.Icon);
    }
}

internal sealed class PluginPreferences
{
    public HashSet<string> EnabledUserPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DisabledPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Keys> Hotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Package generation, or null after uninstall to suppress any legacy copy.
    public Dictionary<string, string?> InstalledPackages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
