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
        var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        start.ArgumentList.Add("--install");
        start.ArgumentList.Add("--transaction");
        start.ArgumentList.Add(Path.Combine(transaction.StagingRoot, "transaction.json"));
        using var process = Process.Start(start) ?? throw new IOException("Updater handoff failed.");
        if (System.Windows.Application.Current is App app) app.RequestExit();
    }
}
