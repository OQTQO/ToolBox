Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-DeterministicZipArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $sourceRoot = [System.IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
        throw "Archive source directory is missing: '$sourceRoot'."
    }

    if (Test-Path -LiteralPath $DestinationPath) {
        throw "Archive destination already exists: '$DestinationPath'."
    }

    $fixedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $destinationStream = [System.IO.File]::Open(
        $DestinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    $archiveCompleted = $false
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $destinationStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            $sourceFiles = @(
                Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
                    Sort-Object { $_.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/') })
            foreach ($sourceFile in $sourceFiles) {
                $relativePath = $sourceFile.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
                $entry = $archive.CreateEntry(
                    $relativePath,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp

                $inputStream = $sourceFile.OpenRead()
                $entryStream = $entry.Open()
                try {
                    $inputStream.CopyTo($entryStream)
                }
                finally {
                    $entryStream.Dispose()
                    $inputStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
        $archiveCompleted = $true
    }
    finally {
        $destinationStream.Dispose()
        if (-not $archiveCompleted -and (Test-Path -LiteralPath $DestinationPath)) {
            Remove-Item -LiteralPath $DestinationPath -Force
        }
    }
}

Export-ModuleMember -Function New-DeterministicZipArchive
