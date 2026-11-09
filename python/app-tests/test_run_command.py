"""TODO 4. run_tests and run_command are provided, but they shorten their output with your
truncate, so their tests pass once that is done. They run real commands as the runner
user, so they only pass inside the workshop container."""
import time

from explain import explain
from support import config
from tools import tools


def test_todo4_run_tests_runs_the_test_command(monkeypatch):
    monkeypatch.setattr(config, "TEST_COMMAND", "echo TESTS-RAN; exit 1")

    result = tools.execute_tool("run_tests", {})

    assert not result.is_error and result.output.startswith("exit code 1") and "TESTS-RAN" in result.output, explain(
        "run_tests runs config.TEST_COMMAND and returns its exit code and output; failing tests are a normal "
        "result the model reads, not an error.", result)


def test_todo4_run_command_reports_exit_code_and_output():
    result = tools.run_command("echo out; echo err >&2; exit 3")

    assert not result.is_error, explain(
        "A non-zero exit code is a normal result, not an error: is_error should be False, so the model can read "
        "failing test output and fix the code.",
        result)
    assert result.output.startswith("exit code 3"), explain(
        'The output should start with "exit code 3" (the command ran "exit 3"), followed by what it printed.',
        result)
    assert "out" in result.output and "err" in result.output, explain(
        'The output should contain both stdout ("out") and stderr ("err"): merge stderr into stdout '
        "(stderr=subprocess.STDOUT).",
        result)


def test_todo4_run_command_runs_in_workspace_as_runner():
    result = tools.run_command("pwd; whoami")

    assert config.WORKSPACE in result.output, explain(
        f"Commands should run in the workspace ({config.WORKSPACE}): pass cwd=config.WORKSPACE.",
        result)
    assert "runner" in result.output, explain(
        "Commands should run as the runner user, not as agent: start them with sudo -u runner timeout ... /bin/sh -c <command>.",
        result)


def test_todo4_run_command_kills_command_and_children_on_timeout():
    started = time.monotonic()
    result = tools.run_command("sleep 100 & sleep 100")
    seconds = time.monotonic() - started

    assert result.is_error, explain(
        "A command that runs too long should come back as an error result: GNU timeout exits with 124 "
        "(or 137 if it had to kill), so treat those exit codes as a timeout.",
        result)
    assert config.COMMAND_TIMEOUT_S - 1 <= seconds <= config.COMMAND_TIMEOUT_S + 3, explain(
        f"It should give up after about config.COMMAND_TIMEOUT_S ({config.COMMAND_TIMEOUT_S} s), but it took "
        f"{seconds:.1f} s. Run it under timeout -k 2 {config.COMMAND_TIMEOUT_S}, and don't wait for the output "
        "pipes forever.")
    left = tools.run_command("pgrep -u runner -fx 'sleep 100' || echo none-left")
    assert "none-left" in left.output, explain(
        'No "sleep 100" may still be running afterwards: GNU timeout kills the command\'s whole process group, '
        "background children included.",
        left)


def test_todo4_run_command_truncates_long_output():
    result = tools.run_command("seq 1 20000")
    length = len(result.output)

    assert "[truncated" in result.output, explain(
        "Long output should go through truncate, which puts a [truncated ...] marker in the middle.",
        result)
    assert config.OUTPUT_LIMIT <= length <= config.OUTPUT_LIMIT + 100, explain(
        f"The output should be about config.OUTPUT_LIMIT ({config.OUTPUT_LIMIT}) characters long, but it is {length}.")
    assert result.output.startswith("exit code 0\n1\n2\n") and result.output.endswith("19999\n20000\n"), explain(
        'The output should be "exit code 0", a newline, then the truncated text: the start and the end of what '
        "the command printed must both survive.",
        result)


def test_todo4_run_command_environment_has_no_real_key():
    result = tools.run_command("env")

    assert "sk-ant" not in result.output and "ANTHROPIC_API_KEY" not in result.output, explain(
        "The command's environment must not contain the API key: run it with sudo -u runner, which starts from a "
        "clean environment, rather than passing yours on.",
        result)


def test_todo4_run_command_cannot_read_agent_source():
    result = tools.run_command("cat /home/agent/app/main.py")

    assert "exit code 0" not in result.output and "Permission denied" in result.output, explain(
        "The runner user must not be able to read /home/agent. If this worked, the command ran as agent: "
        "start it with sudo -u runner.",
        result)


def test_todo4_truncate_keeps_first_2000_and_last_8000():
    text = numbered_lines(5000)  # 30,000 characters: "00000\n00001\n...04999\n"

    result = tools.truncate(text)

    assert result.startswith(text[:2000]), explain(
        "truncate should keep the first 2,000 characters unchanged, so the result should start with "
        f"{describe(text[:2000])}.",
        describe(result))
    assert "\n02500\n" not in result, explain(
        "The middle of the text should be left out, but your result still contains line 02500 from the middle. "
        "If your second part is text[8000:], that is everything from index 8,000 to the end; the last 8,000 "
        "characters are text[-8000:].",
        describe(result))
    assert result.endswith(text[-8000:]), explain(
        "truncate should end with the last 8,000 characters: text[-8000:], so "
        f"{describe(text[-8000:])}. Careful: text[8000:] is everything from index 8,000 to the end.",
        describe(result))
    assert "[truncated" in result, explain(
        "Put a [truncated ...] marker between the two parts, so the model knows something was left out.",
        describe(result))
    assert 10_000 <= len(result) <= 10_100, explain(
        "The result should be the 2,000 + 8,000 characters you kept plus the marker: about config.OUTPUT_LIMIT "
        f"({config.OUTPUT_LIMIT}) characters.",
        describe(result))


def test_todo4_truncate_only_truncates_text_over_the_limit():
    at_limit = "x" * config.OUTPUT_LIMIT
    over_limit = at_limit + "y"

    short_result = tools.truncate("short")
    at_limit_result = tools.truncate(at_limit)
    over_limit_result = tools.truncate(over_limit)

    assert short_result == "short", explain("Short text should come back unchanged.", short_result)
    assert at_limit_result == at_limit, explain(
        f"Text of exactly config.OUTPUT_LIMIT ({config.OUTPUT_LIMIT}) characters should come back unchanged: only "
        "longer text is truncated.",
        describe(at_limit_result))
    assert "[truncated" in over_limit_result, explain(
        f"Text of config.OUTPUT_LIMIT + 1 ({config.OUTPUT_LIMIT + 1}) characters is over the limit and should be "
        "truncated.",
        describe(over_limit_result))


def numbered_lines(count):
    """ "00000\\n00001\\n..." so any piece of it shows where in the text it came from."""
    return "".join(f"{i:05d}\n" for i in range(count))


def describe(text):
    """A long string as its length plus how it starts and ends."""
    escaped = lambda s: s.replace("\n", "\\n")
    if len(text) <= 60:
        return f'"{escaped(text)}" ({len(text)} characters)'
    return f'"{escaped(text[:24])}…{escaped(text[-24:])}" ({len(text)} characters)'
