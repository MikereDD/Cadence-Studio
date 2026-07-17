[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$InnoCompilerPath,
    [switch]$SkipInstaller,
    [switch]$NoRestore
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
& (Join-Path $root 'packaging\New-PortableRelease.ps1') -Runtime $Runtime -Deployment SelfContained -NoRestore:$NoRestore
& (Join-Path $root 'packaging\New-PortableRelease.ps1') -Runtime $Runtime -Deployment FrameworkDependent -NoRestore
if (-not $SkipInstaller) {
    & (Join-Path $root 'packaging\New-Installer.ps1') -Runtime $Runtime -InnoCompilerPath $InnoCompilerPath -NoRestore
}
& (Join-Path $root 'packaging\Test-ReleaseArtifacts.ps1') -Runtime $Runtime -SkipInstaller:$SkipInstaller
Write-Host 'Release packaging complete.' -ForegroundColor Green
