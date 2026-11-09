"""The system prompts: what makes the same loop a Planner, a Coder or a Reviewer.
They know nothing about the calculator: the task file says what to build and how
to judge it. The text is identical in the C#, Python and Java tracks."""

# Test: run-tests -k todo5_prompt
# TODO 5: write the Planner prompt
# Hint: Say: its role (first of Planner, Coder, Reviewer), that it can read but not change anything, that nobody answers questions during a run, and what to reply with (a short numbered plan naming files and tests, no code).
PLANNER = "TODO 5: write the Planner prompt"

# Test: run-tests -k todo3_prompt
# TODO 3: write the Coder prompt
# Hint: Say: its role, its tools and when to use each, that nobody answers questions, to follow the plan, to address every point of feedback (the workspace already holds its previous attempt), and what to finish with (a short summary). In TODO 4, when it gets run_tests: to run the tests before finishing.
CODER = "TODO 3: write the Coder prompt"

# Test: run-tests -k todo6_prompt
# TODO 6: write the Reviewer prompt
# Hint: Say: its role, what it receives (task, review criteria, plan, Coder's summary, test output, diff), that nobody answers questions, to check every criterion (no criteria: every requirement in the task), and the exact reply format parse_verdict reads: APPROVED alone on the first line, or CHANGES_REQUIRED and then one bullet per problem with a concrete fix.
REVIEWER = "TODO 6: write the Reviewer prompt"
