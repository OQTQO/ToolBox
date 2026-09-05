[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SetupPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$setup = [System.IO.Path]::GetFullPath($SetupPath)
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw "Installer was not found: '$setup'."
}

$validationRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxInstallerValidation\$([Guid]::NewGuid().ToString('N'))"
$installRoot = Join-Path $validationRoot 'install'
$dataRoot = Join-Path $installRoot 'Data'

function Invoke-Installer {
    param([Parameter(Mandatory)][string[]]$Arguments)

    Write-Host "> $setup $($Arguments -join ' ')" -ForegroundColor DarkGray
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $setup
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Installer command failed with exit code $($process.ExitCode)."
    }
}

try {
    New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
    Invoke-Installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$installRoot")

    foreach ($requiredFile in @(
            (Join-Path $installRoot 'ToolBox.Host.exe'),
            (Join-Path $installRoot 'ToolBox.PluginWorker.exe'))) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Installed file is missing: '$requiredFile'."
        }
    }

    $settingsPath = Join-Path $dataRoot 'ui-settings.json'
    $pluginDataPath = Join-Path $dataRoot 'PluginData\installer-validation\keep.txt'
    New-Item -ItemType Directory -Path (Split-Path -Parent $pluginDataPath) -Force | Out-Null
    Set-Content -LiteralPath $settingsPath -Value 'keep-settings' -Encoding utf8
    Set-Content -LiteralPath $pluginDataPath -Value 'keep-plugin-data' -Encoding utf8

    Invoke-Installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$installRoot")
    if ((Get-Content -LiteralPath $settingsPath -Raw).Trim() -ne 'keep-settings') {
        throw 'Installer upgrade did not preserve ui-settings.json.'
    }
    if ((Get-Content -LiteralPath $pluginDataPath -Raw).Trim() -ne 'keep-plugin-data') {
        throw 'Installer upgrade did not preserve plugin data.'
    }

    $samplePluginPath = Join-Path $dataRoot 'Plugins\com.toolbox.hello'
    if (Test-Path -LiteralPath $samplePluginPath) {
        throw 'The software installer unexpectedly contains the HelloPlugin sample.'
    }

    $uninstaller = Join-Path $installRoot 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw "Uninstaller is missing: '$uninstaller'."
    }

    $uninstallInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $uninstallInfo.FileName = $uninstaller
    foreach ($argument in @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')) {
        [void]$uninstallInfo.ArgumentList.Add($argument)
    }
    $uninstallProcess = [System.Diagnostics.Process]::Start($uninstallInfo)
    $uninstallProcess.WaitForExit()
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Uninstaller failed with exit code $($uninstallProcess.ExitCode)."
    }
    $userDataMissing = (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) -or
        (-not (Test-Path -LiteralPath $pluginDataPath -PathType Leaf))
    if ($userDataMissing) {
        throw 'Uninstall removed user data under the Data directory.'
    }

    Write-Host 'Installer validation passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $validationRoot) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}
