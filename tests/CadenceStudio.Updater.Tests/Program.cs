using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadenceStudio.Core;
using CadenceStudio.Core.Updates;

var install = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
if (args is ["--prepare-and-exit"])
{
    Console.WriteLine(UpdateTransactionStore.Prepare(install, ProductInfo.InformationalVersion, true).StagingRoot);
    return 0;
}
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException or IOException)
    { Check(true, name); return; }
    throw new Exception("FAIL: accepted " + name);
}
int Run(string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in arguments) start.ArgumentList.Add(arg);
    using var p = Process.Start(start)!;
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    if (!p.WaitForExit(180000)) throw new Exception("Fixture timed out.");
    Console.Write(stdout.GetAwaiter().GetResult());
    Console.Error.Write(stderr.GetAwaiter().GetResult());
    return p.ExitCode;
}
var t = UpdateTransactionStore.Prepare(install, ProductInfo.InformationalVersion, true);
var file = Path.Combine(t.StagingRoot, "transaction.json");
var updater = Path.Combine(install, "updater", "CadenceStudio.Updater.exe");
void Validate(UpdateTransaction value) => UpdateTransactionPaths.Validate(value, file, install);
Check(UpdateTransactionStore.Read(file, install) == t, "transaction round trip");
Check(!UpdateProcessIdentity.IsClosed(t), "matching live process");
Check(Run(updater, "--dry-run", "--transaction", file) == 0, "separate updater live-process handoff");
Check(File.ReadAllText(Path.Combine(t.StagingRoot, "updater.log")).Contains("DryRunCompleted"), "logged terminal state");
Check(UpdateTransactionStore.Read(file, install, requirePrepared: false).State == UpdateTransactionState.DryRunCompleted, "durable live terminal state");
var completedJson = File.ReadAllText(file);
Check(Run(updater, "--dry-run", "--transaction", file) == 1 && File.ReadAllText(file) == completedJson, "completed replay rejected without mutation");
using (var released = new FileStream(Path.Combine(t.StagingRoot, "active.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    Check(true, "completion releases lock");
var lifecycle = UpdateTransactionStore.Prepare(install, ProductInfo.InformationalVersion, true);
var lifecycleFile = Path.Combine(lifecycle.StagingRoot, "transaction.json");
using (var held = new FileStream(Path.Combine(lifecycle.StagingRoot, "active.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
{
    Check(Run(updater, "--dry-run", "--transaction", lifecycleFile) == 1 && UpdateTransactionStore.Read(lifecycleFile, install) == lifecycle,
        "lock contention cannot overwrite owner state");
    foreach (var state in new[] { UpdateTransactionState.Validated, UpdateTransactionState.ProcessRunning, UpdateTransactionState.ProcessClosed, UpdateTransactionState.DryRunCompleted, UpdateTransactionState.Failed })
    {
        lifecycle = UpdateTransactionStore.Transition(lifecycle, state, "Lifecycle persistence fixture.");
        Check(UpdateTransactionStore.Read(lifecycleFile, install, requirePrepared: false) == lifecycle, "durable transition " + state);
    }
    using (var blocked = new FileStream(lifecycleFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        Reject(() => UpdateTransactionStore.Transition(lifecycle, UpdateTransactionState.DryRunCompleted, "Must not be logged."), "replacement failure propagates");
    Check(UpdateTransactionStore.Read(lifecycleFile, install, requirePrepared: false) == lifecycle &&
        !File.ReadAllText(Path.Combine(lifecycle.StagingRoot, "updater.log")).Contains("Must not be logged.") &&
        Directory.GetFiles(lifecycle.StagingRoot, "*.tmp").Length == 0, "failed save preserves JSON and cleans temporary file");
}
var failure = UpdateTransactionStore.Prepare(install, ProductInfo.InformationalVersion, true);
var failureFile = Path.Combine(failure.StagingRoot, "transaction.json");
File.WriteAllText(failureFile, JsonSerializer.Serialize(failure with { ProcessStartUtcTicks = failure.ProcessStartUtcTicks - 1 }, json));
Check(Run(updater, "--dry-run", "--transaction", failureFile) == 1 &&
    UpdateTransactionStore.Read(failureFile, install, requirePrepared: false).State == UpdateTransactionState.Failed, "identity failure persisted by updater");
using (var released = new FileStream(Path.Combine(failure.StagingRoot, "active.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    Check(true, "failure releases lock");
Check(!File.Exists(t.PayloadPath) && !Directory.Exists(t.BackupPath), "no payload or backup created");
Check(Run(updater) == 2, "missing required arguments");
Check(Run(updater, "--install", "--transaction", file) == 2, "install requires verified release transaction");
Check(Run(updater, "--dry-run", "--transaction", file, "--dry-run") == 2, "extra argument rejected");
Reject(() => Validate(t with { TransactionId = "../escape" }), "invalid transaction ID");
Reject(() => Validate(t with { FormatVersion = 0 }), "old local format");
Reject(() => Validate(t with { AppId = "other-app" }), "wrong application");
Reject(() => Validate(t with { State = UpdateTransactionState.DryRunCompleted }), "invalid input state");
Reject(() => Validate(t with { TargetVersion = "1.0" }), "downgrade/channel switch");
Reject(() => Validate(t with { SyntheticTest = false }), "real same-version reinstall");
Reject(() => Validate(t with { RestartExecutable = t.PayloadPath }), "payload cannot select restart");
Reject(() => Validate(t with { InstallRoot = t.StagingRoot }), "install/staging mismatch");
Reject(() => Validate(t with { PayloadPath = t.StagingRoot + "-sibling\\payload.zip" }), "prefix sibling escape");
Reject(() => Validate(t with { BackupPath = install }), "backup escape");
Reject(() => UpdateTransactionPaths.Canonical(t.StagingRoot + "\\..\\escape"), "traversal");
Reject(() => UpdateTransactionPaths.Canonical(t.PayloadPath + ":stream"), "alternate data stream");
Reject(() => UpdateTransactionPaths.Canonical(@"\\?\C:\test"), "device path");
Reject(() => UpdateTransactionPaths.Canonical(t.StagingRoot + "\\trailing."), "trailing dot");
Reject(() => UpdateProcessIdentity.IsClosed(t with { ProcessStartUtcTicks = t.ProcessStartUtcTicks - 1 }), "PID reuse identity conflict");
var original = JsonSerializer.Serialize(t, new JsonSerializerOptions(json) { WriteIndented = true });
File.WriteAllText(file, original.Replace("\"FormatVersion\": 1,", "\"Unexpected\": true, \"FormatVersion\": 1,"));
Reject(() => UpdateTransactionStore.Read(file, install), "unknown JSON property");
File.WriteAllText(file, new string(' ', 32769));
Reject(() => UpdateTransactionStore.Read(file, install), "oversized JSON");
File.WriteAllText(file, original.Replace("\"FormatVersion\": 1,", "\"FormatVersion\": 1, \"FormatVersion\": 1,"));
Reject(() => UpdateTransactionStore.Read(file, install), "duplicate JSON property");
File.WriteAllText(file, "{}");
Reject(() => UpdateTransactionStore.Read(file, install), "missing required JSON fields");
File.WriteAllText(file, original);
var childStart = new ProcessStartInfo(Path.Combine(install, "CadenceStudio.exe"))
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
childStart.ArgumentList.Add("--prepare-and-exit");
using var child = Process.Start(childStart)!;
var exitedStaging = child.StandardOutput.ReadToEnd().Trim();
Check(child.WaitForExit(20000) && child.ExitCode == 0, "fixture main process exits before wait");
var exitedFile = Path.Combine(exitedStaging, "transaction.json");
Check(Run(updater, "--dry-run", "--transaction", exitedFile) == 0, "already-exited process accepted");
Check(File.ReadAllText(Path.Combine(exitedStaging, "updater.log")).Contains("ProcessClosed"), "closed state logged");
Check(UpdateTransactionStore.Read(exitedFile, install, requirePrepared: false).State == UpdateTransactionState.DryRunCompleted, "durable exited terminal state");
ReleaseVersion.TryParse("1.1-dev.6.3", out var a);
ReleaseVersion.TryParse("1.1-dev.6.4", out var b);
Check(a!.CompareTo(b) < 0, "dev.3 multipart comparison preserved");
var sentinel = Path.Combine(t.StagingRoot, "keep.txt");
File.WriteAllText(sentinel, "must survive");
foreach (var entry in Directory.GetFiles(t.StagingRoot)) File.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddDays(-8));
Directory.SetLastWriteTimeUtc(t.StagingRoot, DateTime.UtcNow.AddDays(-8));
UpdateTransactionStore.CleanupAbandoned();
Check(File.Exists(sentinel), "unknown content retained by cleanup");
File.Delete(sentinel);
var leasePath = Path.Combine(t.StagingRoot, "active.lock");
foreach (var entry in Directory.GetFiles(t.StagingRoot)) File.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddDays(-8));
Directory.SetLastWriteTimeUtc(t.StagingRoot, DateTime.UtcNow.AddDays(-8));
using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    UpdateTransactionStore.CleanupAbandoned();
    Check(File.Exists(file), "active transaction retained by cleanup");
}
// PowerShell creates an ordinary directory junction without developer-mode requirements.
var junction = Path.Combine(t.StagingRoot, "extracted");
var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-Command");
psi.ArgumentList.Add("New-Item -ItemType Junction -Path '" + junction.Replace("'", "''") +
    "' -Target '" + install.Replace("'", "''") + "' -ErrorAction Stop | Out-Null");
using (var p = Process.Start(psi)!) { Check(p.WaitForExit(180000) && p.ExitCode == 0, "junction fixture created"); }
Reject(() => Validate(t), "reparse-point extraction rejected");
UpdateTransactionStore.CleanupAbandoned();
Check(File.Exists(file), "junction staging retained by cleanup");
// Remove only the fixture junction itself, never its target or children.
if ((File.GetAttributes(junction) & FileAttributes.ReparsePoint) == 0) throw new IOException("Fixture changed.");
Directory.Delete(junction, recursive: false);
// Cleanup only the fixture-owned flat directories by exercising the production age policy.
foreach (var dir in new[] { t.StagingRoot, exitedStaging, lifecycle.StagingRoot, failure.StagingRoot })
{
    foreach (var entry in Directory.GetFiles(dir)) File.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddDays(-8));
    Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddDays(-8));
}
UpdateTransactionStore.CleanupAbandoned();
Check(!Directory.Exists(t.StagingRoot) && !Directory.Exists(exitedStaging), "expired flat staging cleanup");
Console.WriteLine($"{passed} dev.4 regression tests passed.");
Check(Run(Path.Combine(install, "signed-tests", "CadenceStudio.exe"), "--suite") == 0, "signed dev.5 integration suite");
Console.WriteLine($"{passed} top-level checks passed.");
return 0;
