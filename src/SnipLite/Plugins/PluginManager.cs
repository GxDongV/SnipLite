using System.Text.Json;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed class PluginRecord(string directory, bool bundled = false)
{
    public string Directory { get; } = directory;
    public bool Bundled { get; } = bundled;
    public bool RestartPending { get; set; }
    public string? PendingVersion { get; set; }
    public PluginManifest? Manifest { get; set; }
    public bool IsScrollCapture => string.Equals(Manifest?.Id, BundledScrollPlugin.Id, StringComparison.OrdinalIgnoreCase);
    public bool Enabled { get; set; }
    public bool Loaded { get; set; }
    public IPluginSettings? Settings { get; set; }
    public string Error { get; set; } = "";
    public Bitmap? Icon { get; set; }
    public string Name => IsScrollCapture ? UiText.T("长截图") : Manifest?.Name ?? Path.GetFileName(Directory);
    public string Status => RestartPending ? UiText.T("重启后生效") : Error.Length > 0 ? Error : Enabled && Loaded
        ? UiText.T("已加载") : UiText.T("已停用");
}

internal sealed record RegisteredPluginCommand(string Id, PluginText Title, Func<CancellationToken, Task> Execute, Image? Icon, Func<IScreenshotPluginSession, CancellationToken, Task>? ExecuteInScreenshot = null, bool AllowScreenshotsWhileRunning = false);

internal sealed class PluginManager : IDisposable
{
    public static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipLite", "Plugins");
    private readonly string _preferencesPath;
    private readonly string _userDirectory;
    private readonly IPluginHost _host;
    private readonly Func<bool> _canStart;
    private readonly List<Context> _contexts = new();
    private readonly List<RegisteredPluginCommand> _commands = new();
    private readonly List<(string Id, IImageProcessor Processor)> _processors = new();
    private readonly List<(string Id, IOutputProvider Provider)> _outputs = new();
    private readonly List<(IPlugin Plugin, PluginLoadContext Loader)> _loaded = new();
    private PluginPreferences _preferences;
    private CancellationTokenSource? _execution;
    private TaskCompletionSource? _idle;
    private bool _disposed;
    public List<PluginRecord> Records { get; } = new();
    public IReadOnlyList<RegisteredPluginCommand> Commands => _commands.Where(c => IsEnabled(c.Id)).ToArray();
    public IReadOnlyList<(string Id, IImageProcessor Processor)> Processors => _processors.Where(c => IsEnabled(c.Id)).ToArray();
    public IReadOnlyList<(string Id, IOutputProvider Provider)> Outputs => _outputs.Where(c => IsEnabled(c.Id)).ToArray();
    private string? _runningCommand;
    private bool IsEnabled(string command) => Records.Any(r => r.Enabled && r.Loaded && command.StartsWith(r.Manifest!.Id + "/", StringComparison.Ordinal));
    public bool IsRunning => _execution is not null;
    internal bool BlocksScreenshots { get; private set; }
    public Task WhenIdle => _idle?.Task ?? Task.CompletedTask;
    public event Action? Changed;

    public PluginManager(IPluginHost host, Func<bool> canStart, string? preferencesPath = null, string? userDirectory = null)
    {
        _host = host; _canStart = canStart;
        _preferencesPath = preferencesPath ?? Path.Combine(Path.GetDirectoryName(UserDirectory)!, "plugins.json");
        _userDirectory = Path.GetFullPath(userDirectory ?? UserDirectory);
        try
        {
            _preferences = File.Exists(_preferencesPath)
                ? JsonSerializer.Deserialize<PluginPreferences>(File.ReadAllText(_preferencesPath)) ?? new() : new();
            _preferences.EnabledUserPlugins = new(_preferences.EnabledUserPlugins ?? [], StringComparer.OrdinalIgnoreCase);
            _preferences.DisabledPlugins = new(_preferences.DisabledPlugins ?? [], StringComparer.OrdinalIgnoreCase);
            _preferences.Hotkeys = new(_preferences.Hotkeys ?? [], StringComparer.OrdinalIgnoreCase);
            _preferences.InstalledPackages = new(_preferences.InstalledPackages ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { _preferences = new(); host.Log("Cannot read plugin preferences.", ex); }
    }

    public void Discover(string userDirectory)
    {
        var root = userDirectory;
        if (!Directory.Exists(root)) return;
        string[] directories;
        try { directories = Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception ex) { _host.Log("Cannot enumerate plugins: " + root, ex); return; }
        foreach (var directory in directories)
        {
            if (!Path.GetFileName(directory).StartsWith('.') && File.Exists(Path.Combine(directory, "plugin.json")))
                DiscoverPlugin(directory, false);
        }
    }

    public void DiscoverBundled(string cacheRoot)
    {
        try
        {
            var suppliedDirectory = ManagedDirectory(BundledScrollPlugin.Id);
            if (_preferences.BundledScrollCaptureRemoved)
            {
                if (suppliedDirectory is not null) DiscoverPlugin(suppliedDirectory, false);
                return;
            }

            var bundledDirectory = BundledScrollPlugin.Prepare(cacheRoot);
            var bundled = PluginManifest.Read(Path.Combine(bundledDirectory, "plugin.json"));
            var supplied = suppliedDirectory is null ? null : ReadInstalled(suppliedDirectory, bundled.Id);
            if (supplied is not null && CompareVersions(supplied.Version, bundled.Version) > 0)
            {
                var record = DiscoverPlugin(suppliedDirectory!, false, enabledByDefault: true);
                if (record?.Error.Length == 0) return;
                if (record is not null) Records.Remove(record);
                record?.Icon?.Dispose();
            }
            DiscoverPlugin(bundledDirectory, true);
        }
        catch (Exception ex) { _host.Log("Cannot extract the bundled scrolling screenshot plugin.", ex); }
    }

    // Run once at startup, after the built-in plugin has had a chance to select
    // a newer user package. The legacy folder layout remains readable.
    public void DiscoverInstalled()
    {
        foreach (var id in _preferences.InstalledPackages.Keys)
        {
            if (!PluginManifest.ValidId(id)) continue;
            var directory = ManagedDirectory(id);
            if (id.Equals(BundledScrollPlugin.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (directory is not null)
                DiscoverPlugin(directory, false);
        }
        Discover(_userDirectory);
        CleanRetiredPackages();
    }

    private PluginRecord? DiscoverPlugin(string directory, bool bundled, bool enabledByDefault = false)
    {
        var loaded = Records.FirstOrDefault(record => record.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase));
        if (loaded is not null) return loaded;
        var record = new PluginRecord(directory, bundled);
        try
        {
            // Consult the manifest identity before validating files or compatibility.
            // A stale manual copy is superseded even if its dependencies are missing.
            var manifestPath = Path.Combine(directory, "plugin.json");
            var manifest = PluginManifest.ReadIdentity(manifestPath);
            var existing = Records.FirstOrDefault(candidate => string.Equals(candidate.Manifest?.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
            if (existing?.Bundled == true) return null;
            if (!bundled && _preferences.InstalledPackages.ContainsKey(manifest.Id)
                && !string.Equals(directory, ManagedDirectory(manifest.Id), StringComparison.OrdinalIgnoreCase)) return null;
            if (!bundled && directory.StartsWith(Path.Combine(_userDirectory, ".packages") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(directory, ManagedDirectory(manifest.Id), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installed package ID differs from its recorded ID.");
            if (PluginManifest.ValidId(manifest.Id)) record.Manifest = manifest;
            PluginManifest.Validate(manifest, manifestPath);
            if (existing is not null) throw new InvalidDataException("Duplicate plugin ID: " + manifest.Id);
            try { record.Icon = PluginIcon.Load(directory, manifest.Icon); }
            catch (Exception ex) { _host.Log("Cannot read plugin icon: " + directory, ex); }
            record.Enabled = !_preferences.DisabledPlugins.Contains(manifest.Id)
                && (bundled || enabledByDefault || _preferences.EnabledUserPlugins.Contains(manifest.Id));
            if (record.Enabled) Load(record);
        }
        catch (Exception ex) { record.Error = ex.GetBaseException().Message; _host.Log("Cannot load plugin: " + directory, ex); }
        Records.Add(record);
        return record;
    }

    public void ImportPackage(string package) => PluginInstaller.Import(package, _userDirectory, InstallPackage);

    public Task DownloadPackageAsync(Uri uri, CancellationToken token) =>
        PluginInstaller.DownloadAsync(uri, _userDirectory, token, InstallPackage);

    public async Task InstallFromCatalogAsync(CatalogPlugin entry, CancellationToken token, HttpClient? client = null)
    {
        if (!entry.IsCompatible) throw new InvalidOperationException(UiText.T("与当前版本不兼容"));
        using var ownedClient = client is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(2) } : null;
        await PluginInstaller.DownloadAsync(entry.DownloadUrl, _userDirectory, token, client ?? ownedClient!, (manifest, source) =>
        {
            entry.VerifyManifest(manifest);
            return InstallPackage(manifest, source);
        }, entry.Sha256);
    }

    public PluginRecord? CatalogRecord(string id) => Find(id);

    private string InstallPackage(PluginManifest manifest, string source)
    {
        var record = Find(manifest.Id);
        var installed = ManagedDirectory(manifest.Id);
        var queued = installed is null ? null : ReadInstalled(installed, manifest.Id);
        foreach (var existing in new[] { record?.Manifest, queued })
            if (existing is not null && Version.TryParse(existing.Version, out _) &&
                CompareVersions(manifest.Version, existing.Version) <= 0)
                throw new IOException(UiText.T("请导入比已安装版本更新的插件包。"));

        var generation = Guid.NewGuid().ToString("N");
        var destination = PackageDirectory(manifest.Id, generation);
        var versionRoot = Path.GetDirectoryName(destination)!;
        foreach (var directory in new[] { _userDirectory, Path.Combine(_userDirectory, ".packages"), versionRoot })
        {
            Directory.CreateDirectory(directory);
            if (IsReparsePoint(directory)) throw new IOException(UiText.T("插件包包含无效路径。"));
        }
        Directory.Move(source, destination);
        try { SetPackage(manifest.Id, generation); }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
        if (record is not null)
        {
            record.PendingVersion = manifest.Version;
            record.RestartPending = true;
        }
        else DiscoverPlugin(destination, false);
        Changed?.Invoke();
        return destination;
    }

    public void Uninstall(PluginRecord record)
    {
        if (!CanUninstall(record))
            throw new InvalidOperationException(UiText.T("无法卸载此插件。"));
        ScheduleRemoval(record, removeBundled: record.IsScrollCapture);
    }

    public void RestoreBundled(PluginRecord record)
    {
        if (!CanRestoreBundled(record))
            throw new InvalidOperationException(UiText.T("无法卸载此插件。"));
        ScheduleRemoval(record, removeBundled: false);
    }

    private void ScheduleRemoval(PluginRecord record, bool removeBundled)
    {
        SetPackage(record.Manifest!.Id, null, record.IsScrollCapture ? removeBundled : null);
        record.PendingVersion = null;
        record.RestartPending = true;
        Changed?.Invoke();
    }

    public bool HasPendingChanges => Records.Any(record => record.RestartPending);

    public bool CanUninstall(PluginRecord record) => Records.Contains(record) && record.Manifest is not null &&
        (!record.RestartPending || record.PendingVersion is not null);

    public bool CanRestoreBundled(PluginRecord record) => record.IsScrollCapture && CanUninstall(record) &&
        ManagedDirectory(BundledScrollPlugin.Id) is not null;

    private PluginRecord? Find(string id) => Records.FirstOrDefault(record =>
        string.Equals(record.Manifest?.Id, id, StringComparison.OrdinalIgnoreCase));

    private string PackageDirectory(string id, string generation) =>
        Path.Combine(_userDirectory, ".packages", id.ToLowerInvariant(), generation);

    private string? ManagedDirectory(string id)
    {
        if (!_preferences.InstalledPackages.TryGetValue(id, out var generation) ||
            !Guid.TryParseExact(generation, "N", out var key)) return null;
        return PackageDirectory(id, key.ToString("N"));
    }

    private PluginManifest? ReadInstalled(string directory, string id)
    {
        try
        {
            var manifest = PluginManifest.Read(Path.Combine(directory, "plugin.json"));
            if (!manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installed package ID differs from its recorded ID.");
            return manifest;
        }
        catch (Exception ex)
        {
            _host.Log("Cannot read installed plugin: " + directory, ex);
            return null;
        }
    }

    internal static int CompareVersions(string left, string right)
    {
        static Version Normalize(string value)
        {
            var parsed = Version.Parse(value);
            return new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        }
        return Normalize(left).CompareTo(Normalize(right));
    }

    private void SetPackage(string id, string? generation, bool? bundledScrollCaptureRemoved = null)
    {
        var existed = _preferences.InstalledPackages.TryGetValue(id, out var previous);
        var previouslyRemoved = _preferences.BundledScrollCaptureRemoved;
        _preferences.InstalledPackages[id] = generation;
        if (bundledScrollCaptureRemoved is bool removed)
            _preferences.BundledScrollCaptureRemoved = removed;
        try { Save(); }
        catch
        {
            if (existed) _preferences.InstalledPackages[id] = previous;
            else _preferences.InstalledPackages.Remove(id);
            _preferences.BundledScrollCaptureRemoved = previouslyRemoved;
            throw;
        }
    }

    private void CleanRetiredPackages()
    {
        try
        {
            foreach (var id in _preferences.InstalledPackages.Keys)
            {
                if (!PluginManifest.ValidId(id)) continue;
                var active = ManagedDirectory(id);
                if (active is not null && !Records.Any(record =>
                    record.Error.Length == 0 && string.Equals(record.Directory, active, StringComparison.OrdinalIgnoreCase))) continue;
                var root = Path.GetDirectoryName(PackageDirectory(id, Guid.Empty.ToString("N")))!;
                if (!Directory.Exists(root) || IsReparsePoint(root) ||
                    IsReparsePoint(Path.GetDirectoryName(root)!) || IsReparsePoint(_userDirectory)) continue;
                foreach (var directory in Directory.EnumerateDirectories(root))
                    if (!string.Equals(active, directory, StringComparison.OrdinalIgnoreCase) &&
                        Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) TryDeleteDirectory(directory);
            }
            if (!Directory.Exists(_userDirectory) || IsReparsePoint(_userDirectory)) return;
            foreach (var directory in Directory.EnumerateDirectories(_userDirectory))
            {
                if (Path.GetFileName(directory).StartsWith('.') || IsReparsePoint(directory) ||
                    !File.Exists(Path.Combine(directory, "plugin.json"))) continue;
                try
                {
                    var manifest = PluginManifest.ReadIdentity(Path.Combine(directory, "plugin.json"));
                    if (_preferences.InstalledPackages.TryGetValue(manifest.Id, out var generation) && generation is null)
                        TryDeleteDirectory(directory);
                }
                catch (Exception ex) { _host.Log("Cannot inspect legacy plugin directory: " + directory, ex); }
            }
        }
        catch (Exception ex) { _host.Log("Cannot clean retired plugin packages.", ex); }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !IsReparsePoint(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) { _host.Log("Cannot delete retired plugin directory: " + directory, ex); }
    }

    private static bool IsReparsePoint(string directory) => (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;

    private void Load(PluginRecord record)
    {
        var manifest = record.Manifest!;
        var directory = record.Directory;
        var loader = new PluginLoadContext(Path.Combine(directory, manifest.EntryAssembly));
        Context? context = null;
        IPlugin? plugin = null;
        try
        {
            var assembly = loader.LoadFromAssemblyPath(Path.Combine(directory, manifest.EntryAssembly));
            var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
            plugin = Activator.CreateInstance(type) as IPlugin ?? throw new InvalidDataException("Entry type must implement IPlugin.");
            context = new Context(this, manifest.Id, record.Icon);
            plugin.Initialize(context);
            record.Settings = plugin as IPluginSettings;
            _contexts.Add(context); _loaded.Add((plugin, loader)); record.Loaded = true;
        }
        catch
        {
            context?.Dispose();
            try { plugin?.Dispose(); } catch (Exception ex) { _host.Log("Plugin cleanup failed.", ex); }
            loader.Unload(); throw;
        }
    }

    public Keys Hotkey(string id) => _preferences.Hotkeys.GetValueOrDefault(id);

    public void SetHotkey(string id, Keys value)
    {
        if (!_commands.Any(c => c.Id == id)) throw new ArgumentException("Unknown plugin command.");
        var previous = Hotkey(id);
        _preferences.Hotkeys[id] = value;
        try { Save(); } catch { _preferences.Hotkeys[id] = previous; throw; }
        Changed?.Invoke();
    }

    public void SetEnabled(PluginRecord record, bool enabled)
    {
        if (record.Manifest is null || record.Error.Length > 0) return;
        var id = record.Manifest.Id;
        var previous = record.Enabled;
        void Set(bool value)
        {
            if (value) { _preferences.DisabledPlugins.Remove(id); _preferences.EnabledUserPlugins.Add(id); }
            else { _preferences.DisabledPlugins.Add(id); _preferences.EnabledUserPlugins.Remove(id); }
        }
        if (enabled && !record.Loaded) Load(record);
        Set(enabled);
        try { Save(); } catch { Set(previous); throw; }
        record.Enabled = enabled;
        if (!enabled && _runningCommand?.StartsWith(id + "/", StringComparison.Ordinal) == true) Cancel();
        Changed?.Invoke();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
        File.WriteAllText(_preferencesPath + ".tmp", JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(_preferencesPath + ".tmp", _preferencesPath, true);
    }

    internal async Task ExecuteFromSelectionAsync(string id, Rectangle selection, PluginHostServices host)
    {
        // Let DWM remove the screenshot window before the plugin opens its live UI.
        await Task.Delay(180);
        if (_disposed || IsRunning || !_canStart()) return;
        host.PendingSelection = selection;
        try { await ExecuteAsync(id); }
        finally { host.PendingSelection = null; }
    }

    internal async Task ExecuteFromScreenshotAsync(CaptureForm overlay, string id)
    {
        using var session = (IDisposable)overlay.CreatePluginSession();
        void CancelPlugin(object? _, FormClosedEventArgs __) => Cancel();
        overlay.FormClosed += CancelPlugin;
        try { await ExecuteAsync(id, (IScreenshotPluginSession)session); }
        finally { overlay.FormClosed -= CancelPlugin; }
    }

    public async Task ExecuteAsync(string id, IScreenshotPluginSession? screenshot = null)
    {
        if (_disposed || IsRunning || (screenshot is null && !_canStart())) return;
        var command = Commands.FirstOrDefault(c => c.Id == id);
        if (command is null) return;
        using var execution = new CancellationTokenSource();
        _execution = execution;
        _runningCommand = id;
        BlocksScreenshots = !command.AllowScreenshotsWhileRunning || screenshot is not null;
        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (screenshot is not null && command.ExecuteInScreenshot is { } docked)
                await docked(screenshot, execution.Token);
            else if (screenshot is null)
                await command.Execute(execution.Token);
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _host.Log("Plugin command failed: " + id, ex);
            if (!_disposed) MessageBox.Show(ex.GetBaseException().Message, command.Title.Get(_host.Language), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _execution = null; _runningCommand = null; BlocksScreenshots = false; _idle.TrySetResult(); }
    }

    public void Cancel() => _execution?.Cancel();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Cancel();
        foreach (var context in _contexts) context.Dispose();
        foreach (var (plugin, loader) in _loaded)
        {
            try { plugin.Dispose(); } catch (Exception ex) { _host.Log("Plugin disposal failed.", ex); }
            loader.Unload();
        }
        _contexts.Clear(); _loaded.Clear();
        foreach (var record in Records) { record.Icon?.Dispose(); record.Icon = null; record.Settings = null; }
    }

    private sealed class Registration(Action remove) : IDisposable
    {
        private Action? _remove = remove;
        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }

    private sealed class Context(PluginManager manager, string pluginId, Image? icon) : IPluginContext, IDisposable
    {
        private readonly List<IDisposable> _registrations = new();
        public IPluginHost Host => manager._host;
        public string DataDirectory => Path.Combine(Path.GetDirectoryName(UserDirectory)!, "PluginData", pluginId);

        private string FullId(string id)
        {
            if (!PluginManifest.ValidId(id)) throw new ArgumentException("Invalid contribution ID.");
            return pluginId + "/" + id;
        }

        public IDisposable RegisterCommand(PluginCommand command)
        {
            var id = FullId(command.Id);
            if (manager._commands.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Duplicate command: " + id);
            var entry = new RegisteredPluginCommand(id, command.Title, command.ExecuteAsync, icon, command.ExecuteInScreenshotAsync, command.AllowScreenshotsWhileRunning);
            manager._commands.Add(entry);
            var token = new Registration(() => manager._commands.Remove(entry));
            _registrations.Add(token); return token;
        }

        public IDisposable RegisterCaptureProvider(ICaptureProvider provider) => RegisterCommand(new(provider.Id, provider.Title, async token =>
        {
            using var result = await provider.CaptureAsync(Host, token);
            token.ThrowIfCancellationRequested();
            if (result is not null) await Host.PreviewImageAsync(result, token);
        }));

        public IDisposable RegisterImageProcessor(IImageProcessor processor)
        {
            var command = RegisterCommand(new(processor.Id, processor.Title, async token =>
            {
                using var input = await Host.SelectImageAsync(token);
                if (input is null) return;
                using var output = await processor.ProcessAsync(input, Host, token);
                if (output is not null)
                {
                    if (ReferenceEquals(input.Image, output.Image)) throw new InvalidOperationException("A processor must return a new image.");
                    token.ThrowIfCancellationRequested(); await Host.PreviewImageAsync(output, token);
                }
            }));
            var entry = (FullId(processor.Id), processor); manager._processors.Add(entry);
            var registration = new Registration(() => { command.Dispose(); manager._processors.Remove(entry); });
            _registrations.Add(registration); return registration;
        }

        public IDisposable RegisterOutputProvider(IOutputProvider provider)
        {
            var command = RegisterCommand(new(provider.Id, provider.Title, async token =>
            {
                using var input = await Host.SelectImageAsync(token);
                if (input is not null) await provider.ExportAsync(input, Host, token);
            }));
            var entry = (FullId(provider.Id), provider); manager._outputs.Add(entry);
            var registration = new Registration(() => { command.Dispose(); manager._outputs.Remove(entry); });
            _registrations.Add(registration); return registration;
        }

        public void Dispose()
        {
            for (var i = _registrations.Count - 1; i >= 0; i--) _registrations[i].Dispose();
            _registrations.Clear();
        }
    }
}
