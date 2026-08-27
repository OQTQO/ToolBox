[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\release-validation')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'ToolBox.sln'
$hostProjectPath = Join-Path $repositoryRoot 'src\ToolBox.Host\ToolBox.Host.csproj'
$keyboardPackageScript = Join-Path $PSScriptRoot 'New-KeyboardMousePackage.ps1'
$audioPackageScript = Join-Path $PSScriptRoot 'New-AudioRelayPackage.ps1'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxReleaseValidation\$([Guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $stagingRoot 'publish'
$assetDirectory = Join-Path $stagingRoot 'assets'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$ArgumentList
    )

    Write-Host "> $FilePath $($ArgumentList -join ' ')" -ForegroundColor DarkGray
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Get-ProjectVersion {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw "The Host project does not declare a Version property: '$ProjectPath'."
    }

    return $versionNode.InnerText.Trim()
}

function Assert-ExactSet {
    param(
        [Parameter(Mandatory)][string[]]$Actual,
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string]$Label
    )

    $difference = @(
        Compare-Object `
            -ReferenceObject ($Expected | Sort-Object) `
            -DifferenceObject ($Actual | Sort-Object) `
            -CaseSensitive)
    if ($difference.Count -ne 0) {
        $description = $difference | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
        throw "$Label does not match the expected set: $($description -join '; ')."
    }
}

function Remove-DirectoryWithRetry {
    param([Parameter(Mandatory)][string]$Path)

    for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $Path); $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
        catch [System.IO.IOException] {
            [GC]::Collect()
            [GC]::WaitForPendingFinalizers()
            Start-Sleep -Milliseconds 50
        }
        catch [System.UnauthorizedAccessException] {
            [GC]::Collect()
            [GC]::WaitForPendingFinalizers()
            Start-Sleep -Milliseconds 50
        }
    }

    if (Test-Path -LiteralPath $Path) {
        throw "Release validation staging directory could not be removed: '$Path'."
    }
}

function Read-ZipJson {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Package entry '$EntryName' is missing."
    }

    $stream = $entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true, 1024, $true)
        try {
            return ($reader.ReadToEnd() | ConvertFrom-Json)
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ZipEntrySha256 {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $stream = $Entry.Open()
    try {
        $hash = [System.Security.Cryptography.SHA256]::HashData($stream)
        return [Convert]::ToHexString($hash).ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-PluginPackage {
    param(
        [Parameter(Mandatory)][string]$PackagePath,
        [Parameter(Mandatory)][string]$ExpectedPluginId,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string[]]$ExpectedEntries
    )

    if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
        throw "Expected plugin package is missing: '$PackagePath'."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        $entryNames = @($fileEntries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $caseInsensitiveNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entryName in $entryNames) {
            if (-not $caseInsensitiveNames.Add($entryName)) {
                throw "Package '$PackagePath' contains a duplicate or case-colliding entry '$entryName'."
            }
        }

        Assert-ExactSet -Actual $entryNames -Expected $ExpectedEntries -Label "Entries in '$PackagePath'"

        if ($entryNames | Where-Object { [System.IO.Path]::GetFileName($_) -ieq 'ToolBox.PluginSdk.dll' }) {
            throw "Package '$PackagePath' contains a private ToolBox.PluginSdk.dll copy."
        }

        $manifest = Read-ZipJson -Archive $archive -EntryName 'manifest.json'
        $metadata = Read-ZipJson -Archive $archive -EntryName 'package.json'
        if ($manifest.id -cne $ExpectedPluginId -or $metadata.pluginId -cne $ExpectedPluginId) {
            throw "Package '$PackagePath' does not match plugin id '$ExpectedPluginId'."
        }

        if ($manifest.version -cne $ExpectedVersion -or $metadata.pluginVersion -cne $ExpectedVersion) {
            throw "Package '$PackagePath' does not consistently use version '$ExpectedVersion'."
        }

        $hashedEntries = @($metadata.files | ForEach-Object { [string]$_.path })
        $payloadEntries = @($entryNames | Where-Object { $_ -cne 'package.json' })
        Assert-ExactSet -Actual $hashedEntries -Expected $payloadEntries -Label "Hash inventory in '$PackagePath'"

        foreach ($fileHash in @($metadata.files)) {
            $entry = $archive.GetEntry([string]$fileHash.path)
            if ($null -eq $entry) {
                throw "Hash inventory entry '$($fileHash.path)' is missing from '$PackagePath'."
            }

            $actualHash = Get-ZipEntrySha256 -Entry $entry
            if ($actualHash -cne ([string]$fileHash.sha256).ToLowerInvariant()) {
                throw "Hash mismatch for '$($fileHash.path)' in '$PackagePath'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Write-AndValidateChecksums {
    param(
        [Parameter(Mandatory)][string[]]$ArtifactPaths,
        [Parameter(Mandatory)][string]$ChecksumPath
    )

    $checksumLines = foreach ($artifactPath in $ArtifactPaths) {
        $fileHash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath
        "{0}  {1}" -f $fileHash.Hash.ToLowerInvariant(), (Split-Path -Leaf $fileHash.Path)
    }
    $checksumLines | Set-Content -LiteralPath $ChecksumPath -Encoding ascii

    $validatedNames = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(Get-Content -LiteralPath $ChecksumPath)) {
        if ($line -cnotmatch '^([a-f0-9]{64})  ([^\\/]+)$') {
            throw "Checksum manifest contains an invalid line: '$line'."
        }

        $expectedHash = $Matches[1]
        $fileName = $Matches[2]
        $artifactPath = Join-Path (Split-Path -Parent $ChecksumPath) $fileName
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "Checksum manifest references a missing artifact '$fileName'."
        }

        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath).Hash.ToLowerInvariant()
        if ($actualHash -cne $expectedHash) {
            throw "Checksum validation failed for '$fileName'."
        }

        $validatedNames.Add($fileName)
    }

    Assert-ExactSet `
        -Actual $validatedNames.ToArray() `
        -Expected @($ArtifactPaths | ForEach-Object { Split-Path -Leaf $_ }) `
        -Label 'Checksum manifest file names'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -ProjectPath $hostProjectPath
}

$releaseTag = "v$Version"
$hostAssetName = "ToolBox-Host-$releaseTag-win-x64.exe"
$keyboardAssetName = "KeyboardMouse-$Version.tpk"
$audioAssetName = "PhoneAudioRelay-$Version.tpk"
$checksumAssetName = "SHA256SUMS-$releaseTag.txt"

try {
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

    Push-Location $repositoryRoot
    try {
        Invoke-CheckedCommand dotnet @('clean', $solutionPath, '--configuration', $Configuration, '--verbosity', 'minimal')
        Invoke-CheckedCommand dotnet @('restore', $solutionPath)
        Invoke-CheckedCommand dotnet @(
            'build', $solutionPath,
            '--configuration', $Configuration,
            '--no-restore',
            '-warnaserror',
            "-p:Version=$Version",
            '-p:ContinuousIntegrationBuild=true')
        Invoke-CheckedCommand dotnet @(
            'test', $solutionPath,
            '--configuration', $Configuration,
            '--no-build',
            '--no-restore',
            '--verbosity', 'minimal')
        Invoke-CheckedCommand dotnet @(
            'restore', $hostProjectPath,
            '--runtime', 'win-x64',
            "-p:Version=$Version")
        Invoke-CheckedCommand dotnet @(
            'publish', $hostProjectPath,
            '--configuration', $Configuration,
            '--runtime', 'win-x64',
            '--self-contained', 'true',
            '--no-restore',
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:DebugType=None',
            "-p:Version=$Version",
            '-p:ContinuousIntegrationBuild=true',
            '-o', $publishDirectory)
    }
    finally {
        Pop-Location
    }

    & $keyboardPackageScript `
        -Configuration $Configuration `
        -Version $Version `
        -OutputDirectory $assetDirectory
    & $audioPackageScript `
        -Configuration $Configuration `
        -Version $Version `
        -OutputDirectory $assetDirectory

    $publishedHostPath = Join-Path $publishDirectory 'ToolBox.Host.exe'
    if (-not (Test-Path -LiteralPath $publishedHostPath -PathType Leaf)) {
        throw "Published Host executable is missing: '$publishedHostPath'."
    }

    $hostAssetPath = Join-Path $assetDirectory $hostAssetName
    $keyboardAssetPath = Join-Path $assetDirectory $keyboardAssetName
    $audioAssetPath = Join-Path $assetDirectory $audioAssetName
    $checksumAssetPath = Join-Path $assetDirectory $checksumAssetName
    Copy-Item -LiteralPath $publishedHostPath -Destination $hostAssetPath

    if ((Get-Item -LiteralPath $hostAssetPath).Length -le 0) {
        throw "Published Host executable is empty: '$hostAssetPath'."
    }

    Assert-PluginPackage `
        -PackagePath $keyboardAssetPath `
        -ExpectedPluginId 'com.toolbox.keyboard-test' `
        -ExpectedVersion $Version `
        -ExpectedEntries @(
            'manifest.json',
            'package.json',
            'runtime/KeyboardTest.deps.json',
            'runtime/KeyboardTest.dll')
    Assert-PluginPackage `
        -PackagePath $audioAssetPath `
        -ExpectedPluginId 'com.toolbox.audio-relay' `
        -ExpectedVersion $Version `
        -ExpectedEntries @(
            'manifest.json',
            'package.json',
            'runtime/AudioRelay.deps.json',
            'runtime/AudioRelay.dll',
            'runtime/Microsoft.Windows.SDK.NET.dll',
            'runtime/WinRT.Runtime.dll')

    $releaseArtifactPaths = @($hostAssetPath, $keyboardAssetPath, $audioAssetPath)
    Write-AndValidateChecksums -ArtifactPaths $releaseArtifactPaths -ChecksumPath $checksumAssetPath
    Assert-ExactSet `
        -Actual @(Get-ChildItem -LiteralPath $assetDirectory -File | ForEach-Object { $_.Name }) `
        -Expected @($hostAssetName, $keyboardAssetName, $audioAssetName, $checksumAssetName) `
        -Label 'Release artifact files'

    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    foreach ($assetPath in @($releaseArtifactPaths + $checksumAssetPath)) {
        Copy-Item -LiteralPath $assetPath -Destination (Join-Path $outputRoot (Split-Path -Leaf $assetPath)) -Force
    }

    Write-Host "Release validation passed for ToolBox $releaseTag." -ForegroundColor Green
    Get-ChildItem -LiteralPath $outputRoot -File |
        Where-Object { $_.Name -in @($hostAssetName, $keyboardAssetName, $audioAssetName, $checksumAssetName) } |
        Sort-Object Name |
        Select-Object Name, Length, FullName
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-DirectoryWithRetry -Path $stagingRoot
    }
}
