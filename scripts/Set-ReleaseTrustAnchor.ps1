# Requires PowerShell 7. Writes public material only. Generate/store the private key separately.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublicKeyPath,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{1,128}$')][string]$KeyId
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7 (pwsh).' }
if ($KeyId -match 'test|fixture') { throw 'A test identity cannot be provisioned for production.' }
$pem = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $PublicKeyPath).Path)
if ($pem.Contains('PRIVATE KEY')) { throw 'Pass only a public PEM; never a private key.' }
$key = [Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem($pem)
    if ($key.KeySize -ne 256) { throw 'This implementation requires ECDSA P-256 / SHA-256.' }
    $canonical = $key.ExportSubjectPublicKeyInfoPem()
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($key.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
    $repo = Split-Path -Parent $PSScriptRoot
    $fixture = [IO.File]::ReadAllText((Join-Path $repo 'tests/SigningFixture.Core/ReleaseTrustAnchor.cs'))
    if ($fixture.Replace("`r", '').Contains($canonical.Replace("`r", ''))) { throw 'The fixture public key cannot be used in production.' }
    $source = @"
namespace CadenceStudio.Core.Updates;

// Reviewed local trust anchor. Private signing material must remain outside this repository.
internal static class ReleaseTrustAnchor
{
    internal const string KeyId = "$KeyId";
    internal const string Algorithm = "ecdsa-sha256";
    internal const string PublicKeyPem = """
$canonical
""";
}
"@
    [IO.File]::WriteAllText((Join-Path $repo 'src/CadenceStudio.Core/Updates/ReleaseTrustAnchor.cs'), $source)
    Write-Host "Pinned key ID: $KeyId"
    Write-Host "Canonical DER SPKI SHA-256: $fingerprint"
    Write-Host 'Review this public-only source change, then rebuild BOTH application and updater.'
}
finally { $key.Dispose() }
