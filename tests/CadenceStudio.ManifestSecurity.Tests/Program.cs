using System.Security.Cryptography;
using System.Text;
using CadenceStudio.Core.Updates;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (InvalidDataException) { Check(true, name); return; }
    throw new InvalidOperationException("FAIL: accepted " + name);
}

var root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
using var key = ECDsa.Create();
key.ImportFromPem(File.ReadAllText(Path.Combine(root, "test-only-private.pem")));
var manifestBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":3,\"manifestSequence\":42,\"probe\":\"exact-bytes\"}");
var signature = key.SignData(
    manifestBytes,
    HashAlgorithmName.SHA256,
    DSASignatureFormat.Rfc3279DerSequence);

var authorization = ReleaseManifestAuthentication.VerifyExact(manifestBytes, signature);
Check(authorization.KeyId == "cadence-test-only" && authorization.Algorithm == "ecdsa-sha256",
    "existing P-256 release identity authorizes exact manifest bytes");

var tampered = manifestBytes.ToArray();
tampered[^2] ^= 1;
Reject(() => ReleaseManifestAuthentication.VerifyExact(tampered, signature),
    "one-byte manifest tamper rejected");
Reject(() => ReleaseManifestAuthentication.VerifyExact(manifestBytes, signature.AsSpan(0, signature.Length / 2)),
    "truncated manifest signature rejected");
Reject(() => ReleaseManifestAuthentication.VerifyExact(manifestBytes, ReadOnlySpan<byte>.Empty),
    "empty manifest signature rejected");

Check(ReleaseManifestAuthentication.AuthorizeReleaseKey("cadence-test-only", "ecdsa-sha256").KeyId == "cadence-test-only",
    "active release identity accepted");
Reject(() => ReleaseManifestAuthentication.AuthorizeReleaseKey("cadence-test-pending", "ecdsa-sha256"),
    "pending release identity cannot authorize new release");
Reject(() => ReleaseManifestAuthentication.AuthorizeReleaseKey("cadence-test-retired", "ecdsa-sha256"),
    "retired release identity cannot authorize new release");
Reject(() => ReleaseManifestAuthentication.AuthorizeReleaseKey("cadence-test-revoked", "ecdsa-sha256"),
    "revoked release identity cannot authorize new release");
Reject(() => ReleaseManifestAuthentication.AuthorizeReleaseKey("cadence-test-recovery", "ecdsa-sha256"),
    "recovery authority cannot authorize routine release");
Reject(() => ReleaseManifestAuthentication.AuthorizeReleaseKey("unapproved", "ecdsa-sha256"),
    "unknown remote key identity cannot establish trust");

var candidateVersion = "1.1-dev.999";
var assetName = $"Cadence-Studio-v{candidateVersion}-{UpdateMaterials.Architecture}.zip";
var baseUrl = $"https://github.com/MikereDD/Cadence-Studio/releases/download/v{candidateVersion}/";
var validManifest = new ReleaseManifest
{
    SchemaVersion = 3,
    ManifestSequence = 1,
    AppId = "cadence-studio",
    DisplayName = "Cadence Studio",
    Platform = "windows",
    Architecture = UpdateMaterials.Architecture,
    Channel = "development",
    Version = candidateVersion,
    PublishedAt = DateTimeOffset.UtcNow.ToString("O"),
    MinimumVersion = "1.1-dev.6",
    UpdaterProtocolVersion = 3,
    MinimumUpdaterProtocolVersion = 3,
    ReleaseNotesUrl = "notes.md",
    ChangelogUrl = "changelog.md",
    Source = new()
    {
        RepositoryUrl = "https://github.com/MikereDD/Cadence-Studio",
        Tag = "v" + candidateVersion,
        Commit = new string('a', 40)
    },
    Rollback = new()
    {
        Supported = true,
        RetainVersions = 1,
        MinimumRollbackVersion = "1.1-dev.6"
    },
    Assets =
    [
        new ReleaseAsset
        {
            FileName = assetName,
            DownloadUrl = baseUrl + assetName,
            Size = 1,
            Sha256 = new string('a', 64),
            Signature = new ReleaseSignature
            {
                Algorithm = "ecdsa-sha256",
                FileName = assetName + ".sig",
                DownloadUrl = baseUrl + assetName + ".sig",
                Size = 1,
                Sha256 = new string('b', 64),
                KeyId = "cadence-test-only",
                PublicKeySha256 = authorization.PublicKeySha256
            }
        }
    ]
};
Check(ReleaseManifestValidator.TryValidateForCadence(
        validManifest, UpdateMaterials.Architecture, out _, out _, out _),
    "schema 3 manifest requires and accepts positive sequence");
validManifest.ManifestSequence = 0;
Check(!ReleaseManifestValidator.TryValidateForCadence(
        validManifest, UpdateMaterials.Architecture, out _, out _, out var sequenceError) &&
      sequenceError.Contains("manifestSequence", StringComparison.Ordinal),
    "zero manifest sequence rejected");
validManifest.ManifestSequence = 1;

var stateRoot = Path.Combine(Path.GetTempPath(), "CadenceStudio-manifest-replay-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new ManifestReplayStateStore(stateRoot);
    var publishedAt = DateTimeOffset.UtcNow.ToString("O");
    var hash42 = ReleaseManifestAuthentication.ComputeSha256(manifestBytes);
    Check(store.Observe(42, hash42, publishedAt), "first authenticated manifest sequence persisted");
    Check(!store.Observe(42, hash42, publishedAt), "same authenticated sequence and exact bytes accepted");

    var otherBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":3,\"manifestSequence\":42,\"probe\":\"different\"}");
    var otherHash = ReleaseManifestAuthentication.ComputeSha256(otherBytes);
    Reject(() => store.Observe(42, otherHash, publishedAt), "same manifest sequence with different bytes rejected");
    Reject(() => store.Observe(41, hash42, publishedAt), "older authenticated manifest sequence rejected");
    Check(store.Observe(43, otherHash, DateTimeOffset.UtcNow.AddSeconds(1).ToString("O")),
        "newer authenticated manifest sequence advances trusted state");

    File.WriteAllText(store.StatePath, "{}");
    Reject(() => store.Observe(44, otherHash, publishedAt), "corrupt trusted replay state fails closed");
}
finally
{
    if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, recursive: true);
}

Console.WriteLine($"{passed} manifest security/replay checks passed.");
return 0;
