using System.Text.RegularExpressions;

namespace CadenceStudio.Core.Updates;

public static class ReleaseManifestValidator
{
    private static readonly Regex Sha256Pattern = new(
        "^[A-Fa-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CommitPattern = new(
        "^(?:[A-Fa-f0-9]{40}|[A-Fa-f0-9]{64})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex KeyIdPattern = new(
        "^[A-Za-z0-9._-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AuthenticodeThumbprintPattern = new(
        "^[A-Fa-f0-9]{40,64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SafeRelativePathPattern = new(
        "^[A-Za-z0-9._/-]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedSignatureAlgorithms =
        new(StringComparer.Ordinal)
        {
            "rsa-sha256",
            "ecdsa-sha256",
            "ed25519"
        };

    public static bool TryValidateForCadence(
        ReleaseManifest manifest,
        string expectedArchitecture,
        out ReleaseVersion? candidateVersion,
        out ReleaseVersion? minimumVersion,
        out string error)
    {
        candidateVersion = null;
        minimumVersion = null;
        error = string.Empty;

        if (manifest.SchemaVersion != ProductInfo.ReleaseManifestSchemaVersion)
        {
            return Fail(
                $"Unsupported manifest schema {manifest.SchemaVersion}; expected {ProductInfo.ReleaseManifestSchemaVersion}.",
                out error);
        }

        if (!string.Equals(manifest.AppId, ProductInfo.AppId, StringComparison.Ordinal))
        {
            return Fail("Manifest appId does not identify Cadence Studio.", out error);
        }

        if (!string.Equals(manifest.DisplayName, ProductInfo.Name, StringComparison.Ordinal))
        {
            return Fail("Manifest displayName does not match Cadence Studio.", out error);
        }

        if (!string.Equals(manifest.Platform, "windows", StringComparison.Ordinal))
        {
            return Fail("Manifest platform is not windows.", out error);
        }

        if (!string.Equals(manifest.Architecture, expectedArchitecture, StringComparison.Ordinal))
        {
            return Fail(
                $"Manifest architecture {manifest.Architecture} does not match {expectedArchitecture}.",
                out error);
        }

        if (!string.Equals(manifest.Channel, ProductInfo.UpdateChannel, StringComparison.Ordinal))
        {
            return Fail(
                $"Manifest channel {manifest.Channel} does not match {ProductInfo.UpdateChannel}.",
                out error);
        }

        if (!ReleaseVersion.TryParse(manifest.Version, out candidateVersion))
        {
            return Fail("Manifest version is malformed.", out error);
        }

        if (!ReleaseVersion.TryParse(manifest.MinimumVersion, out minimumVersion))
        {
            return Fail("Manifest minimumVersion is malformed.", out error);
        }

        if (minimumVersion!.CompareTo(candidateVersion) > 0)
        {
            return Fail("Manifest minimumVersion is newer than the target release.", out error);
        }

        if (!DateTimeOffset.TryParse(manifest.PublishedAt, out _))
        {
            return Fail("Manifest publishedAt is not a valid date-time.", out error);
        }

        if (manifest.UpdaterProtocolVersion < 1 ||
            manifest.MinimumUpdaterProtocolVersion < 1 ||
            manifest.MinimumUpdaterProtocolVersion > manifest.UpdaterProtocolVersion)
        {
            return Fail("Manifest updater protocol fields are inconsistent.", out error);
        }

        if (manifest.Mandatory && string.IsNullOrWhiteSpace(manifest.MandatoryReason))
        {
            return Fail("Mandatory manifest is missing mandatoryReason.", out error);
        }

        if (!IsHttpsOrSafeRelativePath(manifest.ReleaseNotesUrl))
        {
            return Fail("Manifest releaseNotesUrl is not an approved HTTPS URL or safe relative path.", out error);
        }

        if (!IsHttpsOrSafeRelativePath(manifest.ChangelogUrl))
        {
            return Fail("Manifest changelogUrl is not an approved HTTPS URL or safe relative path.", out error);
        }

        if (manifest.Assets.Count == 0)
        {
            return Fail("Manifest contains no release assets.", out error);
        }

        foreach (var asset in manifest.Assets)
        {
            if (!TryValidateAsset(asset, out error))
            {
                return false;
            }
        }

        var expectedAssetName =
            $"{ProductInfo.ReleaseAssetProductName}-v{manifest.Version}-{expectedArchitecture}.zip";

        if (manifest.Assets.Count(asset =>
                string.Equals(asset.FileName, expectedAssetName, StringComparison.Ordinal)) != 1)
        {
            return Fail(
                $"Manifest must contain exactly one updater payload named {expectedAssetName}.",
                out error);
        }

        if (!string.Equals(
                manifest.Source.RepositoryUrl.TrimEnd('/'),
                ProductInfo.SourceRepositoryUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Manifest source repository does not match Cadence Studio.", out error);
        }

        if (!string.Equals(manifest.Source.Tag, $"v{manifest.Version}", StringComparison.Ordinal))
        {
            return Fail("Manifest source tag does not equal v + version.", out error);
        }

        if (!CommitPattern.IsMatch(manifest.Source.Commit))
        {
            return Fail("Manifest source commit is not a full SHA-1 or SHA-256 identifier.", out error);
        }

        if (!manifest.Rollback.Supported ||
            manifest.Rollback.RetainVersions != 1 ||
            string.IsNullOrWhiteSpace(manifest.Rollback.MinimumRollbackVersion) ||
            !ReleaseVersion.TryParse(manifest.Rollback.MinimumRollbackVersion, out var rollbackMinimum))
        {
            return Fail("Windows rollback policy must retain exactly one known-good version.", out error);
        }

        if (rollbackMinimum!.CompareTo(candidateVersion) > 0)
        {
            return Fail("minimumRollbackVersion is newer than the target release.", out error);
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateAsset(ReleaseAsset asset, out string error)
    {
        if (string.IsNullOrWhiteSpace(asset.FileName) ||
            asset.FileName.Contains('/') ||
            asset.FileName.Contains('\\'))
        {
            return Fail("Release asset fileName is invalid.", out error);
        }

        if (!IsHttps(asset.DownloadUrl))
        {
            return Fail($"Release asset {asset.FileName} does not use HTTPS.", out error);
        }

        if (asset.Size < 1 || !Sha256Pattern.IsMatch(asset.Sha256))
        {
            return Fail($"Release asset {asset.FileName} has invalid size or SHA-256 metadata.", out error);
        }

        if (!string.IsNullOrWhiteSpace(asset.AuthenticodeSignerThumbprint) &&
            !AuthenticodeThumbprintPattern.IsMatch(asset.AuthenticodeSignerThumbprint))
        {
            return Fail($"Release asset {asset.FileName} has an invalid Authenticode thumbprint.", out error);
        }

        if (!string.IsNullOrWhiteSpace(asset.PackageId) ||
            !string.IsNullOrWhiteSpace(asset.SigningCertificateSha256))
        {
            return Fail($"Windows release asset {asset.FileName} contains Android-only identity fields.", out error);
        }

        var signature = asset.Signature;
        if (!AllowedSignatureAlgorithms.Contains(signature.Algorithm) ||
            !string.Equals(signature.FileName, $"{asset.FileName}.sig", StringComparison.Ordinal) ||
            !IsHttps(signature.DownloadUrl) ||
            signature.Size < 1 ||
            !Sha256Pattern.IsMatch(signature.Sha256) ||
            !KeyIdPattern.IsMatch(signature.KeyId) ||
            !Sha256Pattern.IsMatch(signature.PublicKeySha256))
        {
            return Fail($"Release asset {asset.FileName} has invalid detached-signature metadata.", out error);
        }

        error = string.Empty;
        return true;
    }

    private static bool IsHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(uri.Host);

    private static bool IsHttpsOrSafeRelativePath(string value)
    {
        if (IsHttps(value))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            !SafeRelativePathPattern.IsMatch(value))
        {
            return false;
        }

        return value.Split('/').All(segment => segment != "..");
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
