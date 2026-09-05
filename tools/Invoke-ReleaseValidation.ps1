param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [string]$SigningCertificatePath,

    [string]$SigningPrivateKeyPath,

    [switch]$SkipInstaller
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
$installerBuildScript = Join-Path $PSScriptRoot 'Invoke-InstallerBuild.ps1'
$usesDefaultOutputDirectory = [string]::IsNullOrWhiteSpace($OutputDirectory)
$OutputDirectory = if ($usesDefaultOutputDirectory) {
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
$buildArtifacts = Join-Path $stagingRoot 'build-artifacts'
$ephemeralSigning = [string]::IsNullOrWhiteSpace($SigningCertificatePath) -and [string]::IsNullOrWhiteSpace($SigningPrivateKeyPath)
if ([string]::IsNullOrWhiteSpace($SigningCertificatePath) -ne [string]::IsNullOrWhiteSpace($SigningPrivateKeyPath)) {
    throw 'SigningCertificatePath and SigningPrivateKeyPath must be provided together.'
}
$effectiveSigningCertificatePath = if ($ephemeralSigning) {
    Join-Path $stagingRoot 'validation-signing.cer'
} else {
    [System.IO.Path]::GetFullPath($SigningCertificatePath)
}
$effectiveSigningPrivateKeyPath = if ($ephemeralSigning) {
    Join-Path $stagingRoot 'validation-signing.pk8'
} else {
    [System.IO.Path]::GetFullPath($SigningPrivateKeyPath)
}

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
    @('manifest.json', 'package.json', 'signature.json') + @(
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
        $payloadEntries = @($entryNames | Where-Object { $_ -cne 'package.json' -and $_ -cne 'signature.json' })
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

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    if ($Value -notmatch '[\s"]') { return $Value }

    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Invoke-HostPackageSmokeTest {
    param(
        [Parameter(Mandatory)][string]$HostPath,
        [Parameter(Mandatory)][string]$WorkerPath,
        [Parameter(Mandatory)][string]$PackagePath,
        [Parameter(Mandatory)][string]$WorkingRoot
    )

    $resultPath = Join-Path $WorkingRoot 'result.json'
    $pluginRoot = Join-Path $WorkingRoot 'plugin-data'
    New-Item -ItemType Directory -Path $WorkingRoot, $pluginRoot -Force | Out-Null
    $arguments = @(
        '--smoke-test-package', $PackagePath,
        '--smoke-test-worker', $WorkerPath,
        '--smoke-test-root', $pluginRoot,
        '--smoke-test-result', $resultPath)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $HostPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    if ($null -ne $startInfo.PSObject.Properties['ArgumentList']) {
        foreach ($argument in $arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    }
    else {
        $startInfo.Arguments = ($arguments | ForEach-Object { ConvertTo-ProcessArgument -Value $_ }) -join ' '
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        if (-not $process.WaitForExit(60000)) {
            try { $process.Kill() } catch { }
            throw 'Self-contained Host package smoke test timed out.'
        }
        if ($process.ExitCode -ne 0) {
            $details = if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
                Get-Content -LiteralPath $resultPath -Raw
            } else {
                'The Host did not write a smoke-test result.'
            }
            throw "Self-contained Host package smoke test failed with exit code $($process.ExitCode): $details"
        }
    }
    finally { $process.Dispose() }

    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw 'The self-contained Host did not write a smoke-test result.'
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $packageResult = @($result.packages)
    if (-not $result.success -or $packageResult.Count -ne 1 `
        -or -not $packageResult[0].installed -or -not $packageResult[0].enabled `
        -or -not $packageResult[0].disabled -or -not $packageResult[0].uninstalled) {
        throw "Self-contained Host package smoke test did not complete its lifecycle: $($result | ConvertTo-Json -Depth 8 -Compress)"
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Get-ProjectVersion -ProjectPath $hostProjectPath }

$releaseTag = "v$Version"
$toolBoxAssetName = "ToolBox-$releaseTag-win-x64.zip"
$setupAssetName = "ToolBox-Setup-v$Version.exe"
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
    if ($ephemeralSigning) {
        $rsa = [System.Security.Cryptography.RSA]::Create(2048)
        try {
            $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
                'CN=ToolBox Release Validation',
                $rsa,
                [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
            $certificate = $request.CreateSelfSigned(
                [DateTimeOffset]::UtcNow.AddDays(-1),
                [DateTimeOffset]::UtcNow.AddDays(7))
            try {
                [System.IO.File]::WriteAllBytes(
                    $effectiveSigningCertificatePath,
                    $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
                [System.IO.File]::WriteAllBytes(
                    $effectiveSigningPrivateKeyPath,
                    $rsa.ExportPkcs8PrivateKey())
            }
            finally { $certificate.Dispose() }
        }
        finally { $rsa.Dispose() }
    }
    elseif (-not (Test-Path -LiteralPath $effectiveSigningCertificatePath -PathType Leaf) `
        -or -not (Test-Path -LiteralPath $effectiveSigningPrivateKeyPath -PathType Leaf)) {
        throw 'Release signing certificate or PKCS#8 private key is missing.'
    }
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
        Invoke-CheckedCommand dotnet @('restore', $solutionPath, '--artifacts-path', $buildArtifacts, '-p:NuGetAudit=false')
        Invoke-CheckedCommand dotnet @('build', $solutionPath, '--configuration', $Configuration, '--artifacts-path', $buildArtifacts, '--no-restore', '--no-incremental', '-warnaserror', '--disable-build-servers', "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true')
        Invoke-CheckedCommand dotnet @('test', $solutionPath, '--configuration', $Configuration, '--artifacts-path', $buildArtifacts, '--no-build', '--no-restore', '--disable-build-servers', '--verbosity', 'minimal')
        Invoke-CheckedCommand dotnet @('pack', $pluginSdkProjectPath, '--configuration', $Configuration, '--artifacts-path', $buildArtifacts, '--no-restore', '--output', $sampleFeedDirectory, "-p:Version=$Version", "-p:PackageVersion=$Version")
        Invoke-CheckedCommand dotnet @('restore', $helloProjectPath, '--configfile', $sampleNuGetConfigPath, '--artifacts-path', $buildArtifacts, "-p:RestorePackagesPath=$samplePackageCache", '-p:NuGetAudit=false')
        Invoke-CheckedCommand dotnet @('build', $helloProjectPath, '--configuration', $Configuration, '--artifacts-path', $buildArtifacts, '--no-restore', '--no-incremental', '-warnaserror', "-p:RestorePackagesPath=$samplePackageCache")
        Invoke-CheckedCommand dotnet @('restore', $hostProjectPath, '--runtime', 'win-x64', '--artifacts-path', $buildArtifacts, "-p:Version=$Version", '-p:NuGetAudit=false')
        Invoke-CheckedCommand dotnet @('restore', $pluginWorkerProjectPath, '--runtime', 'win-x64', '--artifacts-path', $buildArtifacts, "-p:Version=$Version", '-p:NuGetAudit=false')
        Invoke-CheckedCommand dotnet @('publish', $hostProjectPath, '--configuration', $Configuration, '--runtime', 'win-x64', '--artifacts-path', $buildArtifacts, '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true', '-o', $publishDirectory)
        Invoke-CheckedCommand dotnet @('publish', $pluginWorkerProjectPath, '--configuration', $Configuration, '--runtime', 'win-x64', '--artifacts-path', $buildArtifacts, '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', "-p:Version=$Version", '-p:ContinuousIntegrationBuild=true', '-o', $workerPublishDirectory)
    }
    finally { Pop-Location }

    $helloRuntimeDirectory = Join-Path $buildArtifacts "bin\HelloPlugin\$($Configuration.ToLowerInvariant())"
    & $packageScript -RuntimeDirectory $helloRuntimeDirectory -ManifestPath $helloManifestPath -Version $Version -PackageName $helloAssetName -OutputDirectory $assetDirectory -SigningCertificatePath $effectiveSigningCertificatePath -SigningPrivateKeyPath $effectiveSigningPrivateKeyPath
    if ($LASTEXITCODE -ne 0) { throw "HelloPlugin package generation failed with exit code $LASTEXITCODE." }

    $publishedHostPath = Join-Path $publishDirectory 'ToolBox.Host.exe'
    if (-not (Test-Path -LiteralPath $publishedHostPath -PathType Leaf)) { throw "Published Host executable is missing." }
    $publishedWorkerPath = Join-Path $workerPublishDirectory 'ToolBox.PluginWorker.exe'
    if (-not (Test-Path -LiteralPath $publishedWorkerPath -PathType Leaf)) { throw "Published PluginWorker executable is missing." }

    Invoke-HostPackageSmokeTest `
        -HostPath $publishedHostPath `
        -WorkerPath $publishedWorkerPath `
        -PackagePath (Join-Path $assetDirectory $helloAssetName) `
        -WorkingRoot (Join-Path $stagingRoot 'host-smoke')

    if (-not $SkipInstaller) {
        & $installerBuildScript `
            -Version $Version `
            -Configuration $Configuration `
            -HostPublishDirectory $publishDirectory `
            -WorkerPublishDirectory $workerPublishDirectory `
            -OutputDirectory $assetDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Installer build failed with exit code $LASTEXITCODE."
        }
    }

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
    Assert-PluginPackage -PackagePath $helloAssetPath -ExpectedPluginId 'com.toolbox.hello' -ExpectedVersion $Version -ExpectedEntries (Get-ExpectedPackageEntries -RuntimeDirectory $helloRuntimeDirectory)

    $releaseArtifactPaths = @($toolBoxAssetPath, $helloAssetPath, $devKitPath)
    if (-not $SkipInstaller) {
        $setupAssetPath = Join-Path $assetDirectory $setupAssetName
        if (-not (Test-Path -LiteralPath $setupAssetPath -PathType Leaf)) {
            throw "Installer artifact is missing: '$setupAssetPath'."
        }
        $releaseArtifactPaths = @($toolBoxAssetPath, $setupAssetPath, $helloAssetPath, $devKitPath)
    }
    Write-AndValidateChecksums -ArtifactPaths $releaseArtifactPaths -ChecksumPath $checksumAssetPath
    $expectedArtifactNames = @($toolBoxAssetName, $helloAssetName, $devKitAssetName, $checksumAssetName)
    if (-not $SkipInstaller) {
        $expectedArtifactNames = @($toolBoxAssetName, $setupAssetName, $helloAssetName, $devKitAssetName, $checksumAssetName)
    }
    Assert-ExactSet -Actual @(Get-ChildItem -LiteralPath $assetDirectory -File | ForEach-Object { $_.Name }) -Expected $expectedArtifactNames -Label 'Release artifact files'

    if ($usesDefaultOutputDirectory -and (Test-Path -LiteralPath $outputRoot)) {
        Remove-DirectoryWithRetry -Path $outputRoot
    }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    foreach ($assetPath in @($releaseArtifactPaths + $checksumAssetPath)) { Copy-Item -LiteralPath $assetPath -Destination (Join-Path $outputRoot (Split-Path -Leaf $assetPath)) -Force }

    if ($usesDefaultOutputDirectory) {
        Assert-ExactSet `
            -Actual @(Get-ChildItem -LiteralPath $outputRoot -File | ForEach-Object { $_.Name }) `
            -Expected $expectedArtifactNames `
            -Label 'Default release-validation output files'
    }

    Write-Host "Release validation passed for ToolBox $releaseTag." -ForegroundColor Green
    @($releaseArtifactPaths + $checksumAssetPath) | ForEach-Object {
        Get-Item -LiteralPath (Join-Path $outputRoot (Split-Path -Leaf $_))
    } | Sort-Object Name | Select-Object Name, Length, FullName
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-DirectoryWithRetry -Path $stagingRoot }
}
