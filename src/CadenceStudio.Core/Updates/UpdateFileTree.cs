using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CadenceStudio.Core.Updates;

// Hold directories against rename/delete for the lifetime of destructive work.
// Checks alone leave a junction-swap window on Windows.
internal sealed class UpdateFileTree : IDisposable
{
    private readonly Dictionary<string, SafeFileHandle> directories = new(StringComparer.OrdinalIgnoreCase);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    public void Pin(string directory)
    {
        directory = UpdateTransactionPaths.Canonical(directory);
        var parent = Path.GetDirectoryName(directory);
        if (parent is not null && parent.Length > 3) Pin(parent);
        if (directories.ContainsKey(directory)) return;
        var handle = CreateFileW(directory, 0, 3, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot lock update directory."); }
        directories.Add(directory, handle);
        UpdateTransactionPaths.RejectReparsePoints(directory);
    }

    public string Resolve(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.StartsWith('/') ||
            relative.Split('/').Any(s => s.Length == 0 || s is "." or ".."))
            throw new InvalidDataException("Unsafe relative update path.");
        var path = UpdateTransactionPaths.Canonical(Path.Combine(root, relative.Replace('/', '\\')));
        if (!UpdateTransactionPaths.Within(path, root)) throw new InvalidDataException("Update path escapes root.");
        return path;
    }

    public void EnsureParent(string path)
    {
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent))
        {
            EnsureParent(parent);
            Directory.CreateDirectory(parent);
        }
        Pin(parent);
    }

    public Dictionary<string, string> Inventory(string root, bool installed = false)
    {
        Pin(root);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Walk(root);
        return result;
        void Walk(string dir)
        {
            Pin(dir);
            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (installed && (relative.Equals(".cadence-previous", StringComparison.OrdinalIgnoreCase) || relative.Equals(".cadence-update.lock", StringComparison.OrdinalIgnoreCase))) continue;
                UpdateTransactionPaths.Canonical(path);
                if (Directory.Exists(path)) Walk(path);
                else
                {
                    using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    result.Add(relative, UpdateMaterials.Hash(source));
                    if (result.Count > 20000) throw new InvalidDataException("Too many installed files.");
                }
            }
        }
    }

    public void Copy(string source, string destination, string expectedHash)
    {
        UpdateTransactionPaths.Canonical(source);
        UpdateTransactionPaths.Canonical(destination);
        EnsureParent(destination);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!UpdateMaterials.Hash(input).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source changed after verification.");
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".cadence-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            UpdateTransactionPaths.Canonical(destination);
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
            Verify(destination, expectedHash);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Verify(string path, string hash)
    {
        UpdateTransactionPaths.Canonical(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!UpdateMaterials.Hash(input).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed or backup file hash mismatch.");
    }

    public void Dispose()
    {
        foreach (var handle in directories.Values) handle.Dispose();
        directories.Clear();
    }
}
