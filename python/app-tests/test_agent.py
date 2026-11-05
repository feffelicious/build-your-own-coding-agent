"""TODO 3 to 7, against a scripted model (FakeModel), so no API key is needed.
Each test scripts what the model answers, then checks what your code sent."""
import json
import os
import shutil
import tempfile

import anthropic
import pytest

from agent import agent_loop, orchestrator, prompts
from explain import explain
from fake_model import FakeModel, essentials, text, tool_names
from support import config, log
from tools import tool_schemas, tools

# Any tool list will do for the loop's own tests.
SOME_TOOLS = [tool_schemas.READ_FILE, tool_schemas.WRITE_FILE, tool_schemas.GLOB]


@pytest.fixture
def model(monkeypatch):
    # A workspace runner can enter too (pytest's own tmp folders are private to agent).
    workspace = tempfile.mkdtemp(prefix="ws-")
    os.chmod(workspace, 0o755)
    with open(os.path.join(workspace, "notes.txt"), "w") as f:
        f.write("hello from the workspace")
    monkeypatch.setattr(config, "WORKSPACE", workspace)
    monkeypatch.setattr(config, "TEST_COMMAND", "echo TESTS-RAN")
    fake = FakeModel()
    monkeypatch.setattr(agent_loop, "client", anthropic.Anthropic(base_url=fake.base_url, api_key="test-key", max_retries=0))
    # A log file of its own per test, so a test can check what your code logged.
    log_dir = tempfile.mkdtemp(prefix="logs-")
    monkeypatch.setattr(config, "LOG_DIR", log_dir)
    monkeypatch.setattr(log, "_file", None)
    monkeypatch.setattr(log, "file_path", None)
    log.start()
    yield fake
    fake.close()
    log._file.close()
    if os.path.isdir(os.path.join(workspace, "repo")):
        config.WORKSPACE = workspace
        tools.run_command("rm -rf repo")  # runner made it, so runner removes it
    shutil.rmtree(workspace, ignore_errors=True)
    shutil.rmtree(log_dir, ignore_errors=True)


def test_todo3_run_agent_returns_the_final_text(model):
    model.says("All done.")

    result = agent_loop.run_agent("coder", "You are a test.", SOME_TOOLS, "Do the task.")

    assert result == "All done.", explain(
        "When the model stops asking for tools, run_agent should return its text: final_text(response).", result)
    assert len(model.requests) == 1, explain(
        f"With no tool calls, run_agent should call the model exactly once, but it called it {len(model.requests)} times.")
    request = model.requests[0]
    assert request.get("model") == config.MODEL and request.get("max_tokens") == config.MAX_TOKENS, explain(
        f'Send model=config.MODEL ("{config.MODEL}") and max_tokens=config.MAX_TOKENS ({config.MAX_TOKENS}).',
        f"model {request.get('model')}, max_tokens {request.get('max_tokens')}")
    assert text(request.get("system")) == "You are a test.", explain(
        "Send the system_prompt argument as system=.", text(request.get("system")))
    assert tool_names(request) == ["read_file", "write_file", "glob"], explain(
        "Send the tools you were given as tools=.", tool_names(request))
    messages = request.get("messages", [])
    assert len(messages) == 1 and messages[0].get("role") == "user" and text(messages[0].get("content")) == "Do the task.", explain(
        "A fresh conversation starts with one user message: the user_message argument.", messages)
    assert request.get("cache_control") is not None, explain(
        'Pass cache_control={"type": "ephemeral"}, so the growing conversation is cached between calls.')


def test_todo3_run_agent_runs_the_tool_and_sends_the_result_back(model):
    model.calls_tools(("read_file", {"path": "notes.txt"})).says("I read it.")

    result = agent_loop.run_agent("coder", "You are a test.", SOME_TOOLS, "Read notes.txt.")

    assert len(model.requests) == 2, explain(
        'After a reply with stop_reason "tool_use", run the tool and call the model again. Expected 2 calls, '
        f"got {len(model.requests)}.")
    assert result == "I read it.", explain("Return the text of the last reply, the one after the tool result.", result)
    messages = model.requests[1]["messages"]
    assert [m.get("role") for m in messages] == ["user", "assistant", "user"], explain(
        "The second call should send the whole conversation: your user message, the assistant turn, then a "
        "user message with the tool result.", [m.get("role") for m in messages])
    assert same_blocks(messages[1].get("content"), model.replies[0]), explain(
        "Append the assistant turn exactly as received, every block including the thinking block: "
        '{"role": "assistant", "content": response.content}.', messages[1].get("content"))
    results = tool_results(messages[2])
    tool_use_id = model.replies[0][1]["id"]
    assert len(results) == 1 and results[0].get("tool_use_id") == tool_use_id, explain(
        f"Answer the tool call with a tool_result whose tool_use_id is the call's id ({tool_use_id}).",
        messages[2].get("content"))
    assert "hello from the workspace" in text_of_result(results[0]) and not results[0].get("is_error"), explain(
        "The tool_result should carry what the tool returned (the text of notes.txt) and is_error False: run it "
        "with execute_tool(block.name, block.input).", results[0])
    logged = logged_events(None, lambda e: f"{e['event']}({e.get('role')})")
    assert logged == "model_call(coder), tool_call(coder), tool_result(coder), model_call(coder)", explain(
        "Log every step with the role you were given, so the trace shows it: log.model_call after each model call, "
        "log.tool_call before running a tool and log.tool_result after it.", f"log: {logged}")


def test_todo3_run_agent_sends_all_results_in_one_message(model):
    model.calls_tools(("read_file", {"path": "notes.txt"}), ("read_file", {"path": "missing.txt"})).says("ok")

    agent_loop.run_agent("coder", "You are a test.", SOME_TOOLS, "Read two files.")

    messages = model.requests[1]["messages"]
    assert len(messages) == 3, explain(
        "When the model asks for several tools in one turn, send ALL the results back in ONE user message, "
        "not one message per result.", [m.get("role") for m in messages])
    results = tool_results(messages[2])
    ids = [block["id"] for block in model.replies[0][1:]]
    assert [r.get("tool_use_id") for r in results] == ids, explain(
        f"There should be one tool_result per tool call, matched by id, in order ({', '.join(ids)}).",
        messages[2].get("content"))
    assert results[1].get("is_error") is True, explain(
        "A tool that fails (missing.txt doesn't exist) is still answered, with is_error True, so the model "
        "knows it went wrong.", results[1])


def test_todo3_run_agent_refuses_tools_it_was_not_given(model):
    model.calls_tools(("write_file", {"path": "sneaky.txt", "content": "should not be written"})).says("Tried.")

    agent_loop.run_agent("planner", "You are a test.", [tool_schemas.READ_FILE, tool_schemas.GLOB], "Read only.")

    results = tool_results(model.requests[1]["messages"][2])
    assert not os.path.exists(os.path.join(config.WORKSPACE, "sneaky.txt")), explain(
        "The model asked for write_file, which this role wasn't given, and your loop ran it. The tools you send "
        "only tell the model what it may use; your code has to enforce it: run a tool only if "
        "agent_loop.offers(tools, block.name).")
    assert len(results) == 1 and results[0].get("is_error") is True and "not one of your tools" in text(results[0].get("content")), explain(
        'Answer a tool the role wasn\'t given with an error result, "Refused: write_file is not one of your tools", '
        "so the model knows, instead of running it.", results)


def test_todo3_run_agent_stops_at_the_tool_call_cap(model, monkeypatch):
    monkeypatch.setattr(config, "MAX_TOOL_CALLS", 3)
    model.when_script_ends = lambda fake: fake.tool_calls(("read_file", {"path": "notes.txt"}))

    result = agent_loop.run_agent("coder", "You are a test.", SOME_TOOLS, "Keep reading.")

    assert result == "[stopped: more than 3 tool calls]", explain(
        "A model that never stops asking for tools must be stopped: after config.MAX_TOOL_CALLS tool calls, "
        'log.cap_reached and return "[stopped: more than N tool calls]" with N = config.MAX_TOOL_CALLS.', result)
    assert len(model.requests) == 4, explain(
        "With config.MAX_TOOL_CALLS = 3, run 3 tools (3 calls to the model), and stop when the 4th reply asks for "
        f"another: 4 calls in total, but there were {len(model.requests)}.")
    capped = logged_events("cap_reached", lambda e: f"{e['role']} {e['tool_calls']}")
    assert capped == "coder 3", explain(
        "Call log.cap_reached(role, tool_calls) before you return, so the trace shows why the agent stopped, with the "
        "number of tools that ran (3 here, config.MAX_TOOL_CALLS). Logged 4? Then tool_calls went up before "
        "log.cap_reached: compare tool_calls == config.MAX_TOOL_CALLS first, and do tool_calls += 1 after the if.",
        f"cap_reached logged (role, tool_calls): {capped}")


def test_todo3_run_agent_counts_tools_not_model_calls(model, monkeypatch):
    monkeypatch.setattr(config, "MAX_TOOL_CALLS", 3)
    model.when_script_ends = lambda fake: fake.tool_calls(("read_file", {"path": "notes.txt"}), ("read_file", {"path": "notes.txt"}))

    result = agent_loop.run_agent("coder", "You are a test.", SOME_TOOLS, "Keep reading, two at a time.")

    ran = len(logged_events("tool_call", lambda e: e["event"]).split(", "))
    assert result == "[stopped: more than 3 tool calls]" and ran == 3, explain(
        "The cap counts tools, not model calls: here each reply asks for two tools, so stop after the 3rd tool, in "
        "the middle of the 2nd reply. Count each tool you run (tool_calls += 1 per tool_use block).",
        f"result {result!r}, tools run: {ran}")


def test_todo3_code_sends_task_plan_and_feedback(model):
    model.says("Changed it.")

    summary = orchestrator.code("THE TASK", "THE PLAN", "THE FEEDBACK")

    assert len(model.requests) == 1, explain("code should call the model once here.", len(model.requests))
    request = model.requests[0]
    message = text(request["messages"][0].get("content"))
    assert summary == "Changed it.", explain("code should return what run_agent returns.", summary)
    assert text(request.get("system")) == prompts.CODER, explain(
        'code should call run_agent with "coder" and prompts.CODER as the system prompt.', text(request.get("system")))
    assert all(name in tool_names(request) for name in ["read_file", "write_file"]), explain(
        "The Coder needs at least tool_schemas.READ_FILE and WRITE_FILE (GLOB and GREP help it find code; run_tests "
        "follows in TODO 4).",
        tool_names(request))
    assert "THE TASK" in message and "THE PLAN" in message and "THE FEEDBACK" in message, explain(
        "The Coder's user message should contain the task, the plan and, when there is some, the reviewer's feedback.",
        message)

    model.says("Coded it.")
    orchestrator.code("THE TASK", "THE PLAN", None)

    first = text(model.requests[1]["messages"][0].get("content"))
    assert "THE TASK" in first and "THE PLAN" in first and "None" not in first, explain(
        "On the first attempt there is no feedback (feedback is None): send the task and the plan, and leave the "
        'feedback part out instead of sending "None".', first)


def test_todo4_code_can_run_the_tests(model):
    model.says("Changed it.")

    orchestrator.code("THE TASK", "THE PLAN", None)

    assert "run_tests" in tool_names(model.requests[0]), explain(
        "Give the Coder run_tests: add tool_schemas.RUN_TESTS to its tools in orchestrator.code (TODO 4). Without it "
        "the Coder can't check its own work.", tool_names(model.requests[0]))


# The tools that change files or run commands. A role that only reads gets none of them.
WRITERS = {"write_file", "edit_file", "run_command"}


def only_reads(request):
    return "read_file" in tool_names(request) and not WRITERS & set(tool_names(request))


def test_todo5_plan_asks_the_planner(model):
    model.says("1. Do it.")

    plan = orchestrator.plan("THE TASK")

    assert len(model.requests) == 1, explain("plan should call the model once here.", len(model.requests))
    request = model.requests[0]
    assert plan == "1. Do it.", explain("plan should return the Planner's answer.", plan)
    assert text(request.get("system")) == prompts.PLANNER, explain(
        'plan should call run_agent as "planner" with prompts.PLANNER as the system prompt.', text(request.get("system")))
    assert only_reads(request), explain(
        "The Planner only reads: give it tool_schemas.READ_FILE (GLOB and GREP help it search), and nothing that "
        "changes files or runs commands (write_file, run_command, an edit_file of your own).",
        tool_names(request))
    assert "THE TASK" in text(request["messages"][0].get("content")), explain(
        "The Planner's user message should contain the task.", text(request["messages"][0].get("content")))


def test_todo6_review_changes_gives_the_reviewer_everything(model):
    model.says("APPROVED\nAll good.")
    git_repo_with_a_new_file()

    review = orchestrator.review_changes("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0)

    assert len(model.requests) == 1, explain("review_changes should call the model once here.", len(model.requests))
    request = model.requests[0]
    message = text(request["messages"][0].get("content"))
    assert review.approved and review.text == "APPROVED\nAll good.", explain(
        "A review whose first line is APPROVED should come back as approved=True, with the full review text.", review)
    assert text(request.get("system")) == prompts.REVIEWER and only_reads(request), explain(
        'Call run_agent as "reviewer" with prompts.REVIEWER and tools that only read: tool_schemas.READ_FILE (GLOB '
        "and GREP help), nothing that changes files or runs commands. run_tests is fine.",
        tool_names(request))
    assert "THE TASK" in message and "THE PLAN" in message and "THE SUMMARY" in message, explain(
        "The Reviewer's message should contain the task, the plan and the Coder's summary.", message)
    assert "THE CRITERIA" in message, explain(
        "The Reviewer's message should contain the review criteria (the criteria parameter): they say what to check.",
        message)
    assert "TESTS-RAN" in message, explain(
        "The Reviewer should get the real test output: run the tests with run_tests() and include its output "
        '(the test sets config.TEST_COMMAND to "echo TESTS-RAN").', message)
    assert "NEW-FILE-CONTENT" in message, explain(
        'The Reviewer should also get the git diff, including new files: run "git add -N . && git diff" with '
        "run_command and include its output. (Plain git diff leaves out files git doesn't track yet, like the new "
        "test file the Coder wrote here.)", message)
    assert "is_error" not in message, explain(
        "Give the Reviewer the commands' output text (result.output), not the ToolResult itself: the message "
        'contains "ToolResult(output=..., is_error=...)".', message)


def test_todo6_review_changes_uses_parse_verdict(model):
    model.says("APPROVED with two small notes\n- naming").says("\n  APPROVED\nAll good.")

    chatty = orchestrator.review_changes("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0)
    padded = orchestrator.review_changes("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 1)

    assert not chatty.approved, explain(
        'The Reviewer answered "APPROVED with two small notes": its first line isn\'t exactly APPROVED, so that\'s '
        "CHANGES_REQUIRED. Decide with parse_verdict(text), not with your own check (startswith accepts this).", chatty)
    assert padded.approved, explain(
        'The Reviewer answered with a blank line, then "  APPROVED": parse_verdict trims that, so it\'s approved. '
        "Decide with parse_verdict(text), not with your own check.", padded)
    verdicts = logged_events("verdict", lambda e: f"{e['revision']} {e['verdict']}")
    assert verdicts == "0 CHANGES_REQUIRED, 1 APPROVED", explain(
        "Log each verdict so the trace shows it: log.verdict(revision, verdict) with what parse_verdict returned.",
        f"verdicts logged: {verdicts}")


def test_todo6_review_changes_changes_required_is_not_approved(model):
    model.says("CHANGES_REQUIRED\n- divide truncates")

    review = orchestrator.review_changes("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0)

    assert not review.approved and review.text == "CHANGES_REQUIRED\n- divide truncates", explain(
        "A CHANGES_REQUIRED review should come back as approved=False, with the full review text: the Coder gets "
        "it as feedback.", review)


def test_todo7_run_revises_until_approved(model):
    model.says("1. THE PLAN").says("Coded it.").says("CHANGES_REQUIRED\n- REVIEW-ONE").says("Fixed it.").says("APPROVED")

    approved = orchestrator.run("THE TASK", "THE CRITERIA")

    roles = " → ".join(role_of(r) for r in model.requests)
    assert roles == "planner → coder → reviewer → coder → reviewer", explain(
        "run should plan once, then Coder and Reviewer, and after CHANGES_REQUIRED the Coder and Reviewer again.", roles)
    assert approved is True, explain("run should return True once the Reviewer approves.", approved)
    second_coder = text(model.requests[3]["messages"][0].get("content"))
    assert "REVIEW-ONE" in second_coder and "THE PLAN" in second_coder, explain(
        "The second Coder run should get the plan and the full review as feedback.", second_coder)
    saw_criteria = " → ".join(
        f"{role_of(r)}:{'yes' if 'THE CRITERIA' in text(r['messages'][0].get('content')) else 'no'}"
        for r in model.requests)
    assert saw_criteria == "planner:no → coder:no → reviewer:yes → coder:no → reviewer:yes", explain(
        "Only the Reviewer gets the review criteria: pass criteria to review_changes, not to plan or code.",
        saw_criteria)
    expect_flow_logged(approved=True)


def test_todo7_run_stops_at_the_revision_limit(model, monkeypatch):
    monkeypatch.setattr(config, "MAX_REVISIONS", 1)
    model.says("1. THE PLAN")
    model.when_script_ends = lambda fake: [{"type": "text", "text": "Coded it." if len(fake.requests) % 2 == 0
                                            else "CHANGES_REQUIRED\n- still wrong"}]

    approved = orchestrator.run("THE TASK", "THE CRITERIA")

    roles = " → ".join(role_of(r) for r in model.requests)
    assert approved is False, explain(
        "run should return False when the revision limit is reached without approval.", approved)
    assert roles == "planner → coder → reviewer → coder → reviewer", explain(
        "With config.MAX_REVISIONS = 1 the Coder runs twice (the first attempt plus one revision), then run stops.",
        roles)
    expect_flow_logged(approved=False)


# A git repo, made as runner (who runs git in the real workspace), with one committed file and
# one new, untracked file, as after the Coder added a test file.
def git_repo_with_a_new_file():
    os.chmod(config.WORKSPACE, 0o777)
    made = tools.run_command(
        "mkdir repo && cd repo && git init -q && echo old > a.txt && git add a.txt "
        "&& git -c user.name=t -c user.email=t@t commit -qm init && echo NEW-FILE-CONTENT > new_test.txt")
    assert "exit code 0" in made.output, made.output
    config.WORKSPACE = os.path.join(config.WORKSPACE, "repo")


# Both run tests take the same path: plan, code, review, code again, review again.
def expect_flow_logged(approved):
    transitions = logged_events("transition", lambda e: f"{e['from']}→{e['to']} {e['revision']}")
    assert transitions == ("start→planner 0, planner→coder 0, coder→reviewer 0, reviewer→coder 1, "
                           "coder→reviewer 1, reviewer→end 1"), explain(
        "Log each step of the flow with log.transition(from_, to, revision), as the steps in run list them.",
        f"transitions logged: {transitions}")
    summary = logged_events("run_summary", lambda e: f"approved {e['approved']}, revisions {e['revisions']}")
    assert summary == f"approved {approved}, revisions 1", explain(
        f"End the run with log.run_summary({approved}, revision), once.", f"run_summary logged: {summary}")


# The events of one kind (all when kind is None) in this test's log, each shown with show, comma-separated.
def logged_events(kind, show):
    with open(log.file_path, encoding="utf-8") as f:
        events = [json.loads(line) for line in f if line.strip()]
    return ", ".join(show(e) for e in events if kind is None or e["event"] == kind)


def tool_results(message):
    content = message.get("content")
    return [b for b in content if isinstance(b, dict) and b.get("type") == "tool_result"] if isinstance(content, list) else []


def text_of_result(result):
    return text(result.get("content"))


def same_blocks(sent, received):
    return (isinstance(sent, list) and len(sent) == len(received)
            and all(essentials(a) == essentials(b) for a, b in zip(sent, received)))


def role_of(request):
    system = text(request.get("system"))
    return {prompts.PLANNER: "planner", prompts.CODER: "coder", prompts.REVIEWER: "reviewer"}.get(system, "?")
