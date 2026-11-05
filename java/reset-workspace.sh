#!/bin/sh
# Puts /workspace back by running app/reset-workspace inside the running container, so
# your VS Code window stays connected. Your app code and your run logs are not touched.
# (In the container terminal, just run reset-workspace yourself.)
#   ./reset-workspace.sh           the calculator project, as at the start
#   ./reset-workspace.sh --empty   an empty project (a git repo with no files), for your own tasks
set -eu
cd "$(dirname "$0")"
. ./track.env
case "${1:-}" in
  "") empty="" ;;
  --empty) empty="yes" ;;
  *) echo "Usage: $0 [--empty]" >&2; exit 2 ;;
esac

if [ -n "$(docker compose ps -q --status running workshop 2>/dev/null)" ] \
  && docker compose exec -T workshop /home/agent/app/reset-workspace ${empty:+--empty}; then
  exit 0
fi

# The container isn't running, or the reset failed in it: start over with a new
# volume. This restarts the containers.
echo "Recreating the workspace volume (this restarts the containers)..."
docker compose down
docker volume rm "coding-agent-${TRACK}_workspace" >/dev/null 2>&1 || true
docker compose up -d
if [ -n "$empty" ]; then
  # A fresh volume holds only runner's seed files, so runner can clear it.
  docker compose exec -T workshop sudo -u runner timeout 120 sh -c \
    'cd /workspace && find . -mindepth 1 -delete && git init -q -b main \
     && git -c user.name="Workshop Seed" -c user.email=seed@workshop.invalid commit -q --allow-empty -m empty'
  echo "Workspace emptied: /workspace is an empty git repo. Give the agent a task of your own, e.g. run-agent my-task.md (see the cheat sheet)"
else
  echo "Workspace reset: the calculator project is back to its seed state."
fi
