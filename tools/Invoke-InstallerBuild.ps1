[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.6.0',

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [string]$HostPublishDirectory,

    [string]$WorkerPublishDirectory,

    [string]$IsccPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hostProjectPath = Join-Path $repositoryRoot 'src\ToolBox.Host\ToolBox.Host.csproj'
$workerProjectPath = Join-Path $repositoryRoot 'src\ToolBox.PluginWorker\ToolBox.PluginWorker.csproj'
$installerScriptPath = Join-Path $repositoryRoot 'installer\ToolBox.iss'
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts\installer'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$stagingRoot = $null
$ownsPublishDirectories = [string]::IsNullOrWhiteSpace($HostPublishDirectory)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )

    Write-Host "> $FilePath $($ArgumentList -join ' ')" -ForegroundColor DarkGray
    $commandOutput = & $FilePath @ArgumentList
    foreach ($line in $commandOutput) {
        Write-Host $line
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Resolve-IsccPath {
    if (-not [string]::IsNullOrWhiteSpace($IsccPath)) {
        $resolved = [System.IO.Path]::GetFullPath($IsccPath)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "The specified Inno Setup compiler was not found: '$resolved'."
        }

        return $resolved
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw 'Inno Setup 6 compiler ISCC.exe was not found. Install Inno Setup 6 or pass -IsccPath explicitly.'
}

function Publish-Product {
    $script:stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxInstallerBuild\$([Guid]::NewGuid().ToString('N'))"
    $hostDirectory = Join-Path $script:stagingRoot 'host'
    $workerDirectory = Join-Path $script:stagingRoot 'worker'
    $artifactsDirectory = Join-Path $script:stagingRoot 'artifacts'
    New-Item -ItemType Directory -Path $hostDirectory, $workerDirectory -Force | Out-Null

    foreach ($projectPath in @($hostProjectPath, $workerProjectPath)) {
        Invoke-CheckedCommand 'dotnet' @(
            'restore',
            $projectPath,
            '--runtime',
            'win-x64',
            '--artifacts-path',
            $artifactsDirectory,
            '--disable-build-servers',
            '-p:NuGetAudit=false',
            '-p:ContinuousIntegrationBuild=true')
    }

    Invoke-CheckedCommand 'dotnet' @(
        'publish',
        $hostProjectPath,
        '--configuration',
        $Configuration,
        '--runtime',
        'win-x64',
        '--self-contained',
        'true',
        '--no-restore',
        '--artifacts-path',
        $artifactsDirectory,
        '--disable-build-servers',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        "-p:Version=$Version",
        '-p:ContinuousIntegrationBuild=true',
        '-o',
        $hostDirectory)

    Invoke-CheckedCommand 'dotnet' @(
        'publish',
        $workerProjectPath,
        '--configuration',
        $Configuration,
        '--runtime',
        'win-x64',
        '--self-contained',
        'true',
        '--no-restore',
        '--artifacts-path',
        $artifactsDirectory,
        '--disable-build-servers',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        "-p:Version=$Version",
        '-p:ContinuousIntegrationBuild=true',
        '-o',
        $workerDirectory)

    return @($hostDirectory, $workerDirectory)
}

try {
    if ([string]::IsNullOrWhiteSpace($HostPublishDirectory) -xor [string]::IsNullOrWhiteSpace($WorkerPublishDirectory)) {
        throw 'HostPublishDirectory and WorkerPublishDirectory must be provided together.'
    }

    if ($ownsPublishDirectories) {
        $publishedDirectories = Publish-Product
        $stagingRoot = Split-Path -Parent $publishedDirectories[0]
        $HostPublishDirectory = $publishedDirectories[0]
        $WorkerPublishDirectory = $publishedDirectories[1]
    } else {
        $HostPublishDirectory = [System.IO.Path]::GetFullPath($HostPublishDirectory)
        $WorkerPublishDirectory = [System.IO.Path]::GetFullPath($WorkerPublishDirectory)
    }

    foreach ($requiredFile in @(
            (Join-Path $HostPublishDirectory 'ToolBox.Host.exe'),
            (Join-Path $WorkerPublishDirectory 'ToolBox.PluginWorker.exe'))) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Published installer input is missing: '$requiredFile'."
        }
    }

    $iscc = Resolve-IsccPath
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    Invoke-CheckedCommand $iscc @(
        "/DMyAppVersion=$Version",
        "/DHostPublishDir=$HostPublishDirectory",
        "/DWorkerPublishDir=$WorkerPublishDirectory",
        "/O$outputRoot",
        $installerScriptPath)

    $setupPath = Join-Path $outputRoot "ToolBox-Setup-v$Version.exe"
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        throw "Inno Setup completed but the installer was not produced: '$setupPath'."
    }

    Write-Host "Installer created: $setupPath" -ForegroundColor Green
    Get-Item -LiteralPath $setupPath
}
finally {
    if ($ownsPublishDirectories -and $null -ne $stagingRoot -and (Test-Path -LiteralPath $stagingRoot)) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
