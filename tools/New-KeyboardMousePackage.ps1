[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'),

    [switch]$Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ToolBox.PackageTools.psm1') -Force

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceDirectory = Join-Path $repositoryRoot "spikes\KeyboardTest\bin\$Configuration\net8.0"
$sourceManifestPath = Join-Path $repositoryRoot 'spikes\KeyboardTest\manifest.json'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$outputPath = Join-Path $outputRoot "KeyboardMouse-$Version.tpk"
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxPackageBuild\$([Guid]::NewGuid().ToString('N'))"

try {
    $requiredFiles = @(
        (Join-Path $sourceDirectory 'KeyboardTest.dll'),
        (Join-Path $sourceDirectory 'KeyboardTest.deps.json')
    )

    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "The Keyboard & Mouse Test build output is missing: '$requiredFile'. Run a Release build first."
        }
    }

    if (-not (Test-Path -LiteralPath $sourceManifestPath -PathType Leaf)) {
        throw "The Keyboard & Mouse Test manifest is missing: '$sourceManifestPath'."
    }

    $manifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
    if ($manifest.id -ne 'com.toolbox.keyboard-test') {
        throw "Unexpected Keyboard & Mouse Test plugin id '$($manifest.id)'."
    }

    $manifest.version = $Version

    if ((Test-Path -LiteralPath $outputPath) -and -not $Overwrite) {
        throw "The output package already exists: '$outputPath'. Use -Overwrite to replace it explicitly."
    }

    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'runtime') -Force | Out-Null
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stagingRoot 'manifest.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'KeyboardTest.dll') -Destination (Join-Path $stagingRoot 'runtime\KeyboardTest.dll')
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'KeyboardTest.deps.json') -Destination (Join-Path $stagingRoot 'runtime\KeyboardTest.deps.json')

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
        pluginId = 'com.toolbox.keyboard-test'
        pluginVersion = $Version
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
