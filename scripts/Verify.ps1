[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    # Upstream source under vendor/ retains its own formatting conventions.
    dotnet format Daybreak.slnx --verify-no-changes --no-restore --include src tests
    if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }
    & "$PSScriptRoot/Test.ps1" -Configuration Release
    & "$PSScriptRoot/Publish.ps1" -Runtime linux-x64
    & "$PSScriptRoot/Publish.ps1" -Runtime linux-arm64
    & "$PSScriptRoot/Assets.ps1"
    & "$PSScriptRoot/Dependencies.ps1"
}
finally {
    Pop-Location
}
