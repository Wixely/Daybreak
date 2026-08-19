[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$userProfile = [Environment]::GetFolderPath('UserProfile')
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $userProfile '.nuget/packages' }
$resolved = @{}

Get-ChildItem -Path $repositoryRoot -Recurse -Filter packages.lock.json | ForEach-Object {
    $lock = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json
    foreach ($framework in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $framework.Value.PSObject.Properties) {
            $version = $package.Value.resolved
            if ($version) {
                $resolved[$package.Name.ToLowerInvariant()] = $version
            }
        }
    }
}

$rows = foreach ($entry in $resolved.GetEnumerator() | Sort-Object Name) {
    $directory = Join-Path $packageRoot "$($entry.Name)/$($entry.Value)"
    $nuspec = Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
    $license = 'Missing'
    if ($nuspec) {
        [xml]$metadata = Get-Content -Raw -LiteralPath $nuspec.FullName
        if ($metadata.package.metadata.license) {
            $license = $metadata.package.metadata.license.InnerText
        }
    }

    [pscustomobject]@{ Package = $entry.Name; Version = $entry.Value; License = $license }
}

$rows | Format-Table -AutoSize
$unapproved = @($rows | Where-Object { $_.License -notin @('MIT', 'Apache-2.0') })
if ($unapproved.Count -gt 0) {
    Write-Warning "$($unapproved.Count) package(s) require explicit license review."
    exit 1
}

dotnet list "$repositoryRoot/Daybreak.slnx" package --vulnerable --include-transitive
if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability audit failed.' }

dotnet list "$repositoryRoot/Daybreak.slnx" package --deprecated --include-transitive
if ($LASTEXITCODE -ne 0) { throw 'NuGet deprecation audit failed.' }
