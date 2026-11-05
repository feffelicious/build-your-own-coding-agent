#!/bin/sh
# Usage: ./catch-up.sh <branch>   e.g. ./catch-up.sh step-2-coder
# Saves your uncommitted work in a labeled stash, then switches to the branch.
set -eu
branch="${1:-}"
if [ -z "$branch" ]; then
  echo "Usage: $0 <branch>   (step-1-file-tools, step-2-coder, step-3-planner, step-4-reviewer, main)" >&2
  exit 2
fi
cd "$(dirname "$0")"
git fetch --quiet origin "$branch" 2>/dev/null || true
if ! git rev-parse --verify --quiet "refs/heads/$branch" >/dev/null    && ! git rev-parse --verify --quiet "refs/remotes/origin/$branch" >/dev/null; then
  echo "There is no branch called $branch. Nothing was changed." >&2
  echo "Branches: step-1-file-tools, step-2-coder, step-3-planner, step-4-reviewer, main, main-2h" >&2
  exit 2
fi
label="catch-up: work before $branch, $(date '+%H:%M:%S')"
stashed=""
if [ -n "$(git status --porcelain)" ]; then
  git stash push --include-untracked --quiet -m "$label"
  stashed="$(git rev-parse stash@{0})"
fi
if git rev-parse --verify --quiet "refs/heads/$branch" >/dev/null; then
  git checkout --quiet "$branch"
  if git rev-parse --verify --quiet "refs/remotes/origin/$branch" >/dev/null \
     && ! git merge --quiet --ff-only "origin/$branch" 2>/dev/null; then
    echo "Note: your local $branch differs from origin/$branch; kept your local version."
  fi
else
  git checkout --quiet -b "$branch" --track "origin/$branch"
fi
echo "You are now on $branch."
if [ -n "$stashed" ]; then
  echo "Your previous changes are saved in a stash named: $label"
  echo "To get them back later:  git stash apply $stashed"
fi
