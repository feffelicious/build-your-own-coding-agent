# Puts /workspace back by running app/reset-workspace inside the running container, so
# your VS Code window stays connected. Your app code and your run logs are not touched.
# (In the container terminal, just run reset-workspace yourself.)
#   .\reset-workspace.ps1          the calculator project, as at the start
#   .\reset-workspace.ps1 -Empty   an empty project (a git repo with no files), for your own tasks
param([switch]$Empty)
# No 'Stop': in Windows PowerShell 5.1 a docker message on stderr would then abort the script.
Set-Location $PSScriptRoot
$track = (Select-String -Path track.env -Pattern '^TRACK=(.+)$').Matches[0].Groups[1].Value.Trim()
$resetArgs = if ($Empty) { @('--empty') } else { @() }

$running = docker compose ps -q --status running workshop 2>$null
if ($running) {
  docker compose exec -T workshop /home/agent/app/reset-workspace @resetArgs
  if ($LASTEXITCODE -eq 0) { exit 0 }
}

# The container isn't running, or the reset failed in it: start over with a new
# volume. This restarts the containers.
Write-Host "Recreating the workspace volume (this restarts the containers)..."
docker compose down
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
docker volume rm "coding-agent-${track}_workspace" *> $null
docker compose up -d
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Empty) {
  # A fresh volume holds only runner's seed files, so runner can clear it.
  $script = 'cd /workspace && find . -mindepth 1 -delete && git init -q -b main ' +
    '&& git -c user.name="Workshop Seed" -c user.email=seed@workshop.invalid commit -q --allow-empty -m empty'
  docker compose exec -T workshop sudo -u runner timeout 120 sh -c $script
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  Write-Host "Workspace emptied: /workspace is an empty git repo. Give the agent a task of your own, e.g. run-agent my-task.md (see the cheat sheet)"
} else {
  Write-Host "Workspace reset: the calculator project is back to its seed state."
}
