"""Planner -> Coder -> [tests + diff] -> Reviewer, with at most MAX_REVISIONS
rounds of feedback. Plain code decides what happens next, not a model."""
from typing import NamedTuple

from support import config
from support import log
from agent import prompts
from tools import tool_schemas
from agent.agent_loop import run_agent
from tools.tools import run_command, run_tests


class Review(NamedTuple):
    approved: bool
    text: str


# Test: run-tests -k todo5_plan
# Then try it: reset-workspace, then run-agent --plan
def plan(task):
    return run_agent("planner", prompts.PLANNER,
                     [tool_schemas.READ_FILE, tool_schemas.FIND_FILES, tool_schemas.SEARCH_TEXT], f"Task:\n{task}")


# Test: run-tests -k todo3_code
# Then try it on the real model: run-agent --coder (the API key steps are in CHEATSHEET.md)
def code(task, plan_text, feedback):
    """The Coder: the same loop with its own prompt and tools. feedback is None on the first attempt."""
    message = f"Task:\n{task}\n\nPlan:\n{plan_text}"
    if feedback is not None:
        message += f"\n\nReviewer feedback to address:\n{feedback}"
    return run_agent("coder", prompts.CODER,
                     [tool_schemas.READ_FILE, tool_schemas.WRITE_FILE, tool_schemas.FIND_FILES, tool_schemas.SEARCH_TEXT,
                      tool_schemas.RUN_TESTS], message)


# criteria is the task file's "## Review" section, for the Reviewer only; "" when there is none.
# Test: run-tests -k todo6_review_changes
# Nothing new to run yet: Run (TODO 7) brings the Reviewer in, then run-agent runs all three roles
def review_changes(task, criteria, plan_text, coder_summary, revision):
    tests = run_tests()
    diff = run_command("git add -N . && git diff")
    criteria_text = criteria or "(none given: check the requirements in the task)"
    message = (f"Task:\n{task}\n\nReview criteria:\n{criteria_text}\n\nPlan:\n{plan_text}\n\nCoder's summary:\n{coder_summary}\n\n"
               f"Test output ({config.TEST_COMMAND}):\n{tests.output}\n\nGit diff:\n{diff.output}")
    text = run_agent("reviewer", prompts.REVIEWER,
                     [tool_schemas.READ_FILE, tool_schemas.FIND_FILES, tool_schemas.SEARCH_TEXT], message)
    verdict = parse_verdict(text)
    log.verdict(revision, verdict)
    return Review(verdict == "APPROVED", text)


# Test: run-tests -k todo6_parse_verdict
def parse_verdict(review):
    """"APPROVED" only if the first line, trimmed, is exactly APPROVED."""
    first_line = review.lstrip().split("\n")[0].strip()
    return "APPROVED" if first_line == "APPROVED" else "CHANGES_REQUIRED"


# Test: run-tests -k todo7_run
# Then try it: reset-workspace, then run-agent
def run(task, criteria):
    """Returns True when the Reviewer approved."""
    log.transition("start", "planner", 0)
    plan_text = plan(task)
    feedback = None
    revision = 0
    while True:
        log.transition("planner" if revision == 0 else "reviewer", "coder", revision)
        summary = code(task, plan_text, feedback)

        log.transition("coder", "reviewer", revision)
        review = review_changes(task, criteria, plan_text, summary, revision)
        if review.approved:
            log.transition("reviewer", "end", revision)
            log.run_summary(True, revision)
            return True
        if revision == config.MAX_REVISIONS:
            log.info(f"Revision limit reached ({config.MAX_REVISIONS}): not approved.")
            log.transition("reviewer", "end", revision)
            log.run_summary(False, revision)
            return False
        feedback = review.text
        revision += 1
