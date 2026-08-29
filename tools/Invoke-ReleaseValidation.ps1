param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'ToolBox.sln'
$hostProjectPath = Join-Path $repositoryRoot 'src\ToolBox.Host\ToolBox.Host.csproj'
$coreProjectPath = Join-Path $repositoryRoot 'src\ToolBox.Core\ToolBox.Core.csproj'
$pluginSdkProjectPath = Join-Path $repositoryRoot 'src\ToolBox.PluginSdk\ToolBox.PluginSdk.csproj'
$pluginWorkerProjectPath = Join-Path $repositoryRoot 'src\ToolBox.PluginWorker\ToolBox.PluginWorker.csproj'
$helloProjectPath = Join-Path $repositoryRoot 'samples\HelloPlugin\HelloPlugin.csproj'
$helloManifestPath = Join-Path $repositoryRoot 'samples\HelloPlugin\manifest.json'
$packageScript = Join-Path $PSScriptRoot 'New-PluginPackage.ps1'
$packageToolsModule = Join-Path $PSScriptRoot 'ToolBox.PackageTools.psm1'
$OutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $PSScriptRoot '..\artifacts\release-validation'
} else {
    $OutputDirectory
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxReleaseValidation\$([Guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $stagingRoot 'publish'
$workerPublishDirectory = Join-Path $stagingRoot 'worker-publish'
$assetDirectory = Join-Path $stagingRoot 'assets'
$bundleDirectory = Join-Path $stagingRoot 'toolbox-bundle'
$devKitDirectory = Join-Path $stagingRoot 'devkit'
$sampleFeedDirectory = Join-Path $stagingRoot 'sdk-feed'
$samplePackageCache = Join-Path $stagingRoot 'nuget-cache'
$sampleNuGetConfigPath = Join-Path $stagingRoot 'NuGet.config'

Import-Module $packageToolsModule -Force

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
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
        throw "The project does not declare a Version property: '$ProjectPath'."
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
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
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
    if ($null -eq $entry) { throw "Package entry '$EntryName' is missing." }

    $stream = $entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true, 1024, $true)
        try { return ($reader.ReadToEnd() | ConvertFrom-Json) }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-ZipEntrySha256 {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $stream = $Entry.Open()
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}

function Get-ExpectedPackageEntries {
    param([Parameter(Mandatory)][string]$RuntimeDirectory)

    $runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\', '/')
    $runtimeManifestPath = Join-Path $runtimeRoot 'manifest.json'
    @('manifest.json', 'package.json') + @(
        Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse | Where-Object {
            $_.FullName -ine $runtimeManifestPath -and $_.Name -notlike 'ToolBox.PluginSdk.*'
        } | ForEach-Object {
            $relativePath = $_.FullName.Substring($runtimeRoot.Length + 1).Replace('\', '/')
            "runtime/$relativePath"
        })
}

function Assert-PluginPackage {
    param(
        [Parameter(Mandatory)][string]$PackagePath,
        [Parameter(Mandatory)][string]$ExpectedPluginId,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string[]]$ExpectedEntries
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entryNames = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } | ForEach-Object { $_.FullName.Replace('\', '/') })
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
            if ($null -eq $entry) { throw "Hash inventory entry '$($fileHash.path)' is missing." }
            if ((Get-ZipEntrySha256 -Entry $entry) -cne ([string]$fileHash.sha256).ToLowerInvariant()) {
                throw "Hash mismatch for '$($fileHash.path)' in '$PackagePath'."
            }
        }
    }
    finally { $archive.Dispose() }
}

function Write-AndValidateChecksums {
    param(
        [Parameter(Mandatory)][string[]]$ArtifactPaths,
        [Parameter(Mandatory)][string]$ChecksumPath
    )

    foreach ($artifactPath in $ArtifactPaths) {
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) { throw "Release artifact is missing: '$artifactPath'." }
    }

    $checksumLines = foreach ($artifactPath in $ArtifactPaths) {
        $fileHash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath
        "{0}  {1}" -f $fileHash.Hash.ToLowerInvariant(), (Split-Path -Leaf $fileHash.Path)
    }
    $checksumLines | Set-Content -LiteralPath $ChecksumPath -Encoding ascii

    $validatedNames = @()
    foreach ($line in @(Get-Content -LiteralPath $ChecksumPath)) {
        if ($line -cnotmatch '^([a-f0-9]{64})  ([^\\/]+)$') { throw "Invalid checksum line: '$line'." }
        $fileName = $Matches[2]
        $artifactPath = Join-Path (Split-Path -Parent $ChecksumPath) $fileName
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath).Hash.ToLowerInvariant()
        if ($actualHash -cne $Matches[1]) { throw "Checksum validation failed for '$fileName'." }
        $validatedNames += $fileName
    }
    Assert-ExactSet -Actual $validatedNames -Expected @($ArtifactPaths | ForEach-Object { Split-Path -Leaf $_ }) -Label 'Checksum manifest file names'
}

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Get-ProjectVersion -ProjectPath $hostProjectPath }

$releaseTag = "v$Version"
$toolBoxAssetName = "ToolBox-$releaseTag-win-x64.zip"
$helloAssetName = "HelloPlugin-$Version.tpk"
$devKitAssetName = "ToolBox-PluginDevKit-$Version.zip"
$checksumAssetName = "SHA256SUMS-$releaseTag.txt"

foreach ($projectPath in @($hostProjectPath, $coreProjectPath, $pluginSdkProjectPath, $pluginWorkerProjectPath, $helloProjectPath)) {
    if ((Get-ProjectVersion -ProjectPath $projectPath) -cne $Version) { throw "Project '$projectPath' does not declare version '$Version'." }
}
$helloManifest = Get-Content -LiteralPath $helloManifestPath -Raw | ConvertFrom-Json
if ([string]$helloManifest.version -cne $Version) { throw "HelloPlugin manifest does not declare version '$Version'." }

try {
    New-Item -ItemType Directory -Path $publishDirectory, $workerPublishDirectory, $assetDirectory, $bundleDirectory, $sampleFeedDirectory, $samplePackageCache -Force | Out-Null
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="toolbox-local-sdk" value="$sampleFeedDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $sampleNuGetConfigPath -Encoding utf8

    Push-Location $repositoryRoot
    try {
        Invoke-CheckedCommand dotnet @('clean', $solutionPath, '--configuration', $Configuration, '--verbosity', 'minimal')
        Invoke-CheckedCommand dotnet @('restore', $solutionPath)
        Invoke-CheckedCommand dotnet @('build', $solutionPath, '--configuration', $Configuration, '--no-restore', '-warnaserror', "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true')
        Invoke-CheckedCommand dotnet @('test', $solutionPath, '--configuration', $Configuration, '--no-build', '--no-restore', '--verbosity', 'minimal')
        Invoke-CheckedCommand dotnet @('pack', $pluginSdkProjectPath, '--configuration', $Configuration, '--no-restore', '--output', $sampleFeedDirectory, "-p:Version=$Version", "-p:PackageVersion=$Version")
        Invoke-CheckedCommand dotnet @('restore', $helloProjectPath, '--configfile', $sampleNuGetConfigPath, "-p:RestorePackagesPath=$samplePackageCache")
        Invoke-CheckedCommand dotnet @('build', $helloProjectPath, '--configuration', $Configuration, '--no-restore', "-p:RestorePackagesPath=$samplePackageCache")
        Invoke-CheckedCommand dotnet @('restore', $hostProjectPath, '--runtime', 'win-x64', "-p:Version=$Version")
        Invoke-CheckedCommand dotnet @('restore', $pluginWorkerProjectPath, '--runtime', 'win-x64', "-p:Version=$Version")
        Invoke-CheckedCommand dotnet @('publish', $hostProjectPath, '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true', '-o', $publishDirectory)
        Invoke-CheckedCommand dotnet @('publish', $pluginWorkerProjectPath, '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true', '-o', $workerPublishDirectory)
    }
    finally { Pop-Location }

    & $packageScript -RuntimeDirectory (Join-Path $repositoryRoot "samples\HelloPlugin\bin\$Configuration\net8.0") -ManifestPath $helloManifestPath -Version $Version -PackageName $helloAssetName -OutputDirectory $assetDirectory
    if ($LASTEXITCODE -ne 0) { throw "HelloPlugin package generation failed with exit code $LASTEXITCODE." }

    $publishedHostPath = Join-Path $publishDirectory 'ToolBox.Host.exe'
    if (-not (Test-Path -LiteralPath $publishedHostPath -PathType Leaf)) { throw "Published Host executable is missing." }
    $publishedWorkerPath = Join-Path $workerPublishDirectory 'ToolBox.PluginWorker.exe'
    if (-not (Test-Path -LiteralPath $publishedWorkerPath -PathType Leaf)) { throw "Published PluginWorker executable is missing." }

    Copy-Item -LiteralPath $publishedHostPath -Destination (Join-Path $bundleDirectory 'ToolBox.Host.exe')
    Copy-Item -LiteralPath $publishedWorkerPath -Destination (Join-Path $bundleDirectory 'ToolBox.PluginWorker.exe')

    New-Item -ItemType Directory -Path (Join-Path $devKitDirectory 'sdk'), (Join-Path $devKitDirectory 'tools'), (Join-Path $devKitDirectory 'docs'), (Join-Path $devKitDirectory 'samples\HelloPlugin') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sampleFeedDirectory "ToolBox.PluginSdk.$Version.nupkg") -Destination (Join-Path $devKitDirectory 'sdk')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'New-PluginPackage.ps1'), $packageToolsModule -Destination (Join-Path $devKitDirectory 'tools')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\plugin-development.md'), (Join-Path $repositoryRoot 'docs\plugin-manifest.md'), (Join-Path $repositoryRoot 'docs\plugin-runtime.md') -Destination (Join-Path $devKitDirectory 'docs')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'samples\HelloPlugin\HelloPlugin.csproj'), (Join-Path $repositoryRoot 'samples\HelloPlugin\HelloPlugin.cs'), $helloManifestPath -Destination (Join-Path $devKitDirectory 'samples\HelloPlugin')
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="toolbox-local-sdk" value="sdk" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $devKitDirectory 'NuGet.config') -Encoding utf8

    $devKitPath = Join-Path $assetDirectory $devKitAssetName
    New-DeterministicZipArchive -SourceDirectory $devKitDirectory -DestinationPath $devKitPath

    $toolBoxAssetPath = Join-Path $assetDirectory $toolBoxAssetName
    $helloAssetPath = Join-Path $assetDirectory $helloAssetName
    $checksumAssetPath = Join-Path $assetDirectory $checksumAssetName
    New-DeterministicZipArchive -SourceDirectory $bundleDirectory -DestinationPath $toolBoxAssetPath
    Assert-PluginPackage -PackagePath $helloAssetPath -ExpectedPluginId 'com.toolbox.hello' -ExpectedVersion $Version -ExpectedEntries (Get-ExpectedPackageEntries -RuntimeDirectory (Join-Path $repositoryRoot "samples\HelloPlugin\bin\$Configuration\net8.0"))

    $releaseArtifactPaths = @($toolBoxAssetPath, $helloAssetPath, $devKitPath)
    Write-AndValidateChecksums -ArtifactPaths $releaseArtifactPaths -ChecksumPath $checksumAssetPath
    Assert-ExactSet -Actual @(Get-ChildItem -LiteralPath $assetDirectory -File | ForEach-Object { $_.Name }) -Expected @($toolBoxAssetName, $helloAssetName, $devKitAssetName, $checksumAssetName) -Label 'Release artifact files'

    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    foreach ($assetPath in @($releaseArtifactPaths + $checksumAssetPath)) { Copy-Item -LiteralPath $assetPath -Destination (Join-Path $outputRoot (Split-Path -Leaf $assetPath)) -Force }

    Write-Host "Release validation passed for ToolBox $releaseTag." -ForegroundColor Green
    Get-ChildItem -LiteralPath $outputRoot -File | Sort-Object Name | Select-Object Name, Length, FullName
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-DirectoryWithRetry -Path $stagingRoot }
}
