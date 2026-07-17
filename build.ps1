[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Run,
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root 'CadenceStudio.sln'
$appProject = Join-Path $root 'src\CadenceStudio.App\CadenceStudio.App.csproj'
$appOutput = Join-Path $root "src\CadenceStudio.App\bin\$Configuration\net8.0-windows"
$executable = Join-Path $appOutput 'CadenceStudio.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found. Install the .NET 8 SDK and try again.'
}

$installedSdks = @(& dotnet --list-sdks 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to query installed .NET SDKs.`n$($installedSdks -join [Environment]::NewLine)"
}

$net8Sdk = $installedSdks | Where-Object { $_ -match '^8\.0\.\d+\s' } | Select-Object -Last 1
if (-not $net8Sdk) {
    $installedText = if ($installedSdks.Count -gt 0) {
        $installedSdks -join [Environment]::NewLine
    }
    else {
        '(none found)'
    }

    throw "Cadence Studio v1.0 requires a .NET 8 SDK.`nInstalled SDKs:`n$installedText"
}

$sdkVersionOutput = @(& dotnet --version 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "The .NET 8 SDK was found, but SDK resolution failed.`n$($sdkVersionOutput -join [Environment]::NewLine)"
}
$sdkVersion = $sdkVersionOutput | Select-Object -First 1

Write-Host 'Cadence Studio v1.0' -ForegroundColor Cyan
Write-Host "SDK: $sdkVersion" -ForegroundColor DarkGray

if (-not $NoRestore) {
    Write-Host 'Restoring NAudio and TagLibSharp packages...' -ForegroundColor Gray
    & dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
}

Write-Host "Building $Configuration..." -ForegroundColor Gray
& dotnet build $solution -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Build completed, but the expected executable was not found: $executable"
}

Write-Host 'Build succeeded.' -ForegroundColor Green
Write-Host "Executable: $executable" -ForegroundColor Cyan

if ($Run) {
    Write-Host 'Launching Cadence Studio executable...' -ForegroundColor Gray
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Cadence Studio exited with an error.' }
}
