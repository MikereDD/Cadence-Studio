using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace CadenceStudio.Core.Updates;

public static class UpdateInstaller
{
    public static UpdateTransaction Install(UpdateTransaction t)
    {
        if (t.State != UpdateTransactionState.MaterialsVerified || t.Manifest is null || t.SyntheticTest)
            throw new InvalidDataException("Transaction is not ready for installation.");
        using var tree = new UpdateFileTree();
        tree.Pin(t.InstallRoot);
        tree.Pin(t.StagingRoot);
        var lockPath = Path.Combine(t.InstallRoot, ".cadence-update.lock");
        UpdateTransactionPaths.Canonical(lockPath);
        using var installLease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var changed = new List<string>();
        Dictionary<string, string>? previous = null;
        var destructive = false;
        Process? launchedProcess = null;
        long launchedStartUtcTicks = 0;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!UpdateProcessIdentity.IsClosed(t))
            {
                if (DateTime.UtcNow >= deadline) throw new IOException("Main application did not exit within 30 seconds.");
                Thread.Sleep(100);
            }
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.ProcessClosed, "Validated main process is closed.");
            using var payload = UpdateMaterials.OpenVerified(t);
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.MaterialsVerified, "Updater independently verified names, sizes, both hashes and pinned signature.");
            var files = Extract(t, payload, tree);
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.Extracted, "Signed archive, identity and file inventory validated.");
            previous = tree.Inventory(t.InstallRoot, installed: true);
            // One retained backup. A pre-existing backup is never reused as this transaction's backup.
            ClearPrevious(t.BackupPath, t.InstallRoot);
            Directory.CreateDirectory(t.BackupPath);
            tree.Pin(t.BackupPath);
            foreach (var pair in previous)
                tree.Copy(tree.Resolve(t.InstallRoot, pair.Key), tree.Resolve(t.BackupPath, pair.Key), pair.Value);
            foreach (var pair in previous) UpdateFileTree.Verify(tree.Resolve(t.BackupPath, pair.Key), pair.Value);
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.BackupReady, "Exactly one complete, hash-verified prior installation retained.");
            // Recheck immediately before any installation mutation, still holding the verified ZIP handle.
            using (UpdateMaterials.OpenVerified(t)) { }
            if (!UpdateProcessIdentity.IsClosed(t)) throw new IOException("Main process is running.");
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.Replacing, "Beginning bounded installation replacement.");
            destructive = true;
            foreach (var pair in files)
            {
                if (previous.TryGetValue(pair.Key, out var oldHash) && oldHash == pair.Value) continue;
                changed.Add(pair.Key);
                tree.Copy(tree.Resolve(t.ExtractionPath, pair.Key), tree.Resolve(t.InstallRoot, pair.Key), pair.Value);
            }
            foreach (var pair in previous.Where(p => !files.ContainsKey(p.Key)))
            {
                changed.Add(pair.Key);
                var path = tree.Resolve(t.InstallRoot, pair.Key);
                File.Delete(path);
            }
            foreach (var pair in files) UpdateFileTree.Verify(tree.Resolve(t.InstallRoot, pair.Key), pair.Value);
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.InstalledVerified, "Every installed package file matches the verified signed archive.");

            UpdateTransactionPaths.Canonical(t.RestartExecutable);
            if (File.Exists(t.HealthMarkerPath)) throw new InvalidDataException("Startup-health marker unexpectedly already exists.");
            using (var executable = new FileStream(t.RestartExecutable, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (UpdateMaterials.Hash(executable) != files["CadenceStudio.exe"])
                    throw new InvalidDataException("Restart executable changed.");
            }

            var start = new ProcessStartInfo(t.RestartExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = t.InstallRoot
            };
            start.ArgumentList.Add("--update-health");
            start.ArgumentList.Add("--transaction-id");
            start.ArgumentList.Add(t.TransactionId);
            start.ArgumentList.Add("--target-version");
            start.ArgumentList.Add(t.TargetVersion);
            start.ArgumentList.Add("--health-marker");
            start.ArgumentList.Add(t.HealthMarkerPath);
            start.Environment[UpdateHealthHandshake.TokenEnvironmentVariable] = t.HealthToken;
            launchedProcess = Process.Start(start) ?? throw new IOException("Restart failed.");
            launchedStartUtcTicks = launchedProcess.StartTime.ToUniversalTime().Ticks;
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.Restarted,
                $"Validated installed CadenceStudio.exe launched as PID {launchedProcess.Id}.");
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.HealthPending,
                "Waiting for transaction-scoped startup-health confirmation.");
            if (!UpdateHealthHandshake.WaitForConfirmation(t, launchedProcess, launchedStartUtcTicks, UpdateHealthHandshake.StartupTimeout, out var healthError))
                throw new IOException(healthError);
            t = UpdateTransactionStore.Transition(t, UpdateTransactionState.HealthConfirmed,
                $"Updated Cadence Studio PID {launchedProcess.Id} confirmed startup health for {t.TargetVersion}.");
            destructive = false;
            return t;
        }
        catch (Exception failure)
        {
            if (destructive && previous is not null)
            {
                // Persistence failure must not prevent restoration of modified installation files.
                TryRecord(ref t, UpdateTransactionState.RollingBack, failure.Message);
                try
                {
                    UpdateHealthHandshake.StopForRollback(launchedProcess, t.RestartExecutable, launchedStartUtcTicks);
                    foreach (var relative in changed.AsEnumerable().Reverse().Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var target = tree.Resolve(t.InstallRoot, relative);
                        if (previous.TryGetValue(relative, out var hash))
                        {
                            // A failed atomic replacement may have left this target unchanged.
                            try { UpdateFileTree.Verify(target, hash); continue; }
                            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { }
                            tree.Copy(tree.Resolve(t.BackupPath, relative), target, hash);
                        }
                        else if (File.Exists(target)) File.Delete(target);
                    }
                    foreach (var pair in previous) UpdateFileTree.Verify(tree.Resolve(t.InstallRoot, pair.Key), pair.Value);
                    return UpdateTransactionStore.Transition(t, UpdateTransactionState.RolledBack,
                        "Prior known-good file state restored; no automatic retry. Cause: " + failure.Message);
                }
                catch (Exception rollback)
                {
                    TryRecord(ref t, UpdateTransactionState.RollbackFailed, "Backup retained for recovery. " + rollback.Message);
                    throw new IOException("Rollback failed; retain backup and transaction for manual recovery.", rollback);
                }
            }
            TryRecord(ref t, UpdateTransactionState.Failed, failure.Message);
            throw;
        }
        finally
        {
            launchedProcess?.Dispose();
        }
    }

    private static void TryRecord(ref UpdateTransaction t, UpdateTransactionState state, string reason)
    {
        try { t = UpdateTransactionStore.Transition(t, state, reason); }
        catch (Exception e) { Console.Error.WriteLine("State persistence failed: " + e.Message); }
    }

    private static Dictionary<string, string> Extract(UpdateTransaction t, Stream payload, UpdateFileTree tree)
    {
        if (Directory.Exists(t.ExtractionPath) || File.Exists(t.ExtractionPath))
            throw new InvalidDataException("Extraction directory already exists.");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is 0 or > 20000) throw new InvalidDataException("Invalid archive entry count.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.TrimEnd('/');
            tree.Resolve(t.ExtractionPath, name);
            var top = name.Split('/')[0];
            if (top.StartsWith(".cadence", StringComparison.OrdinalIgnoreCase) || !paths.Add(name) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000 or 0x4000))
                throw new InvalidDataException("Duplicate, reserved, symlink or special archive entry.");
            total = checked(total + entry.Length);
            if (total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Expanded package exceeds 2 GiB.");
        }
        Directory.CreateDirectory(t.ExtractionPath);
        tree.Pin(t.ExtractionPath);
        foreach (var entry in archive.Entries)
        {
            var path = tree.Resolve(t.ExtractionPath, entry.FullName.TrimEnd('/'));
            tree.EnsureParent(path);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); tree.Pin(path); continue; }
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long count = 0;
            int read;
            while ((read = input.Read(buffer)) != 0)
            {
                count = checked(count + read);
                if (count > entry.Length) throw new InvalidDataException("Expanded entry size mismatch.");
                output.Write(buffer, 0, read);
            }
            if (count != entry.Length) throw new InvalidDataException("Truncated archive entry.");
            output.Flush(true);
        }
        var identityPath = tree.Resolve(t.ExtractionPath, "cadence-update.json");
        if (new FileInfo(identityPath).Length > 4096) throw new InvalidDataException("Oversized package identity.");
        using var identity = JsonDocument.Parse(File.ReadAllBytes(identityPath));
        var id = identity.RootElement;
        if (id.EnumerateObject().Count() != 4 || id.GetProperty("appId").GetString() != ProductInfo.AppId ||
            id.GetProperty("version").GetString() != t.TargetVersion ||
            id.GetProperty("architecture").GetString() != UpdateMaterials.Architecture ||
            id.GetProperty("channel").GetString() != ProductInfo.UpdateChannel)
            throw new InvalidDataException("Signed package identity differs from eligible release.");
        var result = tree.Inventory(t.ExtractionPath);
        foreach (var critical in new[] { "CadenceStudio.exe", "CadenceStudio.dll", "CadenceStudio.deps.json", "CadenceStudio.runtimeconfig.json" })
            if (!result.ContainsKey(critical)) throw new InvalidDataException("Missing critical package file: " + critical);
        return result;
    }

    private static void ClearPrevious(string path, string installRoot)
    {
        if (!UpdateTransactionPaths.Same(path, Path.Combine(installRoot, ".cadence-previous")))
            throw new InvalidDataException("Invalid backup root.");
        if (!Directory.Exists(path)) return;
        // Inspect the entire old backup before removing any of it. Never traverse a reparse point.
        using (var scan = new UpdateFileTree()) { scan.Inventory(path); }
        Remove(path);
        static void Remove(string directory)
        {
            using (var pinned = new UpdateFileTree())
            {
                pinned.Pin(directory);
                foreach (var file in Directory.GetFiles(directory))
                { UpdateTransactionPaths.Canonical(file); File.Delete(file); }
                foreach (var child in Directory.GetDirectories(directory)) Remove(child);
            }
            UpdateTransactionPaths.Canonical(directory);
            Directory.Delete(directory, false);
        }
    }
}
