[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $running = Get-Process CadenceStudio -ErrorAction SilentlyContinue
    if ($running) {
        throw "CadenceStudio is running (PID(s): $($running.Id -join ', ')). Exit the tray app before validating dev.6."
    }
    & dotnet build CadenceStudio.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    & dotnet run --project tests/CadenceStudio.ManifestSecurity.Tests/CadenceStudio.ManifestSecurity.Tests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Manifest security/replay harness failed.' }
    & dotnet run --project tests/CadenceStudio.Updater.Tests/CadenceStudio.Updater.Tests.csproj -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Executable security/integration harness failed.' }
    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace check failed.' }
}
finally { Pop-Location }
