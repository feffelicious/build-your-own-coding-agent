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
        var tests = Tools.RunTests();
        var diff = Tools.RunCommand("git add -N . && git diff");
        var criteriaText = criteria == "" ? "(none given: check the requirements in the task)" : criteria;
        var message = $"Task:\n{task}\n\nReview criteria:\n{criteriaText}\n\nPlan:\n{plan}\n\nCoder's summary:\n{coderSummary}\n\n"
            + $"Test output ({Config.TestCommand}):\n{tests.Output}\n\nGit diff:\n{diff.Output}";
        var text = await AgentLoop.RunAgent("reviewer", Prompts.Reviewer,
            [ToolSchemas.ReadFile, ToolSchemas.FindFiles, ToolSchemas.SearchText], message);
        var verdict = ParseVerdict(text);
        Log.Verdict(revision, verdict);
        return new Review(verdict == "APPROVED", text);
    }

    // "APPROVED" only if the first line, trimmed, is exactly APPROVED.
    // Test: run-tests --filter Todo6_ParseVerdict
    public static string ParseVerdict(string review)
    {
        var firstLine = review.TrimStart().Split('\n')[0].Trim();
        return firstLine == "APPROVED" ? "APPROVED" : "CHANGES_REQUIRED";
    }

    // Returns true when the Reviewer approved.
    // Test: run-tests --filter Todo7_Run
    // Then try it: reset-workspace, then run-agent
    public static async Task<bool> Run(string task, string criteria)
    {
        Log.Transition("start", "planner", 0);
        var plan = await Plan(task);
        string? feedback = null;
        for (var revision = 0; ; revision++)
        {
            Log.Transition(revision == 0 ? "planner" : "reviewer", "coder", revision);
            var summary = await Code(task, plan, feedback);

            Log.Transition("coder", "reviewer", revision);
            var review = await ReviewChanges(task, criteria, plan, summary, revision);
            if (review.Approved)
            {
                Log.Transition("reviewer", "end", revision);
                Log.RunSummary(true, revision);
                return true;
            }
            if (revision == Config.MaxRevisions)
            {
                Log.Info($"Revision limit reached ({Config.MaxRevisions}): not approved.");
                Log.Transition("reviewer", "end", revision);
                Log.RunSummary(false, revision);
                return false;
            }
            feedback = review.Text;
        }
    }
}
