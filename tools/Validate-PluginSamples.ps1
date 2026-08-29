[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.2.2'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$feedDirectory = Join-Path $repositoryRoot 'artifacts\nuget'
$packageCache = Join-Path $repositoryRoot 'artifacts\nuget-cache'

function Invoke-Checked {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments)
    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $feedDirectory, $packageCache -Force | Out-Null
Invoke-Checked 'dotnet' @('pack', (Join-Path $repositoryRoot 'src\ToolBox.PluginSdk\ToolBox.PluginSdk.csproj'), '--configuration', $Configuration, '--output', $feedDirectory)

$projects = @(
    @{ Path = 'samples\HelloPlugin\HelloPlugin.csproj'; Runtime = 'samples\HelloPlugin\bin\' + $Configuration + '\net8.0'; Manifest = 'samples\HelloPlugin\manifest.json' }
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repositoryRoot $project.Path
    $restorePackagesPath = "-p:RestorePackagesPath=$packageCache"
    Invoke-Checked 'dotnet' @('restore', $projectPath, $restorePackagesPath)
    Invoke-Checked 'dotnet' @('build', $projectPath, '--configuration', $Configuration, '--no-restore', $restorePackagesPath)
    Invoke-Checked 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'New-PluginPackage.ps1'), '-RuntimeDirectory', (Join-Path $repositoryRoot $project.Runtime), '-ManifestPath', (Join-Path $repositoryRoot $project.Manifest), '-Version', $Version, '-OutputDirectory', (Join-Path $repositoryRoot 'artifacts'), '-Overwrite')
}

Write-Host 'HelloPlugin sample built and packaged successfully.' -ForegroundColor Green
