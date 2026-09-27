using System.Security.Cryptography;

namespace CadenceStudio.Core.Updates;

internal enum ReleaseTrustRole
{
    Release,
    Recovery
}

internal enum ReleaseKeyLifecycle
{
    Pending,
    Active,
    Retiring,
    Retired,
    Revoked
}

internal sealed record ReleaseTrustKey(
    string KeyId,
    ReleaseTrustRole Role,
    ReleaseKeyLifecycle Lifecycle,
    string Algorithm,
    int KeySize,
    string PublicKeySha256,
    string PublicKeyPem);

internal static class ReleaseTrustPolicy
{
    internal static IEnumerable<ReleaseTrustKey> AuthorizingReleaseKeys =>
        ReleaseTrustAnchor.Keys.Where(key =>
            key.Role == ReleaseTrustRole.Release &&
            key.Lifecycle is ReleaseKeyLifecycle.Active or ReleaseKeyLifecycle.Retiring);

    internal static ReleaseTrustKey RequireAuthorizingReleaseKey(string keyId, string algorithm)
    {
        var matches = ReleaseTrustAnchor.Keys
            .Where(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal) &&
                          string.Equals(key.Algorithm, algorithm, StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        if (matches.Length == 0)
            throw new InvalidDataException("Unapproved signing identity.");
        if (matches.Length != 1)
            throw new InvalidDataException("Ambiguous local signing identity.");

        var key = matches[0];
        if (key.Role != ReleaseTrustRole.Release ||
            key.Lifecycle is not (ReleaseKeyLifecycle.Active or ReleaseKeyLifecycle.Retiring))
            throw new InvalidDataException("Signing identity is not authorized for new releases.");

        ValidateKeyMaterial(key);
        return key;
    }

    internal static string ValidateKeyMaterial(ReleaseTrustKey key)
    {
        var profile = GetEcdsaProfile(key.Algorithm);
        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(key.PublicKeyPem);
        if (publicKey.KeySize != profile.KeySize || key.KeySize != profile.KeySize)
            throw new InvalidDataException("Pinned release key does not match its declared cryptographic profile.");

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        if (!string.Equals(fingerprint, key.PublicKeySha256, StringComparison.Ordinal))
            throw new InvalidDataException("Pinned release-key fingerprint does not match its public key.");

        return fingerprint;
    }

    internal static (HashAlgorithmName HashAlgorithm, int KeySize) GetEcdsaProfile(string algorithm) =>
        algorithm switch
        {
            "ecdsa-sha256" => (HashAlgorithmName.SHA256, 256),
            "typezero-ecdsa-p384-sha384-v1" => (HashAlgorithmName.SHA384, 384),
            _ => throw new InvalidDataException("Unsupported local release-signing profile.")
        };
}
