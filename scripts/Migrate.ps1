[CmdletBinding()]
param(
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ($ConnectionString) {
    $env:ConnectionStrings__Daybreak = $ConnectionString
}

Push-Location $repositoryRoot
try {
    dotnet run --project src/Daybreak/Daybreak.csproj --no-launch-profile -- --migrate-only
    if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }
}
finally {
    Pop-Location
}
