[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.4.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validationRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ToolBoxSampleValidation\$([Guid]::NewGuid().ToString('N'))"
$feedDirectory = Join-Path $validationRoot 'nuget'
$packageCache = Join-Path $validationRoot 'nuget-cache'
$signingRoot = Join-Path $validationRoot 'sample-signing'
$buildArtifacts = Join-Path $validationRoot 'build-artifacts'
$packageOutput = Join-Path $validationRoot 'packages'

function Invoke-Checked {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments)
    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $feedDirectory, $packageCache, $signingRoot -Force | Out-Null
$signingCertificatePath = Join-Path $signingRoot 'sample-signing.cer'
$signingPrivateKeyPath = Join-Path $signingRoot 'sample-signing.pk8'
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
try {
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=ToolBox Sample Validation',
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddDays(-1),
        [DateTimeOffset]::UtcNow.AddDays(7))
    try {
        [System.IO.File]::WriteAllBytes($signingCertificatePath, $certificate.Export(
            [System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        [System.IO.File]::WriteAllBytes($signingPrivateKeyPath, $rsa.ExportPkcs8PrivateKey())
    }
    finally {
        $certificate.Dispose()
    }
}
finally {
    $rsa.Dispose()
}
Invoke-Checked 'dotnet' @('restore', (Join-Path $repositoryRoot 'src\ToolBox.PluginSdk\ToolBox.PluginSdk.csproj'), '--artifacts-path', $buildArtifacts, '-p:NuGetAudit=false')
Invoke-Checked 'dotnet' @('pack', (Join-Path $repositoryRoot 'src\ToolBox.PluginSdk\ToolBox.PluginSdk.csproj'), '--configuration', $Configuration, '--output', $feedDirectory, '--artifacts-path', $buildArtifacts, '--no-restore', '-warnaserror', '--disable-build-servers')

$projects = @(
    @{ Path = 'samples\HelloPlugin\HelloPlugin.csproj'; ProjectName = 'HelloPlugin'; Manifest = 'samples\HelloPlugin\manifest.json' }
)

try {
    foreach ($project in $projects) {
        $projectPath = Join-Path $repositoryRoot $project.Path
        $restorePackagesPath = "-p:RestorePackagesPath=$packageCache"
        Invoke-Checked 'dotnet' @('restore', $projectPath, '--source', $feedDirectory, '--artifacts-path', $buildArtifacts, $restorePackagesPath, '-p:NuGetAudit=false')
        Invoke-Checked 'dotnet' @('build', $projectPath, '--configuration', $Configuration, '--artifacts-path', $buildArtifacts, '--no-restore', '--no-incremental', '-warnaserror', '--disable-build-servers', $restorePackagesPath)
        $runtimeDirectory = Join-Path $buildArtifacts "bin\$($project.ProjectName)\$($Configuration.ToLowerInvariant())"
        Invoke-Checked 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'New-PluginPackage.ps1'), '-RuntimeDirectory', $runtimeDirectory, '-ManifestPath', (Join-Path $repositoryRoot $project.Manifest), '-Version', $Version, '-OutputDirectory', $packageOutput, '-SigningCertificatePath', $signingCertificatePath, '-SigningPrivateKeyPath', $signingPrivateKeyPath, '-Overwrite')
    }
}
finally {
    if (Test-Path -LiteralPath $validationRoot) {
        Remove-Item -LiteralPath $validationRoot -Recurse -Force
    }
}

Write-Host 'HelloPlugin sample built and packaged successfully.' -ForegroundColor Green
