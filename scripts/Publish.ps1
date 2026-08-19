[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'linux-arm64')]
    [string]$Runtime = 'linux-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repositoryRoot "artifacts/publish/$Runtime"
Push-Location $repositoryRoot
try {
    dotnet restore src/Daybreak/Daybreak.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet publish src/Daybreak/Daybreak.csproj --configuration Release --no-restore `
        --runtime $Runtime --self-contained:$false --output $output /p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
finally {
    Pop-Location
}
