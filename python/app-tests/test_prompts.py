"""The texts the model reads: the tool descriptions (TODO 1, 2 and 4) and the role prompts (TODO 3, 5 and 6).
No test can tell whether a text is good; these only check that it's written and keeps the
one rule code depends on. Whether it works shows in a real run."""
from explain import explain
from agent import prompts
from tools import tool_schemas


def descriptions(tool):
    """The tool's description, then each parameter's: "name: description"."""
    return [f"description: {tool['description']}"] + [
        f"{name}: {schema['description']}" for name, schema in tool["input_schema"]["properties"].items()]


def expect_described(tool, todo):
    stubs = [d for d in descriptions(tool) if not d.split(": ", 1)[1].strip() or d.split(": ", 1)[1].startswith("TODO")]
    assert not stubs, explain(
        f"tool_schemas.{tool['name'].upper()}: write the tool's description and each parameter's (TODO {todo}). The "
        "model knows the tool only from these texts.", "; ".join(stubs))


def test_todo1_description_read_file():
    expect_described(tool_schemas.READ_FILE, 1)


def test_todo2_description_write_file():
    expect_described(tool_schemas.WRITE_FILE, 2)


def test_todo4_description_run_tests():
    expect_described(tool_schemas.RUN_TESTS, 4)


def expect_written(prompt, name, todo):
    assert prompt.strip() and not prompt.startswith("TODO"), explain(
        f"prompts.{name.upper()} is still the placeholder: write the {name}'s system prompt (TODO {todo}).", prompt)


def test_todo3_prompt_coder():
    expect_written(prompts.CODER, "Coder", 3)


def test_todo5_prompt_planner():
    expect_written(prompts.PLANNER, "Planner", 5)


def test_todo6_prompt_reviewer():
    expect_written(prompts.REVIEWER, "Reviewer", 6)
    assert "APPROVED" in prompts.REVIEWER and "CHANGES_REQUIRED" in prompts.REVIEWER, explain(
        "The Reviewer prompt must name both verdicts, APPROVED and CHANGES_REQUIRED, and say the verdict goes alone "
        "on the first line: that line is all parse_verdict reads (TODO 6).", prompts.REVIEWER)
    assert prompts.REVIEWER not in (prompts.CODER, prompts.PLANNER), explain(
        "Each role needs its own prompt: the Reviewer's is the same text as another role's (TODO 6).", prompts.REVIEWER)
