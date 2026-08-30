[CmdletBinding()]
param(
    [switch]$IncludeDiffStat
)

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Repository: $repoRoot"
Write-Host "`n=== AI.md ==="
Get-Content -LiteralPath (Join-Path $repoRoot 'AI.md')

$activeTask = Join-Path $repoRoot 'docs\maintainer\tasks\active.md'
if (Test-Path -LiteralPath $activeTask) {
    Write-Host "`n=== active task ==="
    Get-Content -LiteralPath $activeTask
}

Write-Host "`n=== git status ==="
git -C $repoRoot status --short --branch

if ($IncludeDiffStat) {
    Write-Host "`n=== diff stat ==="
    git -C $repoRoot diff --stat
}
