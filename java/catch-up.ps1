# Usage: .\catch-up.ps1 <branch>   e.g. .\catch-up.ps1 step-2-coder
# Saves your uncommitted work in a labeled stash, then switches to the branch.
param([string]$Branch)
# No 'Stop': in Windows PowerShell 5.1 a git message on stderr would then abort the script.
# Every git call below checks $LASTEXITCODE instead.
if (-not $Branch) {
  Write-Host "Usage: .\catch-up.ps1 <branch>   (step-1-file-tools, step-2-coder, step-3-planner, step-4-reviewer, main)"
  exit 2
}
Set-Location $PSScriptRoot
git fetch --quiet origin $Branch 2>$null
git rev-parse --verify --quiet "refs/heads/$Branch" *> $null
$local = $LASTEXITCODE -eq 0
git rev-parse --verify --quiet "refs/remotes/origin/$Branch" *> $null
if (-not $local -and $LASTEXITCODE -ne 0) {
  Write-Host "There is no branch called $Branch. Nothing was changed."
  Write-Host "Branches: step-1-file-tools, step-2-coder, step-3-planner, step-4-reviewer, main, main-2h"
  exit 2
}
$label = "catch-up: work before $Branch, $(Get-Date -Format 'HH:mm:ss')"
$stashed = $false
if (git status --porcelain) {
  git stash push --include-untracked --quiet -m $label
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  $stashed = git rev-parse 'stash@{0}'
}
if ($local) {
  git checkout --quiet $Branch
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  git rev-parse --verify --quiet "refs/remotes/origin/$Branch" *> $null
  if ($LASTEXITCODE -eq 0) {
    git merge --quiet --ff-only "origin/$Branch" 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Host "Note: your local $Branch differs from origin/$Branch; kept your local version." }
  }
} else {
  git checkout --quiet -b $Branch --track "origin/$Branch"
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$global:LASTEXITCODE = 0
Write-Host "You are now on $Branch."
if ($stashed) {
  Write-Host "Your previous changes are saved in a stash named: $label"
  Write-Host "To get them back later:  git stash apply $stashed"
}
