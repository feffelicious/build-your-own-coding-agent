using Anthropic.Exceptions;
using CodingAgent;

// Usage: run-agent [--coder | --plan] [task-file | --task "text"]
//   (no flag)  Planner, Coder and Reviewer with revisions (needs TODO 1-7)
//   --coder    the Coder on its own (needs TODO 1-3)
//   --plan     the Planner, then the Coder once (needs TODO 1-5)
// Exit codes: 0 approved or finished, 1 not approved, 2 TODO not implemented,
// 3 spend limit reached, 4 other API error.
var mode = args.FirstOrDefault(a => a.StartsWith("--")) ?? "";
var taskFile = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "task.md";
if (mode is not ("" or "--coder" or "--plan"))
{
    Console.Error.WriteLine("Usage: run-agent [--coder | --plan] [task-file | --task \"text\"]   (default task file: task.md)");
    return 2;
}
if (!File.Exists(taskFile))
{
    Console.Error.WriteLine($"Task file not found: {taskFile}");
    return 2;
}
// Everything above a "## Review" line is the task; the lines below it are the review
// criteria, which only the Reviewer gets. No such line: no criteria.
var lines = File.ReadAllLines(taskFile);
var split = Array.FindIndex(lines, line => line.Trim().Equals("## Review", StringComparison.OrdinalIgnoreCase));
var task = string.Join("\n", split < 0 ? lines : lines[..split]).Trim();
var criteria = split < 0 ? "" : string.Join("\n", lines[(split + 1)..]).Trim();

Log.Start();
try
{
    switch (mode)
    {
        case "--coder":
            Log.Transition("start", "coder", 0);
            await Orchestrator.Code(task, "(no plan: the Coder is running on its own)", null);
            Log.Transition("coder", "end", 0);
            Log.RunSummary(null, 0);
            return 0;
        case "--plan":
            Log.Transition("start", "planner", 0);
            var plan = await Orchestrator.Plan(task);
            Log.Transition("planner", "coder", 0);
            await Orchestrator.Code(task, plan, null);
            Log.Transition("coder", "end", 0);
            Log.RunSummary(null, 0);
            return 0;
        default:
            return await Orchestrator.Run(task, criteria) ? 0 : 1;
    }
}
catch (NotImplementedException e)
{
    Console.Error.WriteLine($"\nNot implemented yet: {e.Message}");
    return 2;
}
catch (AnthropicBadRequestException e) when (e.Message.Contains("usage limit", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("\nSpend limit reached for the workshop API key. Tell the facilitator.");
    return 3;
}
catch (AnthropicApiException e)
{
    Console.Error.WriteLine($"\nAPI error: {e.Message}");
    return 4;
}
catch (AnthropicException e) // no answer at all, e.g. the proxy isn't running
{
    Console.Error.WriteLine($"\nAPI error: {e.Message}");
    return 4;
}
