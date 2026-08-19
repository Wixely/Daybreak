[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    dotnet restore Daybreak.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build Daybreak.slnx --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
finally {
    Pop-Location
}
