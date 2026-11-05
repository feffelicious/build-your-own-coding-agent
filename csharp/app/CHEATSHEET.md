# Cheat sheet: C#

You build a Planner â Coder â Reviewer agent: the tools it uses (`read_file` and `write_file`; `run_tests`, `glob` and `grep` are provided), which role gets which tools, the loop that lets the model call them, the prompts that turn that one loop into three different roles, and the code that decides who goes next. The model never runs anything itself; it asks, your code runs the tool and sends back the result.

## Run and test (in the container, in `app/`)

Files: `Tools/` (TODO 1, 2 and 4: tool code and descriptions), `Agent/` (TODO 3-7: loop, prompts, orchestration), `Support/` (config and logging, provided).

```sh
run-tests                  # all tests        run-tests --filter Todo1   only TODO 1
run-agent --coder          # Coder only (TODO 1-3)
run-agent --plan           # Planner + Coder (TODO 1-5)
run-agent                  # everything (TODO 1-7)
MAX_TOOL_CALLS=3 run-agent # any config value can be overridden like this
```

Every TODO has tests (for the prompts and tool descriptions they only check that you wrote them). TODO 3 and 5-7 run against a scripted model, so `run-tests` needs no API key. Once the tests for TODO 1-3 pass (`run-tests --filter "Todo1|Todo2|Todo3"`), your loop is ready for the real model: ask for your API key (README, "During the workshop"). Then your first real run:

1. `run-agent --coder`. Read the trace and the Coder's summary: does it know whether its code works?
2. Check it yourself: `in-workspace dotnet test`. The code may well be right; the point is that the Coder couldn't tell.
3. `reset-workspace`, so the next run starts from the same calculator.
4. TODO 4 gives the Coder `run_tests`. Then `run-agent --coder` again, and compare the two traces: what does it do now that it didn't before?

Caught up to `step-2-coder`? It already has `run_tests`: for the first run, take `ToolSchemas.RunTests` out of the Coder's tools in `Code`, and put it back for the second. `run-agent` needs all tests green.

## Debugging

Set a breakpoint, open **Run and Debug** (Ctrl+Shift+D), pick a configuration and press F5:

- **Debug tests** asks which tests to run (the same names as the `Test:` comments) and stops at your breakpoints in the code they call.
- **Debug agent: run-agent --coder**, **--plan** or **run-agent** runs the agent under the debugger; its trace appears in the terminal panel.

`run-tests` and `run-agent` typed in the terminal don't stop at breakpoints.

## Where the TODOs are

Each TODO method has a comment right above it with the command to check it: `Test:` runs just its tests. Copy it, run it, repeat until green.

| TODO | What | Where |
| --- | --- | --- |
| 1 | path check, `read_file`, its description | `Tools/Tools.cs`: `ResolveInWorkspace`, `ReadFile`; `Tools/ToolSchemas.cs`: `ReadFile` |
| 2 | `write_file`, its description | `Tools/Tools.cs`: `WriteFile`; `Tools/ToolSchemas.cs`: `WriteFile` |
| 3 | the tool loop, the Coder, its tools and its prompt | `Agent/AgentLoop.cs`: `RunAgent`; `Agent/Orchestrator.cs`: `Code`; `Agent/Prompts.cs`: `Coder` |
| 4 | truncation; describe the provided `run_tests` and give it to the Coder | `Tools/Tools.cs`: `Truncate` (read `RunTests` and `RunCommand`); `Tools/ToolSchemas.cs`: `RunTests`; `Agent/Orchestrator.cs`: `Code` |
| 5 | Planner step, its tools and its prompt | `Agent/Orchestrator.cs`: `Plan`; `Agent/Prompts.cs`: `Planner` |
| 6 | tests + diff, Reviewer, its tools and its prompt, verdict | `Agent/Orchestrator.cs`: `ReviewChanges`, `ParseVerdict`; `Agent/Prompts.cs`: `Reviewer` |
| 7 | revision loop | `Agent/Orchestrator.cs`: `Run` |

## Writing the prompts and tool descriptions (TODO 1-6)

The model knows a tool only from its description, and a role only from its system prompt: the same loop becomes a Planner, a Coder or a Reviewer through what you write. The tests can only check that a text is there; whether it works shows in a real run. Each TODO's hint lists what to cover. Short and concrete beats long and polite.

A role prompt says:

- **Who it is**, and where it sits: Planner, then Coder, then Reviewer.
- **What it gets and what it can do**: its tools, and what it can't (the Planner and the Reviewer only read).
- **Nobody answers questions.** No one is there during a run. Without this line a model may ask for confirmation, and wait for an answer that never comes.
- **When it's done, and what it replies with.** Code reads part of the reply: the Reviewer's verdict must be alone on the first line, exactly `APPROVED` or `CHANGES_REQUIRED` (`ParseVerdict` reads nothing else).
- **For the Coder: what feedback means.** In a revision round the workspace already holds its previous attempt. Untold, it may take its own changes for someone else's.

Which tools a role gets is up to your code, not its prompt: each role gets its own list (TODO 3, 5 and 6), and your loop refuses any tool that isn't on it. A prompt that says "don't change files" is a request; a role without `write_file` can't.

A tool description says what the tool does, what comes back, and what the model must know to call it right: paths are relative to the workspace root, `write_file` replaces the whole file, `run_tests` returns the exit code and the test output. `Glob` and `Grep` in `Tools/ToolSchemas.cs` are finished examples.

After your first real run, read the trace: a tool used wrongly or a role that wanders off usually points to a missing sentence. Change it and run again. Stuck? The catch-up branches have ours: on your machine, `git fetch` and `git show origin/step-4-reviewer:csharp/app/Agent/Prompts.cs`.

## The loop in plain words (TODO 3)

```text
messages = [ user: userMessage ]
loop:
    response = model(system prompt, tools, ALL of messages)    ← the model remembers nothing
    messages += [ assistant: response.content ]                ← exactly as received, every block
    if response.stop_reason != "tool_use": return its text
    results = for each tool_use block: run the tool → tool_result(same id, output, is_error)
              (a tool this role wasn't given: don't run it, send an error result)
    messages += [ user: results ]                              ← ALL results in ONE message
```

After two tool rounds, `messages` is: user, assistant (tool_use), user (tool_result), assistant (tool_use), user (tool_result). Count tool calls and stop at `MAX_TOOL_CALLS`. `RawLoop.md` shows the same loop in plain HTTP, without the SDK.

## The five SDK calls

```csharp
// 1. Create a client (provided in Agent/AgentLoop.cs; reads ANTHROPIC_API_KEY and ANTHROPIC_BASE_URL)
AnthropicClient client = new() { MaxRetries = 4 };

// 2. Send a message
var response = await client.Messages.Create(new MessageCreateParams {
    Model = Config.Model, MaxTokens = Config.MaxTokens, System = systemPrompt,
    Tools = tools, Messages = messages, CacheControl = new CacheControlEphemeral() });

// 3. Find tool calls
foreach (var block in response.Content)
    if (block.TryPickToolUse(out ToolUseBlock? toolUse)) { /* toolUse.ID, .Name, .Input */ }
// response.StopReason == StopReason.ToolUse means "run my tools and come back"

// 4. Build a tool result
new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = output, IsError = isError }

// 5. Send results back: the assistant turn first, then all results in one user message
messages.Add(new() { Role = Role.Assistant, Content = AgentLoop.ToParams(response.Content) });
messages.Add(new() { Role = Role.User, Content = results });   // List<ContentBlockParam>
```

Tool input: `Args.String(toolUse.Input, "path")`.

## Logging (`Support/Log.cs`, provided)

The trace in your terminal and the JSON log in `/home/agent/logs` are how you see what your agent did. They only show what you log:

| Call | Where |
| --- | --- |
| `Log.ModelCall(role, messages.Count, response)` | TODO 3, after every `Messages.Create` |
| `Log.ToolCall(role, toolUse.Name, toolUse.Input)` | TODO 3, before running a tool |
| `Log.ToolResult(role, name, result.IsError, milliseconds, result.Output)` | TODO 3, after running it |
| `Log.CapReached(role, toolCalls)` | TODO 3, when you stop at the cap |
| `Log.Verdict(revision, verdict)` | TODO 6, after `ParseVerdict` |
| `Log.Transition(from, to, revision)` | TODO 7, at every hand-over, e.g. `("coder", "reviewer", 0)`; `"start"` and `"end"` mark the ends |
| `Log.Info(message)` | TODO 7, a note from the orchestrator, e.g. the revision limit |
| `Log.RunSummary(approved, revisions)` | TODO 7, once at the end: tokens, tool calls and cost per role |

## What the agent did (in the container)

To browse the agent's project, open `coding-agent.code-workspace`: VS Code may offer **Open Workspace** when the container starts; otherwise click the file in the Explorer, then **Open Workspace** at the bottom right (**File > Open Recent** brings it back). The Explorer then shows `workspace (read-only)` next to your code, always up to date, and **Source Control** lists every file the agent changed, with a side-by-side diff. It is read-only on purpose: your language extension builds whatever folder it sees, and build files made as you in `/workspace` would block the agent. Don't add `/workspace` itself to VS Code.

```sh
in-workspace git diff                  # its changes to the calculator project
ls -t /home/agent/logs                 # one JSONL log per run: every model call, tool call and verdict
tail -n 1 /home/agent/logs/run-*.jsonl # the summary of each run: tokens, tool calls, cost
in-workspace dotnet test               # run the agent's project yourself (any command works)
reset-workspace                        # the agent made a mess: the calculator back to its start
reset-workspace --empty                # an empty project for your own tasks (below)
```

Plain `git` refuses `/workspace` ("not a git repository"): it belongs to `runner`, and so do its git settings, which the agent's commands could change. Use `in-workspace git ...`; VS Code's Source Control runs git as `runner` too (`git-as-runner`, set as `git.path` in the dev container).

Run commands in `/workspace` with `in-workspace`, not directly: it runs them as `runner`, the user the agent's commands run as. Directly, as yourself, you get "Permission denied" on the agent's build output, and files you create there would block the agent later. In a terminal in the read-only view, `in-workspace` runs the command in the same folder of `/workspace`; without it, anything that writes (build output, caches) fails there. Longer than 10 minutes: `TIMEOUT=1800 in-workspace ...`.

`reset-workspace` keeps your code and your logs. Your own tasks: after `reset-workspace --empty`, write a task file in `app/`, e.g. `my-task.md`, then `run-agent my-task.md` (or with `--plan` or `--coder`). End it with a `## Review` section, one bullet per thing the Reviewer should check, like `task.md`; without one, the Reviewer checks against the task itself. A short task works without a file: `run-agent --task "Make it an interactive command-line app"` (on the calculator too). Set `TEST_COMMAND` if your project isn't tested with `dotnet test`.

## The Reviewer's extra rule (exercise)

`task.md` has two parts: the task, and below `## Review` the review criteria. Only the Reviewer gets the criteria, the way a developer gets a ticket but not the reviewer's checklist. Your first full `run-agent` is usually rejected, and that's on purpose: the last criterion asks for XML doc comments (`///`) on `multiply` and `divide`, and the task doesn't mention them, so the Coder rarely writes them unasked. Watch the Reviewer's feedback go back to the Coder, and the second round get approved: that is TODO 7's loop at work. (The Coder wrote them anyway and the first attempt was approved? Also fine. Never approved? Read the Reviewer's feedback: it says what's missing.)

Then play with it: take the criterion out, or move it up into the task so the Coder knows it from the start, and the first attempt is approved. Or add a criterion of your own, or change the Reviewer's prompt, then `reset-workspace` and `run-agent`.

## Done early? Pick an extra

Only once `run-agent` gets approved; finishing TODO 1-7 comes first. Pick whatever you like:

- **Cheapest approval (the shared challenge).** Create `challenge.md` in `app/` with this task:

  ```markdown
  Add Power(int baseValue, int exponent) to the Calculator. A negative exponent throws ArgumentOutOfRangeException with exactly "Exponent must not be negative". Power(2, 10) returns 1024 and Power(5, 0) returns 1. Add tests for these cases and for the exception. All tests, including existing ones, must pass. Do not modify existing tests.

  ## Review
  - no existing test was modified (check the diff)
  - the test exit code is 0
  ```

  Add the criteria for the new method under `## Review` first: as it stands, the Reviewer checks only the general ones. Then `reset-workspace` and `run-agent challenge.md`. Tune the prompts, the tool descriptions, the review criteria and config values (not the task) until an approved run is as cheap as you can get it: `estimated cost` in the summary line. Compare with the tables around you.
- **Your own task.** `reset-workspace --empty`, then `run-agent --task "..."`, e.g. "Write a small command-line todo list with tests". The Reviewer checks against the task; for criteria of your own, write a task file with a `## Review` section.
- **Give the Coder a shell: `run_command`.** It's provided (`Tools.RunCommand` and `ToolSchemas.RunCommand`), but no role has it. Add it to the Coder's tools in `Code` and mention it in the Coder prompt, then `reset-workspace` and `run-agent --coder`. Read the trace: what does it run besides the tests? Is the run cheaper or dearer? What could it do now that it couldn't with `run_tests`, and what keeps that in check (the `runner` user, the key in the proxy)?
- **Edit instead of rewrite: `edit_file`.** `write_file` sends the whole file every time: slow and expensive for a big file, and a model may drop lines on the way. Write `edit_file` with `path`, `old_text` and `new_text`: it replaces exactly one occurrence of `old_text`. If it occurs 0 times or more than once, return an error that says how many times, so the model can send more of the surrounding lines. You need a schema in `Tools/ToolSchemas.cs` (say when to use it rather than `write_file`), a case in `ExecuteTool` in `Tools/Tools.cs`, the code (with `ResolveInWorkspace`), and a place in the Coder's tools and prompt. Compare a run's tokens and cost with and without it.
- **Let the Reviewer run the tests itself** instead of getting their output in `ReviewChanges`: give it `run_tests` and change its prompt.

## On your own machine (a terminal in `csharp/`, not in the container)

| Command | What it does |
| --- | --- |
| `./catch-up.sh step-2-coder` (Windows: `.\catch-up.ps1 step-2-coder`) | Fell behind: stashes your changes and switches to a branch where the TODOs so far are done. It prints how to get your changes back. |
| `./reset-workspace.sh` (Windows: `.\reset-workspace.ps1`), `--empty` (`-Empty`) for an empty project | The same as `reset-workspace` in the container, from your own machine. If the container isn't running, it starts it with a fresh workspace. |

Catch-up branches: `step-1-file-tools` (TODO 1-2), `step-2-coder` (1-4), `step-3-planner` (1-5), `step-4-reviewer` (all). Started on `main-2h` (TODO 5-7 prebuilt)? Catch up only to `step-4-reviewer`: the other branches have TODO 5-7 open again.

## Common errors

1. **ResolveInWorkspace returns a path under the test or bin folder.** You resolved the model's relative path against the current directory. Combine it with the workspace first: `Path.Combine(Config.Workspace, path)`.
2. **WriteFile says "File not found" for a new file.** You copied the `File.Exists` check from ReadFile. Writing creates the file: keep the path check and the size limit, drop the existence check.
3. **The cap is logged as 4, not 3.** `if (toolCalls++ == Config.MaxToolCalls)` compares 3 but leaves 4 for `Log.CapReached`. Compare with `toolCalls == Config.MaxToolCalls`, and do `toolCalls++` after the if.
4. **A path of exactly `..` gets through.** `relative.StartsWith("../")` misses it: there's no slash. Check `relative == ".."` as well.
5. **"APPROVED with two small notes" counts as approved.** `StartsWith("APPROVED")` lets it through, but the first line has to be exactly `APPROVED`. Decide with `ParseVerdict`, not with a check of your own.
6. **The run is never approved, though the Reviewer's feedback sounds happy.** The Reviewer answered "**APPROVED**" or "Verdict: APPROVED": `ParseVerdict` takes only a first line that is exactly `APPROVED`. Say so in the Reviewer prompt: the verdict alone on the first line, no formatting.
