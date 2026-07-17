[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64',
    [ValidateSet('FrameworkDependent','SelfContained')]
    [string]$Deployment = 'SelfContained',
    [switch]$NoRestore
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$publishScript = Join-Path $root 'packaging\Publish-CadenceStudio.ps1'
$publishDir = & $publishScript -Runtime $Runtime -Deployment $Deployment -Configuration Release -NoRestore:$NoRestore -Clean
if ($publishDir -is [array]) { $publishDir = $publishDir[-1] }
$publishDir = [string]$publishDir
if (-not (Test-Path -LiteralPath $publishDir -PathType Container)) {
    throw "Publish directory was not returned correctly: $publishDir"
}
$releaseDir = Join-Path $root "artifacts\release\$version"
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$deploymentSlug = if ($Deployment -eq 'SelfContained') { 'self-contained' } else { 'framework-dependent' }
$zip = Join-Path $releaseDir "CadenceStudio-$version-$Runtime-$deploymentSlug-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
Write-Host "Portable release: $zip" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor DarkGray
