using System.Diagnostics;
using System.IO;
using CadenceStudio.Core.Updates;

namespace CadenceStudio.App.Services;

public static class UpdateInstallService
{
    public static async Task InstallAsync(UpdateCheckResult eligible)
    {
        if (eligible.State != UpdateCheckState.UpdateAvailable || eligible.Manifest is null)
            throw new InvalidDataException("Check for an eligible release first.");
        var root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var updater = UpdateTransactionPaths.Canonical(Path.Combine(root, "updater", "CadenceStudio.Updater.exe"));
        if (!File.Exists(updater)) throw new FileNotFoundException("Packaged updater is missing.");
        var transaction = UpdateTransactionStore.Prepare(root, eligible.Manifest.Version, false, eligible.Manifest);
        transaction = await UpdateDownload.StageAsync(transaction);
        updater = StageUpdater(transaction);
        var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        start.ArgumentList.Add("--install");
        start.ArgumentList.Add("--transaction");
        start.ArgumentList.Add(Path.Combine(transaction.StagingRoot, "transaction.json"));
        using var process = Process.Start(start) ?? throw new IOException("Updater handoff failed.");
        if (System.Windows.Application.Current is App app) app.RequestExit();
    }

    private static string StageUpdater(UpdateTransaction transaction)
    {
        if (transaction.State != UpdateTransactionState.MaterialsVerified ||
            transaction.SyntheticTest)
            throw new InvalidDataException("Verified release materials required.");

        var install = transaction.InstallRoot;
        var source = Path.Combine(install, "updater");

        var parent = Path.GetDirectoryName(install)
            ?? throw new InvalidDataException("Missing install parent.");

        var destination = Path.Combine(
            parent,
            Path.GetFileName(install) +
            ".cadence-runner-" + transaction.TransactionId);

        UpdateTransactionPaths.Canonical(source);
        UpdateTransactionPaths.Canonical(destination);

        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Staged updater directory already exists.");

        var files = Directory.GetFiles(
            source, "*", SearchOption.AllDirectories);

        if (files.Length == 0)
            throw new InvalidDataException("Packaged updater is empty.");

        Directory.CreateDirectory(destination);

        foreach (var file in files)
        {
            UpdateTransactionPaths.Canonical(file);

            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            UpdateTransactionPaths.Canonical(target);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);

            using var original = File.OpenRead(file);
            using var copied = File.OpenRead(target);

            if (!System.Security.Cryptography.SHA256.HashData(original)
                .SequenceEqual(
                    System.Security.Cryptography.SHA256.HashData(copied)))
                throw new InvalidDataException(
                    "Staged updater copy verification failed.");
        }

        return Path.Combine(destination, "CadenceStudio.Updater.exe");
    }
}
