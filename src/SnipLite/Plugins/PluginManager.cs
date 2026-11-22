using System.Text.Json;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed class PluginRecord(string directory, bool bundled = false)
{
    public string Directory { get; } = directory;
    public bool Bundled { get; } = bundled;
    public PluginManifest? Manifest { get; set; }
    public bool Enabled { get; set; }
    public bool Loaded { get; set; }
    public string Error { get; set; } = "";
    public Bitmap? Icon { get; set; }
    public string Name => Manifest?.Id == BundledScrollPlugin.Id ? UiText.T("长截图") : Manifest?.Name ?? Path.GetFileName(Directory);
}

internal sealed record RegisteredPluginCommand(string Id, PluginText Title, Func<CancellationToken, Task> Execute, Image? Icon);

internal sealed class PluginManager : IDisposable
{
    public static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipLite", "Plugins");
    private readonly string _preferencesPath;
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
    public Task WhenIdle => _idle?.Task ?? Task.CompletedTask;
    public event Action? Changed;

    public PluginManager(IPluginHost host, Func<bool> canStart, string? preferencesPath = null)
    {
        _host = host; _canStart = canStart;
        _preferencesPath = preferencesPath ?? Path.Combine(Path.GetDirectoryName(UserDirectory)!, "plugins.json");
        try
        {
            _preferences = File.Exists(_preferencesPath)
                ? JsonSerializer.Deserialize<PluginPreferences>(File.ReadAllText(_preferencesPath)) ?? new() : new();
            _preferences.EnabledUserPlugins = new(_preferences.EnabledUserPlugins ?? [], StringComparer.OrdinalIgnoreCase);
            _preferences.DisabledPlugins = new(_preferences.DisabledPlugins ?? [], StringComparer.OrdinalIgnoreCase);
            _preferences.Hotkeys = new(_preferences.Hotkeys ?? [], StringComparer.OrdinalIgnoreCase);
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
            if (File.Exists(Path.Combine(directory, "plugin.json"))) DiscoverPlugin(directory, false);
        }
    }

    public void DiscoverBundled(string cacheRoot)
    {
        try { DiscoverPlugin(BundledScrollPlugin.Prepare(cacheRoot), true); }
        catch (Exception ex) { _host.Log("Cannot extract the bundled scrolling screenshot plugin.", ex); }
    }

    private void DiscoverPlugin(string directory, bool bundled)
    {
        if (Records.Any(record => record.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase))) return;
        var record = new PluginRecord(directory, bundled);
        try
        {
            // Consult the manifest identity before validating files or compatibility.
            // A stale manual copy is superseded even if its dependencies are missing.
            var manifestPath = Path.Combine(directory, "plugin.json");
            var manifest = PluginManifest.ReadIdentity(manifestPath);
            var existing = Records.FirstOrDefault(candidate => string.Equals(candidate.Manifest?.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
            if (existing?.Bundled == true) return;
            PluginManifest.Validate(manifest, manifestPath);
            record.Manifest = manifest;
            if (existing is not null) throw new InvalidDataException("Duplicate plugin ID: " + manifest.Id);
            try { record.Icon = PluginIcon.Load(directory, manifest.Icon); }
            catch (Exception ex) { _host.Log("Cannot read plugin icon: " + directory, ex); }
            record.Enabled = !_preferences.DisabledPlugins.Contains(manifest.Id) && (bundled || _preferences.EnabledUserPlugins.Contains(manifest.Id));
            if (record.Enabled) Load(record);
        }
        catch (Exception ex) { record.Error = ex.GetBaseException().Message; _host.Log("Cannot load plugin: " + directory, ex); }
        Records.Add(record);
    }

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

    public async Task ExecuteAsync(string id)
    {
        if (_disposed || IsRunning || !_canStart()) return;
        var command = Commands.FirstOrDefault(c => c.Id == id);
        if (command is null) return;
        using var execution = new CancellationTokenSource();
        _execution = execution;
        _runningCommand = id;
        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await command.Execute(execution.Token); }
        catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _host.Log("Plugin command failed: " + id, ex);
            if (!_disposed) MessageBox.Show(ex.GetBaseException().Message, command.Title.Get(_host.Language), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _execution = null; _runningCommand = null; _idle.TrySetResult(); }
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
        foreach (var record in Records) { record.Icon?.Dispose(); record.Icon = null; }
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
            var entry = new RegisteredPluginCommand(id, command.Title, command.ExecuteAsync, icon);
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
