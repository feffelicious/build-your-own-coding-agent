# Works in Windows PowerShell 5.1 and PowerShell 7.
# Checks that this machine is ready for the workshop. Prints PASS or FAIL per check.
Set-Location $PSScriptRoot
$track = @{}
Get-Content track.env | Where-Object { $_ -match '^\s*([A-Z_]+)=(.*)$' } | ForEach-Object {
  $track[$Matches[1]] = $Matches[2].Trim().Trim("'").Trim('"')
}
$script:failures = 0
function Pass($name) { Write-Host "PASS  $name" }
function Fail($name, $hint) {
  Write-Host "FAIL  $name"
  if ($hint) { Write-Host "      $hint" }
  $script:failures++
}

docker info *> $null
if ($LASTEXITCODE -eq 0) { Pass "Docker is running" } else {
  Fail "Docker is running" "Start Docker Desktop (or your Docker engine) and run this again."
  Write-Host "1 check failed."; exit 1
}

$image = docker compose config --images 2>$null | Where-Object { $_ -match 'coding-agent' } | Select-Object -First 1
if ($image) { docker image inspect $image *> $null }
if ($image -and $LASTEXITCODE -eq 0) { Pass "Track image is downloaded" } else {
  Fail "Track image is downloaded" "Download it (a few minutes): docker compose pull"
  Write-Host "$script:failures check(s) failed."; exit 1
}

$up = docker compose up -d 2>&1
if ($LASTEXITCODE -eq 0) { Pass "Containers start" } else {
  Fail "Containers start" (($up | Select-Object -Last 5) -join "`n      ")
  Write-Host "$script:failures check(s) failed."; exit 1
}

$out = (docker compose exec -T -u runner -w /workspace workshop sh -c $track.TEST_COMMAND 2>&1) -join "`n"
if ($out -match $track.SEED_EXPECT) { Pass "Exercise tests run: exactly 1 failing test (the planted bug)" } else {
  Fail "Exercise tests run: exactly 1 failing test (the planted bug)" (($out -split "`n" | Select-Object -Last 5) -join "`n      ")
}

# Without a key (before the day) the API answers 401: that still proves Docker reaches it.
$hasKey = (Test-Path .env) -and (Select-String -Path .env -Pattern '^ANTHROPIC_API_KEY=.+' -Quiet)
$body = '{"model":"claude-haiku-5-5","max_tokens":16,"messages":[{"role":"user","content":"Reply with OK"}]}'
$code = $body | docker compose exec -T workshop curl -s -o /dev/null -w '%{http_code}' -X POST http://proxy:8080/v1/messages `
  -H 'content-type: application/json' -H 'anthropic-version: 2023-06-01' --data-binary '@-' 2>&1
if ($hasKey) {
  if ("$code" -eq "200") { Pass "API call through the proxy" } else {
    Fail "API call through the proxy" "HTTP status $code (401: wrong key in .env; 000 or 502: no internet from Docker)"
  }
} elseif ("$code" -eq "401") { Pass "API reachable through the proxy (no key yet: you get it on the day)" } else {
  Fail "API reachable through the proxy" "HTTP status $code (000 or 502: no internet from Docker)"
}

$envout = (docker compose exec -T workshop sudo -u runner timeout 10 env 2>&1) -join "`n"
if ($envout -match 'sk-ant') { Fail "Commands cannot see the real key" "runner's environment contains a key" } else {
  Pass "Commands cannot see the real key"
}

docker compose exec -T workshop sudo -u runner timeout 10 ls /home/agent *> $null
if ($LASTEXITCODE -eq 0) { Fail "Commands cannot read the agent's source" "/home/agent is readable by runner" } else {
  Pass "Commands cannot read the agent's source"
}

Write-Host ""
if ($script:failures -eq 0) { Write-Host "All checks passed. You are ready." } else {
  Write-Host "$script:failures check(s) failed. Reply to the setup email with this output."; exit 1
}
