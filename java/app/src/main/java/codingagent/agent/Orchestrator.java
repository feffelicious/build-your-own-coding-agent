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
        // TODO 5: implement the Planner step
        // Hint: Call AgentLoop.runAgent as "planner" with Prompts.PLANNER, the Planner's tools and the task in the user message. Its tools are your call, as long as it only reads: nothing that writes files or runs commands (ToolSchemas.READ_FILE, FIND_FILES and SEARCH_TEXT fit).
        throw new UnsupportedOperationException("TODO 5: implement the Planner step");
    }

    /** The Coder: the same loop with its own prompt and tools. feedback is null on the first attempt. */
    // Test: run-tests -Dtest='*#todo3Code*'
    // Then try it on the real model: run-agent --coder (the API key steps are in CHEATSHEET.md)
    public static String code(String task, String plan, String feedback) {
        // TODO 3: implement code, the loop run as the Coder
        // Hint: Build a user message from the task, the plan and any feedback, then call AgentLoop.runAgent("coder", Prompts.CODER, tools, message) with the Coder's tools as a list, e.g. ToolSchemas.list(ToolSchemas.READ_FILE, ToolSchemas.WRITE_FILE, ToolSchemas.FIND_FILES, ToolSchemas.SEARCH_TEXT). Your call: it needs at least read and write. Leave run_tests out for now: TODO 4 adds it, after a first run without it.
        throw new UnsupportedOperationException("TODO 3: implement code, the loop run as the Coder");
    }

    // criteria is the task file's "## Review" section, for the Reviewer only; "" when there is none.
    // Test: run-tests -Dtest='*#todo6ReviewChanges*'
    // Nothing new to run yet: Run (TODO 7) brings the Reviewer in, then run-agent runs all three roles
    public static Review reviewChanges(String task, String criteria, String plan, String coderSummary, int revision) throws Exception {
        // TODO 6: implement the Reviewer step
        // Hint: Run the tests (Tools.runTests()) and "git add -N . && git diff" (Tools.runCommand), give both to the Reviewer with the task, the review criteria (or a note that there are none), the plan and the summary, then parseVerdict, and Log.verdict(revision, verdict) so the trace shows it. Its tools are your call, as long as it only reads (READ_FILE, FIND_FILES and SEARCH_TEXT fit).
        throw new UnsupportedOperationException("TODO 6: implement the Reviewer step");
    }

    /** "APPROVED" only if the first line, trimmed, is exactly APPROVED. */
    // Test: run-tests -Dtest='*#todo6ParseVerdict*'
    public static String parseVerdict(String review) {
        // TODO 6: implement verdict parsing
        // Hint: Take the first line of the review, trimmed. Exactly "APPROVED" means approved; anything else is "CHANGES_REQUIRED".
        throw new UnsupportedOperationException("TODO 6: implement verdict parsing");
    }

    /** Returns true when the Reviewer approved. */
    // Test: run-tests -Dtest='*#todo7Run*'
    // Then try it: reset-workspace, then run-agent
    public static boolean run(String task, String criteria) throws Exception {
        // TODO 7: implement the revision loop
        // Hint: Follow the steps below; the Log calls are in CHEATSHEET.md.
        // 1. Log.transition("start", "planner", 0), then get the plan once: plan(task).
        // 2. Loop over revision = 0, 1, 2, ...:
        //    - Log.transition(revision == 0 ? "planner" : "reviewer", "coder", revision), then code(task, plan, feedback)
        //    - Log.transition("coder", "reviewer", revision), then reviewChanges(task, criteria, plan, summary, revision)
        // 3. Approved? Log.transition("reviewer", "end", revision), Log.runSummary(true, revision), return true.
        // 4. revision == Config.MAX_REVISIONS? Log.info("Revision limit reached: not approved."),
        //    Log.transition("reviewer", "end", revision), Log.runSummary(false, revision), return false.
        // 5. Otherwise the full review text is the feedback for the next code call.
        throw new UnsupportedOperationException("TODO 7: implement the revision loop");
    }
}
