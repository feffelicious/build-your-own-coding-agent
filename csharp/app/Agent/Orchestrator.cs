namespace CodingAgent;

public record Review(bool Approved, string Text);

// Planner -> Coder -> [tests + diff] -> Reviewer, with at most MAX_REVISIONS
// rounds of feedback. Plain code decides what happens next, not a model.
public static class Orchestrator
{
    // Test: run-tests --filter Todo5_Plan
    public static async Task<string> Plan(string task)
    {
        // TODO 5: implement the Planner step
        // Hint: Call AgentLoop.RunAgent as "planner" with Prompts.Planner, the Planner's tools and the task in the user message. Its tools are your call, as long as it only reads: nothing that writes files or runs commands (ToolSchemas.ReadFile, Glob and Grep fit).
        throw new NotImplementedException("TODO 5: implement the Planner step");
    }

    // The Coder: the same loop with its own prompt and tools. feedback is null on the first attempt.
    // Test: run-tests --filter Todo3_Code
    public static Task<string> Code(string task, string plan, string? feedback)
    {
        // TODO 3: implement Code, the loop run as the Coder
        // Hint: Build a user message from the task, the plan and any feedback, then call AgentLoop.RunAgent("coder", Prompts.Coder, tools, message) with the Coder's tools as a list, e.g. [ToolSchemas.ReadFile, ToolSchemas.WriteFile, ToolSchemas.Glob, ToolSchemas.Grep]. Your call: it needs at least read and write. Leave run_tests out for now: TODO 4 adds it, after a first run without it.
        throw new NotImplementedException("TODO 3: implement Code, the loop run as the Coder");
    }

    // criteria is the task file's "## Review" section, for the Reviewer only; "" when there is none.
    // Test: run-tests --filter Todo6_ReviewChanges
    public static async Task<Review> ReviewChanges(string task, string criteria, string plan, string coderSummary, int revision)
    {
        // TODO 6: implement the Reviewer step
        // Hint: Run the tests (Tools.RunTests()) and "git add -N . && git diff" (Tools.RunCommand), give both to the Reviewer with the task, the review criteria (or a note that there are none), the plan and the summary, then ParseVerdict, and Log.Verdict(revision, verdict) so the trace shows it. Its tools are your call, as long as it only reads (ReadFile, Glob and Grep fit).
        throw new NotImplementedException("TODO 6: implement the Reviewer step");
    }

    // "APPROVED" only if the first line, trimmed, is exactly APPROVED.
    // Test: run-tests --filter Todo6_ParseVerdict
    public static string ParseVerdict(string review)
    {
        // TODO 6: implement verdict parsing
        // Hint: Take the first line of the review, trimmed. Exactly "APPROVED" means approved; anything else is "CHANGES_REQUIRED".
        throw new NotImplementedException("TODO 6: implement verdict parsing");
    }

    // Returns true when the Reviewer approved.
    // Test: run-tests --filter Todo7_Run
    public static async Task<bool> Run(string task, string criteria)
    {
        // TODO 7: implement the revision loop
        // Hint: Follow the steps below; the Log calls are in CHEATSHEET.md.
        // 1. Log.Transition("start", "planner", 0), then get the plan once: Plan(task).
        // 2. Loop over revision = 0, 1, 2, ...:
        //    - Log.Transition(revision == 0 ? "planner" : "reviewer", "coder", revision), then Code(task, plan, feedback)
        //    - Log.Transition("coder", "reviewer", revision), then ReviewChanges(task, criteria, plan, summary, revision)
        // 3. Approved? Log.Transition("reviewer", "end", revision), Log.RunSummary(true, revision), return true.
        // 4. revision == Config.MaxRevisions? Log.Info("Revision limit reached: not approved."),
        //    Log.Transition("reviewer", "end", revision), Log.RunSummary(false, revision), return false.
        // 5. Otherwise the full review text is the feedback for the next Code call.
        throw new NotImplementedException("TODO 7: implement the revision loop");
    }
}
