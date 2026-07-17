[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$InnoCompilerPath,
    [switch]$NoRestore
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$publishScript = Join-Path $root 'packaging\Publish-CadenceStudio.ps1'
$publishDir = & $publishScript -Runtime $Runtime -Deployment SelfContained -Configuration Release -NoRestore:$NoRestore -Clean
if ($publishDir -is [array]) { $publishDir = $publishDir[-1] }
$publishDir = [string]$publishDir
if (-not (Test-Path -LiteralPath $publishDir -PathType Container)) {
    throw "Publish directory was not returned correctly: $publishDir"
}

if ($InnoCompilerPath) {
    if (-not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
        throw "The supplied Inno Setup compiler path does not exist: $InnoCompilerPath"
    }
    $iscc = (Resolve-Path -LiteralPath $InnoCompilerPath).Path
}
else {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $isccCandidates = @(
        if ($command) { $command.Source }
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
    $iscc = $isccCandidates | Select-Object -First 1
}

if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it with 'winget install --id JRSoftware.InnoSetup -e', or pass -InnoCompilerPath."
}
Write-Host "Inno Setup compiler: $iscc" -ForegroundColor DarkGray

$releaseDir = Join-Path $root "artifacts\release\$version"
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$iss = Join-Path $root 'installer\CadenceStudio.iss'
& $iscc "/DMyAppVersion=$version" "/DPublishDir=$publishDir" "/DOutputDir=$releaseDir" "/DArchitecture=$Runtime" $iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$installer = Get-ChildItem -LiteralPath $releaseDir -Filter "CadenceStudio-$version-*-Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) { throw 'Installer completed, but the setup executable was not found.' }
$hash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash
"$hash  $($installer.Name)" | Set-Content -LiteralPath "$($installer.FullName).sha256" -Encoding ascii
Write-Host "Installer: $($installer.FullName)" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor DarkGray
