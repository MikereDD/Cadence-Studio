using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CadenceStudio.Core.Updates;

public static class UpdateMaterials
{
    public static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "win-x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "win-arm64",
        _ => throw new InvalidDataException("Unsupported update architecture.")
    };

    public static ReleaseAsset Select(ReleaseManifest manifest)
    {
        if (!ReleaseManifestValidator.TryValidateForCadence(manifest, Architecture,
            out var target, out var minimum, out var error)) throw new InvalidDataException(error);
        if (!ReleaseVersion.TryParse(ProductInfo.InformationalVersion, out var current))
            throw new InvalidDataException("Invalid compiled version.");
        if (target!.IsDevelopment != current!.IsDevelopment || target.CompareTo(current) <= 0 ||
            current.CompareTo(minimum) < 0 || manifest.MinimumUpdaterProtocolVersion > ProductInfo.UpdaterProtocolVersion)
            throw new InvalidDataException("Ineligible version, channel, or updater protocol.");
        var asset = manifest.Assets.Single(a => a.FileName ==
            $"{ProductInfo.ReleaseAssetProductName}-v{manifest.Version}-{Architecture}.zip");
        if (asset.Size > 1024L * 1024 * 1024 || asset.Signature.Size > 16384 ||
            !string.IsNullOrEmpty(asset.AuthenticodeSignerThumbprint))
            throw new InvalidDataException("Unsupported size or Authenticode policy; use a full installer.");
        CheckOrigin(asset.DownloadUrl, manifest.Version, asset.FileName);
        CheckOrigin(asset.Signature.DownloadUrl, manifest.Version, asset.Signature.FileName);
        return asset;
    }

    public static void CheckOrigin(string value, string version, string name)
    {
        var expected = $"https://github.com/MikereDD/Cadence-Studio/releases/download/v{version}/{name}";
        if (!string.Equals(value, expected, StringComparison.Ordinal))
            throw new InvalidDataException("Unapproved asset origin or filename.");
    }

    public static string Hash(Stream stream)
    {
        stream.Position = 0;
        var result = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        return result;
    }

    public static FileStream OpenVerified(UpdateTransaction t)
    {
        if (t.SyntheticTest || t.Manifest is null || t.Manifest.Version != t.TargetVersion)
            throw new InvalidDataException("Install requires release materials, not a dry-run fixture.");
        var asset = Select(t.Manifest);
        CheckFile(t.PayloadPath, asset.FileName, asset.Size);
        CheckFile(t.SignaturePath, asset.Signature.FileName, asset.Signature.Size);
        var payload = new FileStream(t.PayloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using var signature = new FileStream(t.SignaturePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (payload.Length != asset.Size || signature.Length != asset.Signature.Size ||
                !Hash(payload).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload size or SHA-256 mismatch.");
            if (!Hash(signature).Equals(asset.Signature.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Signature-file SHA-256 mismatch.");
            if (string.IsNullOrEmpty(ReleaseTrustAnchor.KeyId) || string.IsNullOrEmpty(ReleaseTrustAnchor.PublicKeyPem))
                throw new InvalidDataException("Production release trust anchor has not been provisioned.");
            using var key = ECDsa.Create();
            key.ImportFromPem(ReleaseTrustAnchor.PublicKeyPem);
            var fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
            if (asset.Signature.KeyId != ReleaseTrustAnchor.KeyId ||
                asset.Signature.Algorithm != ReleaseTrustAnchor.Algorithm || key.KeySize != 256 ||
                !fingerprint.Equals(asset.Signature.PublicKeySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unapproved signing identity.");
            var bytes = new byte[checked((int)signature.Length)];
            signature.ReadExactly(bytes);
            if (!key.VerifyData(payload, bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                throw new InvalidDataException("Invalid detached signature.");
            payload.Position = 0;
            // Keep this handle open through extraction: Windows denies writes/deletes.
            return payload;
        }
        catch { payload.Dispose(); throw; }
    }

    private static void CheckFile(string path, string name, long size)
    {
        UpdateTransactionPaths.Canonical(path);
        if (Path.GetFileName(path) != name || size < 1 || new FileInfo(path).Length != size)
            throw new InvalidDataException("Exact material filename or size mismatch.");
    }
}
