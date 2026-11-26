using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace SnipLite;

/// <summary>Tracks auto-save outputs independently of the screenshot filenames and user settings.</summary>
internal sealed class AutoSavedCaptures(string indexDirectory)
{
    private record Entry(string Name, DateTime SavedAt, DateTime CreatedAt, DateTime LastWrite, long Length, string Hash);
    private static readonly AutoSavedCaptures UserStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipLite", "auto-save-retention"));
    private readonly object _indexLock = new();

    public static void Record(string path) => UserStore.Add(path);

    public static void Clean(string folder, AutoSaveRetentionMode mode, int limit, DateTime now) =>
        UserStore.Apply(folder, mode, limit, now);

    internal void Add(string path)
    {
        try
        {
            var file = new FileInfo(path);
            using var stream = file.OpenRead();
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var entry = new Entry(file.Name, DateTime.UtcNow, file.CreationTimeUtc, file.LastWriteTimeUtc, file.Length, hash);
            lock (_indexLock)
            {
                var index = IndexPath(file.DirectoryName!);
                var entries = ReadIndex(index);
                entries.RemoveAll(existing => string.Equals(existing.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                entries.Add(entry);
                WriteIndex(index, entries);
            }
        }
        catch (Exception ex) when (IsFileError(ex)) { AppLog.Write("无法记录自动保存截图", ex); }
    }

    internal void Apply(string folder, AutoSaveRetentionMode mode, int limit, DateTime now)
    {
        if (limit < 1 || mode is not (AutoSaveRetentionMode.Count or AutoSaveRetentionMode.Days)) return;
        try
        {
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (!Directory.Exists(folder)) return;

            var index = IndexPath(folder);
            List<Entry> entries;
            lock (_indexLock) entries = ReadIndex(index);
            var removed = new HashSet<Entry>();
            var newest = 0;
            var cutoff = now.AddDays(-Math.Min(limit, AppSettings.MaxAutoSaveDays));
            entries.Reverse();
            foreach (var entry in entries.OrderByDescending(entry => entry.SavedAt))
            {
                if (!IsPngName(entry.Name))
                {
                    removed.Add(entry);
                    continue;
                }

                var file = new FileInfo(Path.Combine(folder, entry.Name));
                try
                {
                    if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                        file.CreationTimeUtc != entry.CreatedAt || file.LastWriteTimeUtc != entry.LastWrite || file.Length != entry.Length)
                    {
                        removed.Add(entry);
                        continue;
                    }

                    var expired = mode == AutoSaveRetentionMode.Count ? newest++ >= limit : entry.SavedAt < cutoff;
                    if (!expired) continue;
                    DeleteIfUnchanged(file.FullName, entry);
                    removed.Add(entry);
                }
                catch (FileNotFoundException) { removed.Add(entry); }
                catch (Exception ex) when (IsFileError(ex) || ex is Win32Exception) { AppLog.Write("无法清理自动保存截图", ex); }
            }

            if (removed.Count > 0)
            {
                lock (_indexLock)
                {
                    var current = ReadIndex(index);
                    current.RemoveAll(removed.Contains);
                    WriteIndex(index, current);
                }
            }
        }
        catch (Exception ex) when (IsFileError(ex)) { AppLog.Write("无法清理自动保存截图", ex); }
    }

    private static bool IsPngName(string name) =>
        !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && string.Equals(Path.GetExtension(name), ".png", StringComparison.OrdinalIgnoreCase);

    private static void DeleteIfUnchanged(string path, Entry entry)
    {
        const uint readAndDelete = 0x80010000; // GENERIC_READ | DELETE
        const uint openReparsePoint = 0x00200000;
        using var handle = CreateFile(path, readAndDelete, FileShare.Read, 0, FileMode.Open, openReparsePoint, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is 2 or 3) return; // The file was removed since enumeration.
            throw new Win32Exception(error);
        }

        using var stream = new FileStream(handle, FileAccess.Read);
        if ((File.GetAttributes(handle) & FileAttributes.ReparsePoint) != 0 ||
            File.GetCreationTimeUtc(handle) != entry.CreatedAt || File.GetLastWriteTimeUtc(handle) != entry.LastWrite ||
            stream.Length != entry.Length || Convert.ToHexString(SHA256.HashData(stream)) != entry.Hash) return;

        // Delete the verified file through its open handle, even if a path has changed.
        const int fileDispositionInfo = 4;
        if (!SetFileInformationByHandle(handle, fileDispositionInfo, [1], 1))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare sharing, nint security,
        FileMode disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, byte[] info, uint size);

    private string IndexPath(string folder)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToUpperInvariant())));
        return Path.Combine(indexDirectory, key + ".json");
    }

    private static List<Entry> ReadIndex(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return (JsonSerializer.Deserialize<List<Entry?>>(stream) ?? []).OfType<Entry>().ToList();
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    private void WriteIndex(string path, List<Entry> entries)
    {
        Directory.CreateDirectory(indexDirectory);
        var staged = path + ".tmp";
        using (var stream = File.Create(staged)) JsonSerializer.Serialize(stream, entries);
        File.Move(staged, path, overwrite: true);
    }

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;
}
