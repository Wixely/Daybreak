[CmdletBinding()]
param(
    [string]$AdminPassword = 'daybreak-dev',
    [string]$Url = 'http://localhost:5180'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$env:DAYBREAK_ADMIN_PASSWORD = $AdminPassword
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Push-Location $repositoryRoot
try {
    dotnet run --project src/Daybreak/Daybreak.csproj --launch-profile http --urls $Url
}
finally {
    Pop-Location
}
