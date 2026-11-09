# Build Your Own Coding Agent

In this workshop you build a small coding agent from scratch: a **Planner** that reads the code and makes a plan, a **Coder** that edits files and runs tests, and a **Reviewer** that checks the work and sends it back if it isn't good enough.

```text
Planner ──▶ Coder ──▶ [tests + git diff] ──▶ Reviewer
              ▲                                 │
              └──── CHANGES_REQUIRED (max 2) ───┤
                                                └── APPROVED ──▶ done
```

You write the tools the model uses (`read_file` and `write_file`; `run_tests`, `find_files` and `search_text` come ready-made), decide which role may use which, the loop that lets a model call them, and the prompts that turn that one loop into a Planner, a Coder and a Reviewer. No agent framework: just the official Anthropic SDK and your own code, so you can see exactly what happens.

The agent works on a small calculator project inside a container. It has a bug, and the Reviewer is strict.

## Pick a track

All three tracks build the same agent, with the same files, the same numbered TODOs and the same output. Pick the language you're most comfortable in and open only that folder.

| Track | Folder | You need to know |
| --- | --- | --- |
| C# (.NET 10) | `csharp/` | async/await |
| Python 3.12 | `python/` | functions and dicts |
| Java 21 (Maven) | `java/` | records and builders |

## Setup (before the workshop)

1. Install Docker Desktop (or another Docker engine) and either VS Code with the **Dev Containers** extension or a JetBrains IDE with Dev Container support.
2. Clone this repository.
3. Download your track's image from your track folder (a few minutes; it holds the SDK and every package, so do it on a good connection):

   ```sh
   docker compose pull
   ```

   Opening the dev container downloads it too if it's missing, but doing it now gets everything before the day, and the setup check needs it. If the download fails, build the image yourself instead: `docker compose -f compose.yaml -f compose.build.yaml build`.
4. Run the setup check from your track folder:
   - macOS / Linux: `./check-setup.sh`
   - Windows: `.\check-setup.ps1`. If PowerShell says running scripts is disabled, use `powershell -ExecutionPolicy Bypass -File .\check-setup.ps1` (the same works for the other `.ps1` scripts).

   Every line should say PASS. If one doesn't, reply to the setup email with the output. No API key is needed yet: you get it on the day.
5. Open the repository folder in VS Code, choose **Reopen in Container** and pick your track (`coding-agent-csharp`, `-python` or `-java`). Opening just your track folder works too. Then open `app/coding-agent.code-workspace` (VS Code may offer **Open Workspace** right away; otherwise click the file, then **Open Workspace** at the bottom right): you see a read-only view of `workspace`, the project the agent works on, next to your code. In a JetBrains IDE, open `<track>/.devcontainer/devcontainer.json` as a Dev Container.
   On Windows, if starting the container fails with "accessing specified distro mount service", turn off the VS Code setting **Dev › Containers: Mount Wayland Socket** and try again.

## During the workshop

**Your API key.** On the day, once the tests for TODO 1-3 pass (your cheat sheet has the command), you get an API key: from then on `run-agent --coder` works with the real model. On your machine (not in the container), copy `.env.example` to `.env` in your track folder, paste the key after `ANTHROPIC_API_KEY=`, and run `docker compose up -d proxy` there. The API key only ever lives in that small proxy container. The container you work in, and every command the agent runs, never sees it.

Everything in this section, plus where each TODO lives and the SDK calls you need, is in your track's `app/CHEATSHEET.md`: open it inside the container (right-click, Open Preview), no second window needed.

Inside the container (the VS Code terminal opens in your `app` folder):

| Command | What it does |
| --- | --- |
| `run-tests` | Runs the tests for every TODO, no API key needed (TODO 3 and 5-7 use a scripted model). Tests for TODOs you haven't done yet fail with "TODO n". When everything is green, you're ready for the real model. |
| `run-agent --coder` | Runs only the Coder on the task (after TODO 1-3). |
| `run-agent --plan` | Runs the Planner, then the Coder (after TODO 1-5). |
| `run-agent` | The whole agent: Planner, Coder, Reviewer, revisions (after TODO 1-7). |
| `reset-workspace` | Puts the calculator project back to its starting state. Your code and logs stay. `reset-workspace --empty` leaves an empty project instead, for giving the agent tasks of your own: `run-agent my-task.md`, or without a file `run-agent --task "Make it an interactive command-line app"`. A `## Review` section at the end of a task file gives the Reviewer its criteria, as in `task.md`. |
| `in-workspace <command>` | Runs a command in `/workspace` the way the agent does (as user `runner`), e.g. its tests or a program it wrote. Use it instead of running things there yourself. |

Each TODO is marked in the code with `TODO n:` and a one-line hint. `app/CHEATSHEET.md` (open it in the container) lists where each one is and the SDK calls you need.

On your own machine (a terminal in your track folder, not in the container):

| Command | What it does |
| --- | --- |
| `./catch-up.sh step-2-coder` (or `.\catch-up.ps1 step-2-coder`) | Fell behind? Saves your changes in a stash and switches to a branch where the TODOs so far are done. |
| `./reset-workspace.sh` (or `.\reset-workspace.ps1`), `--empty` (`-Empty`) for an empty project | The same as `reset-workspace` in the container, from your own machine. If the container isn't running, it starts it with a fresh workspace. |

Catch-up branches: `step-1-file-tools` (TODO 1-2), `step-2-coder` (1-4), `step-3-planner` (1-5), `step-4-reviewer` (all).

## Logs

Every run prints a compact, colored trace and writes every step as one JSON line to `/home/agent/logs/run-<time>.jsonl` inside the container: each model call with its token counts, each tool call and result, each verdict, and a summary. Open it with `code /home/agent/logs/` or `tail` it to see exactly what your loop did.
