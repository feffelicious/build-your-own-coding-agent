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
    # TODO 5: implement the Planner step
    # Hint: Call run_agent as "planner" with prompts.PLANNER, the Planner's tools and the task in the user message. Its tools are your call, as long as it only reads: nothing that writes files or runs commands (tool_schemas.READ_FILE, FIND_FILES and SEARCH_TEXT fit).
    raise NotImplementedError("TODO 5: implement the Planner step")


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
    # TODO 6: implement the Reviewer step
    # Hint: Run the tests (run_tests()) and "git add -N . && git diff" (run_command), give both to the Reviewer with the task, the review criteria (or a note that there are none), the plan and the summary, then parse_verdict, and log.verdict(revision, verdict) so the trace shows it. Its tools are your call, as long as it only reads (READ_FILE, FIND_FILES and SEARCH_TEXT fit).
    raise NotImplementedError("TODO 6: implement the Reviewer step")


# Test: run-tests -k todo6_parse_verdict
def parse_verdict(review):
    """"APPROVED" only if the first line, trimmed, is exactly APPROVED."""
    # TODO 6: implement verdict parsing
    # Hint: Take the first line of the review, trimmed. Exactly "APPROVED" means approved; anything else is "CHANGES_REQUIRED".
    raise NotImplementedError("TODO 6: implement verdict parsing")


# Test: run-tests -k todo7_run
# Then try it: reset-workspace, then run-agent
def run(task, criteria):
    """Returns True when the Reviewer approved."""
    # TODO 7: implement the revision loop
    # Hint: Follow the steps below; the log calls are in CHEATSHEET.md.
    # 1. log.transition("start", "planner", 0), then get the plan once: plan(task).
    # 2. Loop over revision = 0, 1, 2, ...:
    #    - log.transition("planner" if revision == 0 else "reviewer", "coder", revision), then code(task, plan, feedback)
    #    - log.transition("coder", "reviewer", revision), then review_changes(task, criteria, plan, summary, revision)
    # 3. Approved? log.transition("reviewer", "end", revision), log.run_summary(True, revision), return True.
    # 4. revision == config.MAX_REVISIONS? log.info("Revision limit reached: not approved."),
    #    log.transition("reviewer", "end", revision), log.run_summary(False, revision), return False.
    # 5. Otherwise the full review text is the feedback for the next code call.
    raise NotImplementedError("TODO 7: implement the revision loop")
