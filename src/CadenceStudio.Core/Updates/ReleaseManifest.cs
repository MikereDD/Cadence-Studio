using System.Text.Json.Serialization;

namespace CadenceStudio.Core.Updates;

public sealed class ReleaseManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("appId")]
    public string AppId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = string.Empty;

    [JsonPropertyName("minimumVersion")]
    public string MinimumVersion { get; set; } = string.Empty;

    [JsonPropertyName("updaterProtocolVersion")]
    public int UpdaterProtocolVersion { get; set; }

    [JsonPropertyName("minimumUpdaterProtocolVersion")]
    public int MinimumUpdaterProtocolVersion { get; set; }

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }

    [JsonPropertyName("mandatoryReason")]
    public string? MandatoryReason { get; set; }

    [JsonPropertyName("releaseNotesUrl")]
    public string ReleaseNotesUrl { get; set; } = string.Empty;

    [JsonPropertyName("changelogUrl")]
    public string ChangelogUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<ReleaseAsset> Assets { get; set; } = [];

    [JsonPropertyName("source")]
    public ReleaseSource Source { get; set; } = new();

    [JsonPropertyName("rollback")]
    public ReleaseRollback Rollback { get; set; } = new();
}

public sealed class ReleaseAsset
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public ReleaseSignature Signature { get; set; } = new();

    [JsonPropertyName("packageId")]
    public string? PackageId { get; set; }

    [JsonPropertyName("signingCertificateSha256")]
    public string? SigningCertificateSha256 { get; set; }

    [JsonPropertyName("authenticodeSignerThumbprint")]
    public string? AuthenticodeSignerThumbprint { get; set; }
}

public sealed class ReleaseSignature
{
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("publicKeySha256")]
    public string PublicKeySha256 { get; set; } = string.Empty;
}

public sealed class ReleaseSource
{
    [JsonPropertyName("repositoryUrl")]
    public string RepositoryUrl { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("commit")]
    public string Commit { get; set; } = string.Empty;
}

public sealed class ReleaseRollback
{
    [JsonPropertyName("supported")]
    public bool Supported { get; set; }

    [JsonPropertyName("retainVersions")]
    public int RetainVersions { get; set; }

    [JsonPropertyName("minimumRollbackVersion")]
    public string? MinimumRollbackVersion { get; set; }
}
