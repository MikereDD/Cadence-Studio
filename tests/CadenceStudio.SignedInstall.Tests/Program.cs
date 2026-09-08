using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadenceStudio.Core;
using CadenceStudio.Core.Updates;

var root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
var targetVersion = "1.1-dev.999";
if (args.Length == 0 && File.Exists(Path.Combine(root, "fixture-mode")))
{
    File.WriteAllText(Path.Combine(root, "restarted.txt"), "validated installed executable launched");
    return 0;
}
if (args is ["--prepare", var preparedScenario])
{
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(Path.Combine(root, "test-only-private.pem")));
    var name = $"Cadence-Studio-v{targetVersion}-{UpdateMaterials.Architecture}.zip";
    var baseUrl = $"https://github.com/MikereDD/Cadence-Studio/releases/download/v{targetVersion}/";
    var releaseProtocol = preparedScenario is "previous-protocol" or "protocol-too-old"
        ? ProductInfo.UpdaterProtocolVersion + 1
        : ProductInfo.UpdaterProtocolVersion;
    var manifest = new ReleaseManifest
    {
        SchemaVersion = 2, AppId = ProductInfo.AppId, DisplayName = ProductInfo.Name, Platform = "windows",
        Architecture = UpdateMaterials.Architecture, Channel = ProductInfo.UpdateChannel, Version = targetVersion,
        PublishedAt = DateTimeOffset.UtcNow.ToString("O"), MinimumVersion = ProductInfo.InformationalVersion,
        UpdaterProtocolVersion = releaseProtocol, MinimumUpdaterProtocolVersion = ProductInfo.UpdaterProtocolVersion,
        ReleaseNotesUrl = "notes.md", ChangelogUrl = "changelog.md",
        Source = new() { RepositoryUrl = ProductInfo.SourceRepositoryUrl, Tag = "v" + targetVersion, Commit = new string('a', 40) },
        Rollback = new() { Supported = true, RetainVersions = 1, MinimumRollbackVersion = ProductInfo.InformationalVersion },
        Assets = [new() { FileName = name, DownloadUrl = baseUrl + name, Size = 1, Sha256 = new string('a', 64),
            Signature = new() { Algorithm = "ecdsa-sha256", FileName = name + ".sig", DownloadUrl = baseUrl + name + ".sig",
                Size = 1, Sha256 = new string('a', 64), KeyId = "cadence-test-only",
                PublicKeySha256 = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())) } }]
    };
    var t = UpdateTransactionStore.Prepare(root, targetVersion, false, manifest);
    Directory.CreateDirectory(Path.GetDirectoryName(t.PayloadPath)!);
    using (var zip = ZipFile.Open(t.PayloadPath, ZipArchiveMode.Create))
    {
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith(".cadence-previous/", StringComparison.Ordinal) || relative is "old-only.txt" or "test-only-private.pem") continue;
            if ((preparedScenario is "valid" or "rollback" or "previous-protocol" && relative == "CadenceStudio.exe") ||
                (preparedScenario == "valid" && relative == "updater/CadenceStudio.Updater.exe"))
            {
                using var output = zip.CreateEntry(relative).Open();
                output.Write(File.ReadAllBytes(file)); output.WriteByte(0);
            }
            else zip.CreateEntryFromFile(file, relative);
        }
        Add("cadence-update.json", JsonSerializer.Serialize(new { appId = ProductInfo.AppId,
            version = preparedScenario == "relabel" ? ProductInfo.InformationalVersion : targetVersion,
            architecture = UpdateMaterials.Architecture, channel = ProductInfo.UpdateChannel }));
        Add("a-new.txt", "new release");
        if (preparedScenario == "rollback") Add("z-blocked.txt", "replacement bytes");
        if (preparedScenario is "traversal" or "absolute" or "backslash" or "ads" or "reserved")
            Add(preparedScenario switch { "traversal" => "../escape.txt", "absolute" => "C:/escape.txt", "backslash" => "dir\\escape.txt",
                "ads" => "a.txt:stream", _ => ".cadence-previous/escape.txt" }, "bad");
        if (preparedScenario is "symlink" or "reparse")
        {
            var entry = zip.CreateEntry("link");
            entry.ExternalAttributes = preparedScenario == "symlink" ? unchecked((int)0xA1FF0000) : (int)FileAttributes.ReparsePoint;
        }
        if (preparedScenario == "duplicate") Add("A-NEW.txt", "duplicate");
        void Add(string path, string text)
        {
            using var writer = new StreamWriter(zip.CreateEntry(path).Open());
            writer.Write(text);
        }
    }
    var payload = File.ReadAllBytes(t.PayloadPath);
    File.WriteAllBytes(t.SignaturePath, key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    var asset = manifest.Assets[0];
    asset.Size = payload.Length; asset.Sha256 = Convert.ToHexString(SHA256.HashData(payload));
    asset.Signature.Size = new FileInfo(t.SignaturePath).Length;
    asset.Signature.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(t.SignaturePath)));
    using (UpdateMaterials.OpenVerified(t)) { }
    t = UpdateTransactionStore.Transition(t, UpdateTransactionState.MaterialsVerified, "Fixture main application verified signed materials.");
    // Mutations occur AFTER the main-process verification, before launching the separate updater.
    switch (preparedScenario)
    {
        case "bad-hash": asset.Sha256 = new string('0', 64); break;
        case "bad-sig-hash": asset.Signature.Sha256 = new string('0', 64); break;
        case "invalid-signature":
            var sig = File.ReadAllBytes(t.SignaturePath); sig[^1] ^= 1; File.WriteAllBytes(t.SignaturePath, sig);
            asset.Signature.Sha256 = Convert.ToHexString(SHA256.HashData(sig)); break;
        case "wrong-key": asset.Signature.KeyId = "unapproved"; break;
        case "wrong-fingerprint": asset.Signature.PublicKeySha256 = new string('0', 64); break;
        case "tamper": payload[^1] ^= 1; File.WriteAllBytes(t.PayloadPath, payload); break;
        case "wrong-size": asset.Size++; break;
        case "wrong-sig-size": asset.Signature.Size++; break;
        case "wrong-name": t = t with { PayloadPath = Path.Combine(Path.GetDirectoryName(t.PayloadPath)!, "wrong.zip") }; break;
        case "protocol-too-old":
            manifest.MinimumUpdaterProtocolVersion = ProductInfo.UpdaterProtocolVersion + 1;
            break;
        case "same-version": t = t with { TargetVersion = ProductInfo.InformationalVersion }; break;
        case "downgrade": t = t with { TargetVersion = "1.1-dev.1" }; break;
        case "escape": t = t with { InstallRoot = Path.GetDirectoryName(root)! }; break;
        case "restart-escape": t = t with { RestartExecutable = t.PayloadPath }; break;
    }
    File.WriteAllText(Path.Combine(t.StagingRoot, "transaction.json"), JsonSerializer.Serialize(t, options));
    Console.WriteLine(t.StagingRoot);
    return 0;
}

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
var suiteRoot = Path.Combine(Path.GetTempPath(), "CadenceStudio-dev5-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(suiteRoot);
foreach (var scenario in new[] { "bad-hash", "bad-sig-hash", "invalid-signature", "wrong-key", "wrong-fingerprint", "tamper",
    "wrong-size", "wrong-sig-size", "wrong-name", "same-version", "downgrade", "escape", "restart-escape", "traversal", "absolute", "backslash", "ads",
    "reserved", "symlink", "reparse", "duplicate", "relabel", "junction", "replacement-junction", "protocol-too-old",
    "previous-protocol", "rollback", "valid" })
{
    if (args is ["--case", var only] && scenario != only) continue;
    var install = Path.Combine(suiteRoot, scenario);
    Directory.CreateDirectory(install);
    foreach (var file in Directory.GetFiles(root)) File.Copy(file, Path.Combine(install, Path.GetFileName(file)));
    Directory.CreateDirectory(Path.Combine(install, "updater"));
    foreach (var file in Directory.GetFiles(Path.Combine(root, "updater"))) File.Copy(file, Path.Combine(install, "updater", Path.GetFileName(file)));
    File.WriteAllText(Path.Combine(install, "fixture-mode"), "disposable fixture");
    File.WriteAllText(Path.Combine(install, "old-only.txt"), "old release");
    var backup = Path.Combine(install, ".cadence-previous");
    Directory.CreateDirectory(backup);
    File.WriteAllText(Path.Combine(backup, "stale.txt"), "older backup must be replaced");
    var exeHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(install, "CadenceStudio.exe")));
    var prepare = Start(Path.Combine(install, "CadenceStudio.exe"), "--prepare", scenario);
    var staging = prepare.StandardOutput.ReadToEnd().Trim();
    prepare.WaitForExit();
    Check(prepare.ExitCode == 0 && Directory.Exists(staging), scenario + " main verification");
    prepare.Dispose();
    var transactionFile = Path.Combine(staging, "transaction.json");
    if (scenario is "junction" or "replacement-junction")
    {
        var link = scenario == "junction" ? Path.Combine(staging, "extracted") : backup;
        if (Directory.Exists(link)) { File.Delete(Path.Combine(link, "stale.txt")); Directory.Delete(link); }
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + root.Replace("'", "''") + "' -ErrorAction Stop | Out-Null");
        using var junction = Process.Start(psi)!; junction.WaitForExit(); Check(junction.ExitCode == 0, scenario + " created");
    }
    // Locked target is absent from the old inventory and makes replacement fail after a-new.txt was copied.
    FileStream? blocker = null;
    if (scenario == "rollback")
    {
        File.WriteAllText(Path.Combine(install, "z-blocked.txt"), "old locked file");
        blocker = new FileStream(Path.Combine(install, "z-blocked.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    using var updater = Start(Path.Combine(install, "updater", "CadenceStudio.Updater.exe"), "--install", "--transaction", transactionFile);
    var output = updater.StandardOutput.ReadToEnd();
    if (!updater.WaitForExit(45000)) throw new IOException("Updater timeout.");
    blocker?.Dispose();
    var persisted = JsonSerializer.Deserialize<UpdateTransaction>(File.ReadAllText(transactionFile), options)!;
    if (scenario == "valid")
    {
        Check(updater.ExitCode == 0 && persisted.State == UpdateTransactionState.Restarted, "fully valid signed install");
        Check(File.Exists(Path.Combine(install, "a-new.txt")) && !File.Exists(Path.Combine(install, "old-only.txt")), "new inventory installed and obsolete file removed");
        Check(File.Exists(Path.Combine(backup, "old-only.txt")) && !File.Exists(Path.Combine(backup, "stale.txt")) &&
            !Directory.Exists(Path.Combine(backup, ".cadence-previous")), "one-backup policy");
        Check(SpinWait.SpinUntil(() => File.Exists(Path.Combine(install, "restarted.txt")), 10000), "validated installed executable relaunched");
        using var replay = Start(Path.Combine(install, "updater", "CadenceStudio.Updater.exe"), "--install", "--transaction", transactionFile);
        replay.WaitForExit(); Check(replay.ExitCode != 0, "successful transaction cannot replay");
    }
    else if (scenario == "previous-protocol")
    {
        Check(updater.ExitCode == 0 && persisted.State == UpdateTransactionState.Restarted,
            "previous supported updater protocol installs current release");
    }
    else if (scenario == "rollback")
    {
        Check(updater.ExitCode != 0 && persisted.State == UpdateTransactionState.RolledBack, "failed replacement triggers rollback");
        Check(!File.Exists(Path.Combine(install, "a-new.txt")) && File.ReadAllText(Path.Combine(install, "old-only.txt")) == "old release", "rollback restores old state and removes new files");
    }
    else if (scenario == "protocol-too-old")
    {
        Check(updater.ExitCode != 0 && !File.Exists(Path.Combine(install, "a-new.txt")) && File.Exists(Path.Combine(install, "old-only.txt")),
            "updater below minimum protocol rejected before replacement");
    }
    else
    {
        Check(updater.ExitCode != 0 && !File.Exists(Path.Combine(install, "a-new.txt")) && File.Exists(Path.Combine(install, "old-only.txt")), scenario + " rejected before replacement");
    }
    var expectedExeHash = scenario is "valid" or "previous-protocol"
        ? SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "CadenceStudio.exe")).Concat(new byte[] { 0 }).ToArray()) : exeHash;
    Check(SHA256.HashData(File.ReadAllBytes(Path.Combine(install, "CadenceStudio.exe"))).SequenceEqual(expectedExeHash), scenario + " critical executable hash proven");
}
Console.WriteLine($"{passed} signed security/integration checks passed. Fixtures retained: {suiteRoot}");
return 0;

static Process Start(string exe, params string[] arguments)
{
    var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
    foreach (var arg in arguments) start.ArgumentList.Add(arg);
    return Process.Start(start) ?? throw new IOException("Fixture launch failed.");
}
