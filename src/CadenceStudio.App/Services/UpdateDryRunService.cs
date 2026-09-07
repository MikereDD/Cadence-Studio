using System.Diagnostics;
using System.IO;
using CadenceStudio.Core;
using CadenceStudio.Core.Updates;

namespace CadenceStudio.App.Services;

public static class UpdateDryRunService
{
    public static async Task<string> RunAsync(UpdateCheckResult? eligible)
    {
        var installRoot = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var updater = Path.Combine(installRoot, "updater", "CadenceStudio.Updater.exe");
        UpdateTransactionPaths.Canonical(updater);
        if (!File.Exists(updater)) throw new FileNotFoundException("The packaged dry-run updater is missing.");
        UpdateTransactionStore.CleanupAbandoned();
        var candidate = eligible?.State == UpdateCheckState.UpdateAvailable ? eligible.CandidateVersion : null;
        var transaction = UpdateTransactionStore.Prepare(installRoot,
            candidate ?? ProductInfo.InformationalVersion, syntheticTest: candidate is null);
        var start = new ProcessStartInfo(updater)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = installRoot
        };
        start.ArgumentList.Add("--dry-run");
        start.ArgumentList.Add("--transaction");
        start.ArgumentList.Add(Path.Combine(transaction.StagingRoot, "transaction.json"));
        using var process = Process.Start(start) ?? throw new IOException("Could not start the dry-run updater.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            return $"Dry-run response timed out. Inspect {transaction.StagingRoot} before retrying.";
        }
        return process.ExitCode == 0
            ? $"{(transaction.SyntheticTest ? "Synthetic test" : "Eligible update dry run")} completed. Cadence remains open. Log: {transaction.StagingRoot}"
            : $"Dry run rejected (exit {process.ExitCode}). Log: {transaction.StagingRoot}";
    }
}
