namespace CodingAgent;

public record Review(bool Approved, string Text);

// Planner -> Coder -> [tests + diff] -> Reviewer, with at most MAX_REVISIONS
// rounds of feedback. Plain code decides what happens next, not a model.
public static class Orchestrator
{
    // Test: run-tests --filter Todo5_Plan
    // Then try it: reset-workspace, then run-agent --plan
    public static async Task<string> Plan(string task)
    {
        return await AgentLoop.RunAgent("planner", Prompts.Planner,
            [ToolSchemas.ReadFile, ToolSchemas.FindFiles, ToolSchemas.SearchText], $"Task:\n{task}");
    }

    // The Coder: the same loop with its own prompt and tools. feedback is null on the first attempt.
    // Test: run-tests --filter Todo3_Code
    // Then try it on the real model: run-agent --coder (the API key steps are in CHEATSHEET.md)
    public static Task<string> Code(string task, string plan, string? feedback)
    {
        var message = $"Task:\n{task}\n\nPlan:\n{plan}";
        if (feedback is not null) message += $"\n\nReviewer feedback to address:\n{feedback}";
        return AgentLoop.RunAgent("coder", Prompts.Coder,
            [ToolSchemas.ReadFile, ToolSchemas.WriteFile, ToolSchemas.FindFiles, ToolSchemas.SearchText, ToolSchemas.RunTests], message);
    }

    // criteria is the task file's "## Review" section, for the Reviewer only; "" when there is none.
    // Test: run-tests --filter Todo6_ReviewChanges
    // Nothing new to run yet: Run (TODO 7) brings the Reviewer in, then run-agent runs all three roles
    public static async Task<Review> ReviewChanges(string task, string criteria, string plan, string coderSummary, int revision)
    {
        // TODO 6: implement the Reviewer step
        // Hint: Run the tests (Tools.RunTests()) and "git add -N . && git diff" (Tools.RunCommand), give both to the Reviewer with the task, the review criteria (or a note that there are none), the plan and the summary, then ParseVerdict, and Log.Verdict(revision, verdict) so the trace shows it. Its tools are your call, as long as it only reads (ReadFile, FindFiles and SearchText fit).
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
    // Then try it: reset-workspace, then run-agent
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
