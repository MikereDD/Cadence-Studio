[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipInstaller
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$releaseDir = Join-Path $root "artifacts\release\$version"

$expected = @(
    "CadenceStudio-$version-$Runtime-self-contained-portable.zip",
    "CadenceStudio-$version-$Runtime-framework-dependent-portable.zip"
)
if (-not $SkipInstaller) {
    $expected += "CadenceStudio-$version-$Runtime-Setup.exe"
}

foreach ($name in $expected) {
    $artifact = Join-Path $releaseDir $name
    $checksum = "$artifact.sha256"
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) { throw "Missing release artifact: $artifact" }
    if (-not (Test-Path -LiteralPath $checksum -PathType Leaf)) { throw "Missing checksum: $checksum" }

    $expectedHash = ((Get-Content -LiteralPath $checksum -Raw).Trim() -split '\s+')[0]
    $actualHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
    if ($expectedHash -ne $actualHash) { throw "Checksum mismatch: $name" }
    Write-Host "Verified: $name" -ForegroundColor DarkGreen
}
Write-Host "Release artifact validation passed: $releaseDir" -ForegroundColor Green
