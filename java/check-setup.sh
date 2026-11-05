#!/bin/sh
# Checks that this machine is ready for the workshop. Prints PASS or FAIL per check.
cd "$(dirname "$0")"
export MSYS_NO_PATHCONV=1   # Git Bash on Windows: keep /workspace as a container path
. ./track.env
failures=0
pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; [ -n "${2:-}" ] && printf '      %s\n' "$2"; failures=$((failures + 1)); }
in_box() { docker compose exec -T "$@"; }

if docker info >/dev/null 2>&1; then pass "Docker is running"; else
  fail "Docker is running" "Start Docker Desktop (or your Docker engine) and run this again."
  echo "1 check failed."; exit 1
fi

image=$(docker compose config --images 2>/dev/null | grep coding-agent | head -n 1)
if [ -n "$image" ] && docker image inspect "$image" >/dev/null 2>&1; then pass "Track image is downloaded"; else
  fail "Track image is downloaded" "Download it (a few minutes): docker compose pull"
  echo "$failures check(s) failed."; exit 1
fi

if docker compose up -d >/tmp/check-setup-up.log 2>&1; then pass "Containers start"; else
  fail "Containers start" "$(tail -n 5 /tmp/check-setup-up.log)"
  echo "$failures check(s) failed."; exit 1
fi

out=$(in_box -u runner -w /workspace workshop sh -c "$TEST_COMMAND" 2>&1)
if printf '%s' "$out" | grep -Eq "$SEED_EXPECT"; then pass "Exercise tests run: exactly 1 failing test (the planted bug)"; else
  fail "Exercise tests run: exactly 1 failing test (the planted bug)" "$(printf '%s' "$out" | tail -n 5)"
fi

# Without a key (before the day) the API answers 401: that still proves Docker reaches it.
has_key=no
if [ -f .env ] && grep -Eq '^ANTHROPIC_API_KEY=.+' .env; then has_key=yes; fi
code=$(in_box workshop curl -s -o /dev/null -w '%{http_code}' -X POST http://proxy:8080/v1/messages \
  -H 'content-type: application/json' -H 'anthropic-version: 2023-06-01' \
  -d '{"model":"claude-haiku-5-5","max_tokens":16,"messages":[{"role":"user","content":"Reply with OK"}]}' 2>&1)
if [ "$has_key" = yes ]; then
  if [ "$code" = "200" ]; then pass "API call through the proxy"; else
    fail "API call through the proxy" "HTTP status $code (401: wrong key in .env; 000 or 502: no internet from Docker)"
  fi
elif [ "$code" = "401" ]; then pass "API reachable through the proxy (no key yet: you get it on the day)"; else
  fail "API reachable through the proxy" "HTTP status $code (000 or 502: no internet from Docker)"
fi

envout=$(in_box workshop sudo -u runner timeout 10 env 2>&1)
if printf '%s' "$envout" | grep -q 'sk-ant'; then fail "Commands cannot see the real key" "runner's environment contains a key"; else
  pass "Commands cannot see the real key"
fi

if in_box workshop sudo -u runner timeout 10 ls /home/agent >/dev/null 2>&1; then
  fail "Commands cannot read the agent's source" "/home/agent is readable by runner"
else pass "Commands cannot read the agent's source"; fi

echo
if [ "$failures" -eq 0 ]; then echo "All checks passed. You are ready."; else
  echo "$failures check(s) failed. Reply to the setup email with this output."; exit 1; fi
