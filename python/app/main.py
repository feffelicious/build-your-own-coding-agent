"""Usage: run-agent [--coder | --plan] [task-file | --task "text"]
  (no flag)  Planner, Coder and Reviewer with revisions (needs TODO 1-7)
  --coder    the Coder on its own (needs TODO 1-3)
  --plan     the Planner, then the Coder once (needs TODO 1-5)
Exit codes: 0 approved or finished, 1 not approved, 2 TODO not implemented,
3 spend limit reached, 4 other API error."""
import os
import sys

import anthropic

from support import log
from agent import orchestrator


def main(argv):
    mode = next((a for a in argv if a.startswith("--")), "")
    task_file = next((a for a in argv if not a.startswith("--")), "task.md")
    if mode not in ("", "--coder", "--plan"):
        print("Usage: run-agent [--coder | --plan] [task-file | --task \"text\"]   (default task file: task.md)", file=sys.stderr)
        return 2
    if not os.path.isfile(task_file):
        print(f"Task file not found: {task_file}", file=sys.stderr)
        return 2
    # Everything above a "## Review" line is the task; the lines below it are the review
    # criteria, which only the Reviewer gets. No such line: no criteria.
    with open(task_file, encoding="utf-8") as f:
        lines = f.read().splitlines()
    split = next((i for i, line in enumerate(lines) if line.strip().lower() == "## review"), -1)
    task = "\n".join(lines if split < 0 else lines[:split]).strip()
    criteria = "" if split < 0 else "\n".join(lines[split + 1:]).strip()

    log.start()
    try:
        if mode == "--coder":
            log.transition("start", "coder", 0)
            orchestrator.code(task, "(no plan: the Coder is running on its own)", None)
            log.transition("coder", "end", 0)
            log.run_summary(None, 0)
            return 0
        if mode == "--plan":
            log.transition("start", "planner", 0)
            plan = orchestrator.plan(task)
            log.transition("planner", "coder", 0)
            orchestrator.code(task, plan, None)
            log.transition("coder", "end", 0)
            log.run_summary(None, 0)
            return 0
        return 0 if orchestrator.run(task, criteria) else 1
    except NotImplementedError as e:
        print(f"\nNot implemented yet: {e}", file=sys.stderr)
        return 2
    except anthropic.BadRequestError as e:
        if "usage limit" not in str(e).lower():
            print(f"\nAPI error: {e}", file=sys.stderr)
            return 4
        print("\nSpend limit reached for the workshop API key. Tell the facilitator.", file=sys.stderr)
        return 3
    except anthropic.APIError as e:
        print(f"\nAPI error: {e}", file=sys.stderr)
        return 4


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
