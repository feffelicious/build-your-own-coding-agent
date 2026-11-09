package codingagent.agent;

import codingagent.support.Config;
import codingagent.support.Log;
import codingagent.tools.ToolSchemas;
import codingagent.tools.Tools;

/**
 * Planner -> Coder -> [tests + diff] -> Reviewer, with at most MAX_REVISIONS
 * rounds of feedback. Plain code decides what happens next, not a model.
 */
public final class Orchestrator {
    public record Review(boolean approved, String text) {}

    private Orchestrator() {}

    // Test: run-tests -Dtest='*#todo5Plan*'
    // Then try it: reset-workspace, then run-agent --plan
    public static String plan(String task) {
        return AgentLoop.runAgent("planner", Prompts.PLANNER,
            ToolSchemas.list(ToolSchemas.READ_FILE, ToolSchemas.FIND_FILES, ToolSchemas.SEARCH_TEXT), "Task:\n" + task);
    }

    /** The Coder: the same loop with its own prompt and tools. feedback is null on the first attempt. */
    // Test: run-tests -Dtest='*#todo3Code*'
    // Then try it on the real model: run-agent --coder (the API key steps are in CHEATSHEET.md)
    public static String code(String task, String plan, String feedback) {
        String message = "Task:\n" + task + "\n\nPlan:\n" + plan;
        if (feedback != null) message += "\n\nReviewer feedback to address:\n" + feedback;
        return AgentLoop.runAgent("coder", Prompts.CODER, ToolSchemas.list(ToolSchemas.READ_FILE, ToolSchemas.WRITE_FILE,
            ToolSchemas.FIND_FILES, ToolSchemas.SEARCH_TEXT, ToolSchemas.RUN_TESTS), message);
    }

    // criteria is the task file's "## Review" section, for the Reviewer only; "" when there is none.
    // Test: run-tests -Dtest='*#todo6ReviewChanges*'
    // Nothing new to run yet: Run (TODO 7) brings the Reviewer in, then run-agent runs all three roles
    public static Review reviewChanges(String task, String criteria, String plan, String coderSummary, int revision) throws Exception {
        Tools.ToolResult tests = Tools.runTests();
        Tools.ToolResult diff = Tools.runCommand("git add -N . && git diff");
        String criteriaText = criteria.isEmpty() ? "(none given: check the requirements in the task)" : criteria;
        String message = "Task:\n" + task + "\n\nReview criteria:\n" + criteriaText + "\n\nPlan:\n" + plan + "\n\nCoder's summary:\n" + coderSummary + "\n\n"
            + "Test output (" + Config.TEST_COMMAND + "):\n" + tests.output() + "\n\nGit diff:\n" + diff.output();
        String text = AgentLoop.runAgent("reviewer", Prompts.REVIEWER,
            ToolSchemas.list(ToolSchemas.READ_FILE, ToolSchemas.FIND_FILES, ToolSchemas.SEARCH_TEXT), message);
        String verdict = parseVerdict(text);
        Log.verdict(revision, verdict);
        return new Review(verdict.equals("APPROVED"), text);
    }

    /** "APPROVED" only if the first line, trimmed, is exactly APPROVED. */
    // Test: run-tests -Dtest='*#todo6ParseVerdict*'
    public static String parseVerdict(String review) {
        String firstLine = review.stripLeading().split("\n", -1)[0].strip();
        return firstLine.equals("APPROVED") ? "APPROVED" : "CHANGES_REQUIRED";
    }

    /** Returns true when the Reviewer approved. */
    // Test: run-tests -Dtest='*#todo7Run*'
    // Then try it: reset-workspace, then run-agent
    public static boolean run(String task, String criteria) throws Exception {
        Log.transition("start", "planner", 0);
        String plan = plan(task);
        String feedback = null;
        for (int revision = 0; ; revision++) {
            Log.transition(revision == 0 ? "planner" : "reviewer", "coder", revision);
            String summary = code(task, plan, feedback);

            Log.transition("coder", "reviewer", revision);
            Review review = reviewChanges(task, criteria, plan, summary, revision);
            if (review.approved()) {
                Log.transition("reviewer", "end", revision);
                Log.runSummary(true, revision);
                return true;
            }
            if (revision == Config.MAX_REVISIONS) {
                Log.info("Revision limit reached (" + Config.MAX_REVISIONS + "): not approved.");
                Log.transition("reviewer", "end", revision);
                Log.runSummary(false, revision);
                return false;
            }
            feedback = review.text();
        }
    }
}
