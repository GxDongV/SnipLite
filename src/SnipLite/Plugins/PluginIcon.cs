using System.Drawing.Imaging;

namespace SnipLite.Plugins;

/// <summary>Plugin-owned PNG assets; no knowledge of individual plugin features.</summary>
internal static class PluginIcon
{
    internal static string AssetPath(string directory, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || relative.Contains(':') ||
            parts.Any(p => p is "" or "." or "..") || !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin icon must be a relative PNG path inside the plugin directory.");
        return Path.Combine(directory, Path.Combine(parts));
    }

    internal static Bitmap? Load(string directory, string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        var path = AssetPath(directory, relative);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Plugin icon exceeds 1 MiB.");
        using var stream = File.OpenRead(path);
        using var source = Image.FromStream(stream, false, true);
        if (source.RawFormat.Guid != ImageFormat.Png.Guid || source.Width > 1024 || source.Height > 1024)
            throw new InvalidDataException("Plugin icon must be a PNG no larger than 1024 × 1024.");
        // Detach from the file so packages can be moved and the stream can close.
        return new Bitmap(source);
    }

    internal static void Draw(Graphics graphics, Image? image, RectangleF bounds, Color fallbackColor)
    {
        if (image is null) { FluentIcons.Draw(graphics, "plugin", bounds, fallbackColor); return; }
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var width = image.Width * scale; var height = image.Height * scale;
        graphics.DrawImage(image, new RectangleF(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height));
    }
}
