[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RuntimeDirectory,

    [Parameter(Mandatory)]
    [string]$ManifestPath,

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$OutputDirectory,

    [string]$PackageName,

    [switch]$Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ToolBox.PackageTools.psm1') -Force

$runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\', '/')
$manifestSource = [System.IO.Path]::GetFullPath($ManifestPath)
$OutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $PSScriptRoot '..\artifacts'
} else {
    $OutputDirectory
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxPackageBuild\$([Guid]::NewGuid().ToString('N'))"

try {
    if (-not (Test-Path -LiteralPath $runtimeRoot -PathType Container)) {
        throw "The plugin runtime directory is missing: '$runtimeRoot'."
    }

    if (-not (Test-Path -LiteralPath $manifestSource -PathType Leaf)) {
        throw "The plugin manifest is missing: '$manifestSource'."
    }

    $manifest = Get-Content -LiteralPath $manifestSource -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.id) -or [string]::IsNullOrWhiteSpace($manifest.version)) {
        throw 'The manifest must contain non-empty id and version fields.'
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $manifest.version = $Version
    }
    $packageVersion = [string]$manifest.version
    if ($packageVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "Manifest version '$packageVersion' is not a supported semantic version."
    }

    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        $PackageName = "$($manifest.id)-$packageVersion.tpk"
    }
    if (-not $PackageName.EndsWith('.tpk', [System.StringComparison]::OrdinalIgnoreCase)) {
        $PackageName += '.tpk'
    }

    $outputPath = Join-Path $outputRoot $PackageName
    if ((Test-Path -LiteralPath $outputPath) -and -not $Overwrite) {
        throw "The output package already exists: '$outputPath'. Use -Overwrite to replace it explicitly."
    }

    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'runtime') -Force | Out-Null
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stagingRoot 'manifest.json') -Encoding utf8

    $runtimeFiles = @(Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse | Where-Object {
        $_.FullName -ine (Join-Path $runtimeRoot 'manifest.json') -and $_.Name -notlike 'ToolBox.PluginSdk.*'
    })
    if ($runtimeFiles.Count -eq 0) {
        throw "The plugin runtime directory contains no packageable files: '$runtimeRoot'."
    }

    foreach ($runtimeFile in $runtimeFiles) {
        $relativePath = $runtimeFile.FullName.Substring($runtimeRoot.Length + 1).Replace('\', '/')
        $destination = Join-Path $stagingRoot (Join-Path 'runtime' ($relativePath -replace '/', '\'))
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $runtimeFile.FullName -Destination $destination
    }

    $hashes = @(
        Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | ForEach-Object {
            $relativePath = $_.FullName.Substring($stagingRoot.Length + 1).Replace('\', '/')
            [PSCustomObject]@{
                path = $relativePath
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )

    $packageMetadata = [PSCustomObject]@{
        packageFormatVersion = 1
        pluginId = [string]$manifest.id
        pluginVersion = $packageVersion
        automaticRollbackSupported = $true
        files = $hashes
    }
    $packageMetadata | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath (Join-Path $stagingRoot 'package.json') -Encoding utf8

    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Force
    }
    New-DeterministicZipArchive -SourceDirectory $stagingRoot -DestinationPath $outputPath
    Write-Output $outputPath
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
