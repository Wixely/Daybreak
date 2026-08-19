[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    & "$PSScriptRoot/Build.ps1" -Configuration $Configuration
    dotnet test Daybreak.slnx --configuration $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    $nagerTests = 'vendor/Nager.Date/src/Nager.Date.UnitTest/Nager.Date.UnitTest.csproj'
    dotnet restore $nagerTests --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Vendored Nager.Date test restore failed.' }
    dotnet test $nagerTests --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Vendored Nager.Date tests failed.' }
}
finally {
    Pop-Location
}
