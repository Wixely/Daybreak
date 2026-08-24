[CmdletBinding()]
param(
    [string[]]$Runtimes = @('linux-x64', 'linux-arm64')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repositoryRoot 'src/Daybreak'
$remoteAssetPattern = '(?i)<(?:script|img|source|audio|video|iframe)\b[^>]*\b(?:src|srcset)\s*=\s*["'']\s*(?:https?:)?//|<link\b[^>]*\bhref\s*=\s*["'']\s*(?:https?:)?//|@import\s+(?:url\()?\s*["'']?\s*(?:https?:)?//|url\(\s*["'']?\s*(?:https?:)?//'

$browserSourceFiles = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.razor', '.html', '.css', '.js') }
$remoteReferences = $browserSourceFiles | Select-String -Pattern $remoteAssetPattern
if ($remoteReferences) {
    $details = $remoteReferences | ForEach-Object { "{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim() }
    throw "Remote CSS or JavaScript asset references are not allowed. Vendor and serve them locally:`n$($details -join "`n")"
}

$requiredAssets = @(
    'app.css',
    'favicon.svg',
    '_framework/blazor.web.js',
    '_framework/blazor.server.js'
)

foreach ($runtime in $Runtimes) {
    $webRoot = Join-Path $repositoryRoot "artifacts/publish/$runtime/wwwroot"
    foreach ($relativePath in $requiredAssets) {
        $assetPath = Join-Path $webRoot $relativePath
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            throw "Required browser asset is missing from the $runtime publish: $relativePath"
        }

        if ((Get-Item -LiteralPath $assetPath).Length -eq 0) {
            throw "Required browser asset is empty in the $runtime publish: $relativePath"
        }
    }
}

Write-Host "Browser assets are local and complete for: $($Runtimes -join ', ')."
