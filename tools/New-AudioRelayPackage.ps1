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

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetFramework = 'net8.0-windows10.0.19041.0'
$sourceDirectory = Join-Path $repositoryRoot "spikes\AudioRelay\bin\$Configuration\$targetFramework"
$sourceManifestPath = Join-Path $repositoryRoot 'spikes\AudioRelay\manifest.json'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$outputPath = Join-Path $outputRoot "PhoneAudioRelay-$Version.tpk"
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxPackageBuild\$([Guid]::NewGuid().ToString('N'))"

try {
    $requiredFiles = @(
        (Join-Path $sourceDirectory 'AudioRelay.dll'),
        (Join-Path $sourceDirectory 'AudioRelay.deps.json'),
        (Join-Path $sourceDirectory 'Microsoft.Windows.SDK.NET.dll'),
        (Join-Path $sourceDirectory 'WinRT.Runtime.dll')
    )

    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "The Phone Audio Relay build output is missing: '$requiredFile'. Run a Release build first."
        }
    }

    if (-not (Test-Path -LiteralPath $sourceManifestPath -PathType Leaf)) {
        throw "The Phone Audio Relay manifest is missing: '$sourceManifestPath'."
    }

    $manifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
    if ($manifest.id -ne 'com.toolbox.audio-relay') {
        throw "Unexpected Phone Audio Relay plugin id '$($manifest.id)'."
    }

    $manifest.version = $Version

    if ((Test-Path -LiteralPath $outputPath) -and -not $Overwrite) {
        throw "The output package already exists: '$outputPath'. Use -Overwrite to replace it explicitly."
    }

    New-Item -ItemType Directory -Path (Join-Path $stagingRoot 'runtime') -Force | Out-Null
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stagingRoot 'manifest.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'AudioRelay.dll') -Destination (Join-Path $stagingRoot 'runtime\AudioRelay.dll')
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'AudioRelay.deps.json') -Destination (Join-Path $stagingRoot 'runtime\AudioRelay.deps.json')
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'Microsoft.Windows.SDK.NET.dll') -Destination (Join-Path $stagingRoot 'runtime\Microsoft.Windows.SDK.NET.dll')
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'WinRT.Runtime.dll') -Destination (Join-Path $stagingRoot 'runtime\WinRT.Runtime.dll')

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
        pluginId = 'com.toolbox.audio-relay'
        pluginVersion = $Version
        automaticRollbackSupported = $true
        files = $hashes
    }
    $packageMetadata | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath (Join-Path $stagingRoot 'package.json') -Encoding utf8

    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stagingRoot,
        $outputPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    Write-Output $outputPath
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
