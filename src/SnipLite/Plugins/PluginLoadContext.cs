using System.Reflection;
using System.Runtime.Loader;
using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed class PluginLoadContext(string entryPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);

    protected override Assembly? Load(AssemblyName name)
    {
        // The contract has exactly one type identity, even if a plugin ships its own copy.
        var contract = typeof(IPlugin).Assembly;
        if (name.Name == contract.GetName().Name) return contract;
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
