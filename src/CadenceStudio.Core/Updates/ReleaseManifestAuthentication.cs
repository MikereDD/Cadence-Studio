using System.Security.Cryptography;

namespace CadenceStudio.Core.Updates;

public sealed record ReleaseAuthorization(
    string KeyId,
    string Algorithm,
    string PublicKeySha256);

public static class ReleaseManifestAuthentication
{
    public const int MaximumSignatureBytes = 16 * 1024;

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static ReleaseAuthorization VerifyExact(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes)
    {
        if (manifestBytes.IsEmpty)
            throw new InvalidDataException("Signed release manifest is empty.");
        if (signatureBytes.IsEmpty || signatureBytes.Length > MaximumSignatureBytes)
            throw new InvalidDataException("Manifest signature size is invalid.");

        var candidates = ReleaseTrustPolicy.AuthorizingReleaseKeys.ToArray();
        if (candidates.Length == 0)
            throw new InvalidDataException("No local release identity is authorized to sign manifests.");

        foreach (var candidate in candidates)
        {
            ReleaseTrustPolicy.ValidateKeyMaterial(candidate);
            var profile = ReleaseTrustPolicy.GetEcdsaProfile(candidate.Algorithm);
            try
            {
                using var key = ECDsa.Create();
                key.ImportFromPem(candidate.PublicKeyPem);
                if (key.VerifyData(
                        manifestBytes,
                        signatureBytes,
                        profile.HashAlgorithm,
                        DSASignatureFormat.Rfc3279DerSequence))
                {
                    return new ReleaseAuthorization(
                        candidate.KeyId,
                        candidate.Algorithm,
                        candidate.PublicKeySha256);
                }
            }
            catch (CryptographicException)
            {
                // Malformed or incompatible signatures are rejected after all locally
                // authorized release identities have been tried.
            }
        }

        throw new InvalidDataException("Release manifest detached signature is invalid or unauthorized.");
    }

    public static ReleaseAuthorization AuthorizeReleaseKey(string keyId, string algorithm)
    {
        var key = ReleaseTrustPolicy.RequireAuthorizingReleaseKey(keyId, algorithm);
        return new ReleaseAuthorization(key.KeyId, key.Algorithm, key.PublicKeySha256);
    }
}
