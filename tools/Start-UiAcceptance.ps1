[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.6.0',

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$AcceptanceRoot,

    [switch]$ResetAcceptanceData,

    [switch]$SkipBuild,

    [switch]$PrepareOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$defaultAcceptanceRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\ui-acceptance'))
$acceptanceRoot = if ([string]::IsNullOrWhiteSpace($AcceptanceRoot)) {
    $defaultAcceptanceRoot
} else {
    [System.IO.Path]::GetFullPath($AcceptanceRoot)
}

function Test-PathWithin {
    param(
        [Parameter(Mandatory)][string]$ChildPath,
        [Parameter(Mandatory)][string]$ParentPath
    )

    $child = [System.IO.Path]::GetFullPath($ChildPath).TrimEnd('\') + '\'
    $parent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\') + '\'
    return $child.StartsWith($parent, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Assert-AcceptancePath {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-PathWithin -ChildPath $Path -ParentPath (Join-Path $repositoryRoot 'artifacts'))) {
        throw "验收目录必须位于软件仓库的 artifacts 目录内：'$Path'。"
    }
}

function Get-RunningAcceptanceProcess {
    param([Parameter(Mandatory)][string]$ExecutablePath)

    $normalizedExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
    foreach ($process in @(Get-Process -Name 'ToolBox.Host' -ErrorAction SilentlyContinue)) {
        try {
            if ($process.HasExited) {
                continue
            }

            $processPath = $process.MainModule.FileName
            if ([string]::Equals(
                    [System.IO.Path]::GetFullPath($processPath),
                    $normalizedExecutablePath,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                return $process
            }
        }
        catch {
            # A process that cannot expose its path is not treated as this run.
        }
    }

    return $null
}

Assert-AcceptancePath -Path $acceptanceRoot

if ($ResetAcceptanceData -and (Test-Path -LiteralPath $acceptanceRoot)) {
    $hostDirectory = Join-Path $acceptanceRoot 'host'
    $hostExecutable = Join-Path $hostDirectory 'ToolBox.Host.exe'
    $runningProcess = if (Test-Path -LiteralPath $hostExecutable) {
        Get-RunningAcceptanceProcess -ExecutablePath $hostExecutable
    } else {
        $null
    }
    if ($null -ne $runningProcess) {
        throw "验收 Host 仍在运行（PID $($runningProcess.Id)），请先关闭它再重置验收数据。"
    }

    Remove-Item -LiteralPath $acceptanceRoot -Recurse -Force
}

$releaseOutput = Join-Path $acceptanceRoot 'release-validation'
$hostDirectory = Join-Path $acceptanceRoot 'host'
$hostDataRoot = Join-Path $acceptanceRoot 'data'
$hostExecutable = Join-Path $hostDirectory 'ToolBox.Host.exe'
$packagePath = Join-Path $releaseOutput "HelloPlugin-$Version.tpk"
$bundlePath = Join-Path $releaseOutput "ToolBox-v$Version-win-x64.zip"

if (-not $SkipBuild) {
    New-Item -ItemType Directory -Path $acceptanceRoot -Force | Out-Null
    Invoke-Checked 'pwsh' @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        (Join-Path $PSScriptRoot 'Invoke-ReleaseValidation.ps1'),
        '-Version',
        $Version,
        '-Configuration',
        $Configuration,
        '-SkipInstaller',
        '-OutputDirectory',
        $releaseOutput)

    if (Test-Path -LiteralPath $hostDirectory) {
        Remove-Item -LiteralPath $hostDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $bundlePath -DestinationPath $hostDirectory -Force
}

foreach ($requiredPath in @($hostExecutable, $packagePath, (Join-Path $hostDirectory 'ToolBox.PluginWorker.exe'))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "验收资源不存在：'$requiredPath'。请先去掉 -SkipBuild 重新运行。"
    }
}

New-Item -ItemType Directory -Path $hostDataRoot -Force | Out-Null
$runningHost = Get-RunningAcceptanceProcess -ExecutablePath $hostExecutable
if ($null -ne $runningHost) {
    Write-Host "验收 Host 已在运行（PID $($runningHost.Id)），不重复启动。" -ForegroundColor Yellow
    exit 0
}

$arguments = @(
    '--ui-acceptance-root', $hostDataRoot,
    '--ui-acceptance-package', $packagePath)
$argumentString = ($arguments | ForEach-Object {
        ConvertTo-ProcessArgument -Value ([string]$_)
    }) -join ' '

if ($PrepareOnly) {
    Write-Host "验收资源已准备完成：$acceptanceRoot" -ForegroundColor Green
    Write-Host "启动命令：& '$hostExecutable' $argumentString" -ForegroundColor DarkGray
    exit 0
}

$process = Start-Process `
    -FilePath $hostExecutable `
    -WorkingDirectory $hostDirectory `
    -ArgumentList $argumentString `
    -PassThru

Write-Host "ToolBox UI 验收 Host 已启动（PID $($process.Id)）。" -ForegroundColor Green
Write-Host "验收目录：$acceptanceRoot" -ForegroundColor DarkGray
