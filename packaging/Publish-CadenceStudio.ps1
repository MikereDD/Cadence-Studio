[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('FrameworkDependent','SelfContained')]
    [string]$Deployment = 'SelfContained',

    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',

    [string]$OutputRoot,
    [switch]$NoRestore,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$project = Join-Path $root 'src\CadenceStudio.App\CadenceStudio.App.csproj'
if (-not $OutputRoot) { $OutputRoot = Join-Path $root 'artifacts\publish' }

$selfContained = $Deployment -eq 'SelfContained'
$deploymentSlug = if ($selfContained) { 'self-contained' } else { 'framework-dependent' }
$output = Join-Path $OutputRoot "$version\$Runtime-$deploymentSlug"

if ($Clean -and (Test-Path -LiteralPath $output)) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

$args = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $selfContained.ToString().ToLowerInvariant(),
    '-o', $output,
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:ContinuousIntegrationBuild=true'
)
if ($NoRestore) { $args += '--no-restore' }

Write-Host "Publishing Cadence Studio $version" -ForegroundColor Cyan
Write-Host "Runtime: $Runtime | Deployment: $Deployment" -ForegroundColor DarkGray
& dotnet @args | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Publish the separate updater with the same architecture and deployment model.
$updaterProject = Join-Path $root 'updater\CadenceStudio.Updater\CadenceStudio.Updater.csproj'
$updaterArgs = @('publish', $updaterProject, '-c', $Configuration, '-r', $Runtime,
    '--self-contained', $selfContained.ToString().ToLowerInvariant(), '-o', (Join-Path $output 'updater'),
    '-p:PublishSingleFile=false', '-p:DebugType=None', '-p:DebugSymbols=false')
if ($NoRestore) { $updaterArgs += '--no-restore' }
& dotnet @updaterArgs | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }
Copy-Item (Join-Path $root 'README.md') $output -Force
Copy-Item (Join-Path $root 'RELEASE-NOTES.md') $output -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $output -Force
Copy-Item (Join-Path $root 'VERSION') $output -Force

$manifest = [ordered]@{
    product = 'Cadence Studio'
    version = $version
    runtime = $Runtime
    deployment = $Deployment
    createdUtc = [DateTime]::UtcNow.ToString('o')
    executable = 'CadenceStudio.exe'
    userData = '%LOCALAPPDATA%\\CadenceStudio'
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8

Write-Host "Publish complete: $output" -ForegroundColor Green
$output
