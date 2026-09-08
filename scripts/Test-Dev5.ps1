[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & dotnet build CadenceStudio.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    & dotnet run --project tests/CadenceStudio.Updater.Tests/CadenceStudio.Updater.Tests.csproj -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Executable security/integration harness failed.' }
    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace check failed.' }
}
finally { Pop-Location }
