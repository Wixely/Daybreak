[CmdletBinding()]
param(
    [ValidateSet('Build', 'Start', 'Stop', 'Logs')]
    [string]$Command = 'Build'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is required for this command.'
}

Push-Location $repositoryRoot
try {
    switch ($Command) {
        'Build' { docker compose build }
        'Start' { docker compose up --build --detach }
        'Stop' { docker compose down }
        'Logs' { docker compose logs --follow daybreak }
    }

    if ($LASTEXITCODE -ne 0) { throw "Docker command '$Command' failed." }
}
finally {
    Pop-Location
}
