[CmdletBinding()]
param(
    [switch]$IncludeDiffStat
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$workspacePath = Join-Path $workspaceRoot 'WORKSPACE.md'

if (Test-Path -LiteralPath $workspacePath) {
    Write-Host "=== WORKSPACE.md ==="
    Get-Content -LiteralPath $workspacePath
}
$workspaceRoot = Split-Path -Parent $repoRoot
$workspacePath = Join-Path $workspaceRoot 'WORKSPACE.md'

if (Test-Path -LiteralPath $workspacePath) {
    Write-Host "=== WORKSPACE.md ==="
    Get-Content -LiteralPath $workspacePath
}

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

Write-Host "`n=== git head ==="
git -C $repoRoot log -1 --oneline --decorate

Write-Host "`n=== git head ==="
git -C $repoRoot log -1 --oneline --decorate

if ($IncludeDiffStat) {
    Write-Host "`n=== diff stat ==="
    git -C $repoRoot diff --stat
}
