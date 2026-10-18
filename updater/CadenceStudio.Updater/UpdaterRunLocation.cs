using System;
using System.IO;
using System.Security.Cryptography;
using CadenceStudio.Core.Updates;

// EXPERIMENTAL: staging outside the installation directory.
// The runner location, not transaction JSON, determines the install root.
internal static class UpdaterRunLocation
{
    public static string Resolve(string baseDirectory, string transactionFile)
    {
        var directory = UpdateTransactionPaths.Canonical(
            Path.TrimEndingDirectorySeparator(baseDirectory));

        var name = Path.GetFileName(directory);

        // Preserve existing updater behavior for regression testing.
        if (string.Equals(name, "updater", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateTransactionPaths.Canonical(
                Path.GetFullPath(Path.Combine(directory, "..")));
        }

        const string marker = ".cadence-runner-";
        var index = name.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (index < 1)
            throw new InvalidDataException("Unrecognized staged updater directory.");

        var id = name[(index + marker.Length)..];

        if (!Guid.TryParseExact(id, "N", out var parsed) ||
            parsed == Guid.Empty ||
            id != parsed.ToString("N"))
            throw new InvalidDataException("Invalid staged runner identity.");

        var expectedTransaction = Path.Combine(
            UpdateTransactionPaths.UpdatesRoot, id, "transaction.json");

        if (!UpdateTransactionPaths.Same(transactionFile, expectedTransaction))
            throw new InvalidDataException("Runner and transaction identity mismatch.");

        var parent = Path.GetDirectoryName(directory)
            ?? throw new InvalidDataException("Missing runner parent.");

        var installRoot = UpdateTransactionPaths.Canonical(
            Path.Combine(parent, name[..index]));

        var installedUpdater = UpdateTransactionPaths.Canonical(
            Path.Combine(installRoot, "updater"));

        var expected = Snapshot(installedUpdater);
        var actual = Snapshot(directory);

        if (expected.Count == 0 || expected.Count != actual.Count)
            throw new InvalidDataException("Staged updater inventory mismatch.");

        foreach (var entry in expected)
        {
            if (!actual.TryGetValue(entry.Key, out var copied))
                throw new InvalidDataException("Staged updater inventory mismatch.");

            if (!entry.Key.StartsWith("F:", StringComparison.Ordinal))
                continue;

            using var originalStream = File.OpenRead(entry.Value);
            using var copiedStream = File.OpenRead(copied);

            if (originalStream.Length != copiedStream.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(originalStream),
                    SHA256.HashData(copiedStream)))
                throw new InvalidDataException(
                    "Staged updater differs from installed updater.");
        }

        static System.Collections.Generic.Dictionary<string, string> Snapshot(
            string root)
        {
            UpdateTransactionPaths.Canonical(root);

            if (!Directory.Exists(root))
                throw new InvalidDataException("Updater directory missing.");

            var result = new System.Collections.Generic.Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            var pending = new System.Collections.Generic.Stack<string>();
            pending.Push(root);

            while (pending.Count != 0)
            {
                var current = pending.Pop();

                foreach (var item in Directory.EnumerateFileSystemEntries(current))
                {
                    var path = UpdateTransactionPaths.Canonical(item);
                    var relative = Path.GetRelativePath(root, path);

                    if (Directory.Exists(path))
                    {
                        if (!result.TryAdd("D:" + relative, path))
                            throw new InvalidDataException("Duplicate directory.");

                        pending.Push(path);
                    }
                    else if (File.Exists(path))
                    {
                        if (!result.TryAdd("F:" + relative, path))
                            throw new InvalidDataException("Duplicate file.");
                    }
                    else
                    {
                        throw new InvalidDataException("Unexpected filesystem entry.");
                    }

                    if (result.Count > 2048)
                        throw new InvalidDataException("Updater inventory limit exceeded.");
                }
            }

            return result;
        }
        return installRoot;
    }
}