using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadenceStudio.Core.Updates;

public static class UpdateTransactionStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<UpdateTransactionState>(allowIntegerValues: false) }
    };

    public static UpdateTransaction Prepare(string installRoot, string targetVersion, bool syntheticTest, ReleaseManifest? manifest = null)
    {
        using var process = Process.GetCurrentProcess();
        if (!UpdateTransactionPaths.Same(process.MainModule!.FileName!, Path.Combine(installRoot, "CadenceStudio.exe")))
            throw new InvalidDataException("Handoff must originate in the installed Cadence executable.");
        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(UpdateTransactionPaths.UpdatesRoot, id);
        var t = new UpdateTransaction
        {
            Manifest = manifest, FormatVersion = 1, AppId = ProductInfo.AppId, TransactionId = id,
            CurrentVersion = ProductInfo.InformationalVersion, TargetVersion = targetVersion,
            SyntheticTest = syntheticTest, InstallRoot = installRoot,
            InstalledExecutable = Path.Combine(installRoot, "CadenceStudio.exe"),
            RestartExecutable = Path.Combine(installRoot, "CadenceStudio.exe"), StagingRoot = staging,
            PayloadPath = Path.Combine(staging, "payload", manifest is null ? "payload.zip" : UpdateMaterials.Select(manifest).FileName),
            SignaturePath = Path.Combine(staging, "payload", manifest is null ? "payload.zip.sig" : UpdateMaterials.Select(manifest).Signature.FileName),
            ExtractionPath = Path.Combine(staging, "extracted"), BackupPath = manifest is null ? Path.Combine(staging, "previous") : Path.Combine(installRoot, ".cadence-previous"),
            ProcessId = process.Id, ProcessStartUtcTicks = process.StartTime.ToUniversalTime().Ticks,
            CreatedUtc = DateTimeOffset.UtcNow, State = UpdateTransactionState.Prepared
        };
        var file = Path.Combine(staging, "transaction.json");
        UpdateTransactionPaths.Validate(t, file, installRoot);
        Directory.CreateDirectory(UpdateTransactionPaths.UpdatesRoot);
        UpdateTransactionPaths.RejectReparsePoints(UpdateTransactionPaths.UpdatesRoot);
        if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Transaction collision.");
        Directory.CreateDirectory(staging);
        UpdateTransactionPaths.Validate(t, file, installRoot);
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, t, Json);
            stream.Flush(true);
        }
        Log(t, UpdateTransactionState.Prepared, manifest is null ? "Dry-run only; payload and signature are unverified and absent." : "Release transaction prepared; materials unverified.");
        return t;
    }

    public static UpdateTransaction Read(string file, string knownInstallRoot, bool requirePrepared = true)
    {
        UpdateTransactionPaths.Canonical(file);
        if (!UpdateTransactionPaths.Within(file, UpdateTransactionPaths.UpdatesRoot))
            throw new InvalidDataException("Transaction is outside the owned root.");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 32768) throw new InvalidDataException("Transaction exceeds 32 KiB.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
        RejectDuplicates(document.RootElement);
        var t = document.Deserialize<UpdateTransaction>(Json) ?? throw new InvalidDataException("Missing transaction.");
        UpdateTransactionPaths.Validate(t, file, knownInstallRoot, requirePrepared);
        return t;
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate transaction property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    // Caller owns active.lock throughout the transition, including failure recording.
    public static UpdateTransaction Transition(UpdateTransaction t, UpdateTransactionState state, string message)
    {
        var next = t with { State = state, Reason = message };
        var file = Path.Combine(t.StagingRoot, "transaction.json");
        UpdateTransactionPaths.Validate(next, file, t.InstallRoot, requirePrepared: false);
        var temporary = Path.Combine(t.StagingRoot, $"transaction-{Guid.NewGuid():N}.tmp");
        UpdateTransactionPaths.Canonical(temporary);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, next, Json);
                stream.Flush(flushToDisk: true);
            }
            UpdateTransactionPaths.Validate(next, file, t.InstallRoot, requirePrepared: false);
            // Same-directory atomic replacement: readers see the old or complete new JSON.
            File.Replace(temporary, file, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        // Persist first; a failed save must never log a successful transition.
        Log(next, state, message);
        return next;
    }

    public static void Log(UpdateTransaction t, UpdateTransactionState state, string message)
    {
        UpdateTransactionPaths.Validate(t, Path.Combine(t.StagingRoot, "transaction.json"), t.InstallRoot, requirePrepared: false);
        var path = Path.Combine(t.StagingRoot, "updater.log");
        UpdateTransactionPaths.Canonical(path);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        if (stream.Length > 65536) throw new InvalidDataException("Transaction log limit reached.");
        using var writer = new StreamWriter(stream);
        writer.WriteLine($"{DateTimeOffset.UtcNow:O} {t.TransactionId} {t.CurrentVersion} -> {t.TargetVersion} {state}: {message}");
        writer.Flush();
        stream.Flush(true);
    }

    // Intentionally flat cleanup: unexpected files or ANY directories are retained.
    // Never recursively delete a tree supplied by transaction JSON.
    public static int CleanupAbandoned()
    {
        var root = UpdateTransactionPaths.Canonical(UpdateTransactionPaths.UpdatesRoot);
        if (!Directory.Exists(root)) return 0;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(root).Take(256))
        {
            try
            {
                if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var id) ||
                    Path.GetFileName(dir) != id.ToString("N")) continue;
                UpdateTransactionPaths.Canonical(dir);
                if (Directory.GetLastWriteTimeUtc(dir) > DateTime.UtcNow.AddDays(-7)) continue;
                var entries = Directory.GetFileSystemEntries(dir);
                if (!entries.Any(p => Path.GetFileName(p) == "transaction.json")) continue;
                if (entries.Any(p => Directory.Exists(p) ||
                    Path.GetFileName(p) is not ("transaction.json" or "updater.log" or "active.lock"))) continue;
                foreach (var entry in entries)
                {
                    UpdateTransactionPaths.Canonical(entry);
                    if (File.GetLastWriteTimeUtc(entry) > DateTime.UtcNow.AddDays(-7)) throw new IOException("Recent transaction.");
                }
                var lockPath = Path.Combine(dir, "active.lock");
                UpdateTransactionPaths.Canonical(lockPath);
                using (var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (var entry in entries.Where(p => Path.GetFileName(p) != "active.lock"))
                    {
                        UpdateTransactionPaths.Canonical(entry);
                        File.Delete(entry);
                    }
                }
                UpdateTransactionPaths.Canonical(lockPath);
                File.Delete(lockPath);
                UpdateTransactionPaths.Canonical(dir);
                Directory.Delete(dir, recursive: false);
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return removed;
    }
}
