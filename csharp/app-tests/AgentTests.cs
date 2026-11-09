using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using CodingAgent;
using static CodingAgent.Tests.FakeModel;

namespace CodingAgent.Tests;

// TODO 3 to 7, against a scripted model (FakeModel), so no API key is needed.
// Each test scripts what the model answers, then checks what your code sent.
public class AgentTests : IDisposable
{
    readonly string _workspace = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _logDir = Path.Combine(Path.GetTempPath(), "logs-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _originalWorkspace = Config.Workspace;
    readonly string _originalLogDir = Config.LogDir;
    readonly string _originalTestCommand = Config.TestCommand;
    readonly int _originalMaxToolCalls = Config.MaxToolCalls;
    readonly int _originalMaxRevisions = Config.MaxRevisions;
    readonly FakeModel _model = new();
    // Any tool list will do for the loop's own tests.
    static readonly List<ToolUnion> SomeTools = [ToolSchemas.ReadFile, ToolSchemas.WriteFile, ToolSchemas.FindFiles];

    public AgentTests()
    {
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, "notes.txt"), "hello from the workspace");
        Config.Workspace = _workspace;
        Config.TestCommand = "echo TESTS-RAN";
        // A log file of its own per test, so a test can check what your code logged.
        Config.LogDir = _logDir;
        Log.Start();
    }

    public void Dispose()
    {
        _model.Dispose();
        if (Directory.Exists(Path.Combine(_workspace, "repo")))
        {
            Config.Workspace = _workspace;
            Tools.RunCommand("rm -rf repo"); // runner made it, so runner removes it
        }
        Config.Workspace = _originalWorkspace;
        Config.LogDir = _originalLogDir;
        Config.TestCommand = _originalTestCommand;
        Config.MaxToolCalls = _originalMaxToolCalls;
        Config.MaxRevisions = _originalMaxRevisions;
        Directory.Delete(_workspace, recursive: true);
        Directory.Delete(_logDir, recursive: true);
    }

    [Fact]
    public async Task Todo3_RunAgent_ReturnsTheFinalText()
    {
        _model.Says("All done.");

        var result = await AgentLoop.RunAgent("coder", "You are a test.", SomeTools, "Do the task.");

        Expect.That(result == "All done.",
            "When the model stops asking for tools, RunAgent should return its text: FinalText(response).",
            result);
        Expect.That(_model.Requests.Count == 1,
            $"With no tool calls, RunAgent should call the model exactly once, but it called it {_model.Requests.Count} times.");
        var request = _model.Request(0);
        Expect.That((string?)request["model"] == Config.Model && (int?)request["max_tokens"] == Config.MaxTokens,
            $"Send Model = Config.Model (\"{Config.Model}\") and MaxTokens = Config.MaxTokens ({Config.MaxTokens}).",
            $"model {request["model"]}, max_tokens {request["max_tokens"]}");
        Expect.That(Text(request["system"]) == "You are a test.",
            "Send the systemPrompt argument as System.",
            Text(request["system"]));
        Expect.That(ToolNames(request).SequenceEqual(["read_file", "write_file", "find_files"]),
            "Send the tools you were given as Tools.",
            string.Join(", ", ToolNames(request)));
        var messages = Messages(request);
        Expect.That(messages.Count == 1 && (string?)messages[0]!["role"] == "user" && Text(messages[0]!["content"]) == "Do the task.",
            "A fresh conversation starts with one user message: the userMessage argument.",
            messages.ToJsonString());
        Expect.That(request["cache_control"] is not null,
            "Set CacheControl = new CacheControlEphemeral() on the request, so the growing conversation is cached between calls.");
    }

    [Fact]
    public async Task Todo3_RunAgent_RunsTheToolAndSendsTheResultBack()
    {
        _model.CallsTools(("read_file", new { path = "notes.txt" })).Says("I read it.");

        var result = await AgentLoop.RunAgent("coder", "You are a test.", SomeTools, "Read notes.txt.");

        Expect.That(_model.Requests.Count == 2,
            "After a reply with stop_reason \"tool_use\", run the tool and call the model again. Expected 2 calls, "
            + $"got {_model.Requests.Count}.");
        Expect.That(result == "I read it.",
            "Return the text of the last reply, the one after the tool result.",
            result);
        var messages = Messages(_model.Request(1));
        Expect.That(messages.Count == 3 && Roles(messages) == "user,assistant,user",
            "The second call should send the whole conversation: your user message, the assistant turn, then a "
            + "user message with the tool result.",
            Roles(messages));
        Expect.That(SameBlocks(messages[1]!["content"], _model.Replies[0]),
            "Append the assistant turn exactly as received, every block including the thinking block: "
            + "Content = ToParams(response.Content).",
            messages[1]!["content"]?.ToJsonString());
        var results = ToolResults(messages[2]);
        var toolUseId = (string?)_model.Replies[0][1]!["id"];
        Expect.That(results.Count == 1 && (string?)results[0]["tool_use_id"] == toolUseId,
            $"Answer the tool call with a tool_result whose ToolUseID is the call's ID ({toolUseId}).",
            messages[2]!["content"]?.ToJsonString());
        Expect.That(Text(results[0]["content"]).Contains("hello from the workspace") && results[0]["is_error"]?.GetValue<bool>() != true,
            "The tool_result should carry what the tool returned (the text of notes.txt) and IsError false: run it "
            + "with Tools.ExecuteTool(toolUse.Name, toolUse.Input).",
            results[0].ToJsonString());
        var logged = Logged(null, e => $"{e["event"]}({e["role"]})");
        Expect.That(logged == "model_call(coder), tool_call(coder), tool_result(coder), model_call(coder)",
            "Log every step with the role you were given, so the trace shows it: Log.ModelCall after each model "
            + "call, Log.ToolCall before running a tool and Log.ToolResult after it.",
            logged == "" ? "nothing logged" : $"log: {logged}");
    }

    [Fact]
    public async Task Todo3_RunAgent_SendsAllResultsInOneMessage()
    {
        _model.CallsTools(("read_file", new { path = "notes.txt" }), ("read_file", new { path = "missing.txt" })).Says("ok");

        await AgentLoop.RunAgent("coder", "You are a test.", SomeTools, "Read two files.");

        var messages = Messages(_model.Request(1));
        Expect.That(messages.Count == 3,
            "When the model asks for several tools in one turn, send ALL the results back in ONE user message, "
            + "not one message per result.",
            Roles(messages));
        var results = ToolResults(messages[2]);
        var ids = _model.Replies[0].Skip(1).Select(b => (string?)b!["id"]).ToList();
        Expect.That(results.Select(r => (string?)r["tool_use_id"]).SequenceEqual(ids),
            $"There should be one tool_result per tool call, matched by ID, in order ({string.Join(", ", ids)}).",
            messages[2]!["content"]?.ToJsonString());
        Expect.That(results.Count == 2 && results[1]["is_error"]?.GetValue<bool>() == true,
            "A tool that fails (missing.txt doesn't exist) is still answered, with IsError = true, so the model "
            + "knows it went wrong.",
            results.Count == 2 ? results[1].ToJsonString() : messages[2]!["content"]?.ToJsonString());
    }

    [Fact]
    public async Task Todo3_RunAgent_RefusesToolsItWasNotGiven()
    {
        _model.CallsTools(("write_file", new { path = "sneaky.txt", content = "should not be written" })).Says("Tried.");

        await AgentLoop.RunAgent("planner", "You are a test.", [ToolSchemas.ReadFile, ToolSchemas.FindFiles], "Read only.");

        var second = Messages(_model.Request(1));
        Expect.That(second.Count == 3,
            "After refusing the tool, call the model again with your user message, the assistant turn, and one user "
            + "message with the tool results, the refusal among them.",
            Roles(second));
        var results = ToolResults(second[2]);
        Expect.That(!File.Exists(Path.Combine(_workspace, "sneaky.txt")),
            "The model asked for write_file, which this role wasn't given, and your loop ran it. The tools you send "
            + "only tell the model what it may use; your code has to enforce it: run a tool only if "
            + "AgentLoop.Offers(tools, toolUse.Name).");
        Expect.That(results.Count == 1 && results[0]["is_error"]?.GetValue<bool>() == true
                && Text(results[0]["content"]).Contains("not one of your tools"),
            "Answer a tool the role wasn't given with an error result, \"Refused: write_file is not one of your "
            + "tools\", so the model knows, instead of running it.",
            results.Count == 1 ? results[0].ToJsonString() : "no tool_result");
    }

    [Fact]
    public async Task Todo3_RunAgent_StopsAtTheToolCallCap()
    {
        Config.MaxToolCalls = 3;
        _model.WhenScriptEnds = model => model.ToolCalls(("read_file", new { path = "notes.txt" }));

        var result = await AgentLoop.RunAgent("coder", "You are a test.", SomeTools, "Keep reading.");

        Expect.That(result == "[stopped: more than 3 tool calls]",
            "A model that never stops asking for tools must be stopped: after Config.MaxToolCalls tool calls, "
            + "Log.CapReached and return \"[stopped: more than N tool calls]\" with N = Config.MaxToolCalls.",
            result);
        Expect.That(_model.Requests.Count == 4,
            "With Config.MaxToolCalls = 3, run 3 tools (3 calls to the model), and stop when the 4th reply asks for "
            + $"another: 4 calls in total, but there were {_model.Requests.Count}.");
        var capped = Logged("cap_reached", e => $"{e["role"]} {e["tool_calls"]}");
        Expect.That(capped == "coder 3",
            "Call Log.CapReached(role, toolCalls) before you return, so the trace shows why the agent stopped, with "
            + "the number of tools that ran (3 here, Config.MaxToolCalls). Logged 4? Then the count went up before "
            + "Log.CapReached: \"if (toolCalls++ == Config.MaxToolCalls)\" compares 3 but leaves 4. Compare with "
            + "toolCalls == Config.MaxToolCalls, and do toolCalls++ after the if.",
            $"cap_reached logged (role, tool_calls): {capped}");
    }

    [Fact]
    public async Task Todo3_RunAgent_CountsToolsNotModelCalls()
    {
        Config.MaxToolCalls = 3;
        _model.WhenScriptEnds = model => model.ToolCalls(("read_file", new { path = "notes.txt" }), ("read_file", new { path = "notes.txt" }));

        var result = await AgentLoop.RunAgent("coder", "You are a test.", SomeTools, "Keep reading, two at a time.");

        var calls = Logged("tool_call", e => "x");
        var ran = calls == "" ? 0 : calls.Split(", ").Length;
        Expect.That(ran > 0,
            "No tool_call in the log: this test counts the tools you ran from it. Log every tool before running it (Log.ToolCall), then the cap is checked.");
        Expect.That(result == "[stopped: more than 3 tool calls]" && ran == 3,
            "The cap counts tools, not model calls: here each reply asks for two tools, so stop after the 3rd tool, "
            + "in the middle of the 2nd reply. Count each tool you run (toolCalls++ per tool_use block).",
            $"result \"{result}\", tools run: {ran}");
    }

    [Fact]
    public async Task Todo3_Code_SendsTaskPlanAndFeedback()
    {
        _model.Says("Changed it.");

        var summary = await Orchestrator.Code("THE TASK", "THE PLAN", "THE FEEDBACK");

        var request = _model.Requests.Single();
        var message = Text(Messages(request)[0]!["content"]);
        Expect.That(summary == "Changed it.", "Code should return what RunAgent returns.", summary);
        Expect.That(Text(request["system"]) == Prompts.Coder,
            "Code should call AgentLoop.RunAgent with \"coder\" and Prompts.Coder as the system prompt.",
            Text(request["system"]));
        Expect.That(new[] { "read_file", "write_file" }.All(ToolNames(request).Contains),
            "The Coder needs at least ToolSchemas.ReadFile and WriteFile (FindFiles and SearchText help it find code; run_tests "
            + "follows in TODO 4).",
            string.Join(", ", ToolNames(request)));
        Expect.That(message.Contains("THE TASK") && message.Contains("THE PLAN") && message.Contains("THE FEEDBACK"),
            "The Coder's user message should contain the task, the plan and, when there is some, the reviewer's feedback.",
            message);

        _model.Says("Coded it.");
        await Orchestrator.Code("THE TASK", "THE PLAN", null);

        var first = Text(Messages(_model.Request(1))[0]!["content"]);
        Expect.That(first.Contains("THE TASK") && first.Contains("THE PLAN") && !first.Contains("null"),
            "On the first attempt there is no feedback (feedback is null): send the task and the plan, and leave the "
            + "feedback part out instead of sending \"null\".",
            first);
    }

    [Fact]
    public async Task Todo4_Code_CanRunTheTests()
    {
        _model.Says("Changed it.");

        await Orchestrator.Code("THE TASK", "THE PLAN", null);

        Expect.That(ToolNames(_model.Requests.Single()).Contains("run_tests"),
            "Give the Coder run_tests: add ToolSchemas.RunTests to its tools in Orchestrator.Code (TODO 4). Without "
            + "it the Coder can't check its own work.",
            string.Join(", ", ToolNames(_model.Requests.Single())));
    }

    // The tools that change files or run commands. A role that only reads gets none of them.
    static readonly string[] Writers = ["write_file", "edit_file", "run_command"];

    static bool OnlyReads(JsonObject request) =>
        ToolNames(request).Contains("read_file") && !ToolNames(request).Intersect(Writers).Any();

    [Fact]
    public async Task Todo5_Plan_AsksThePlanner()
    {
        _model.Says("1. Do it.");

        var plan = await Orchestrator.Plan("THE TASK");

        var request = _model.Requests.Single();
        Expect.That(plan == "1. Do it.", "Plan should return the Planner's answer.", plan);
        Expect.That(Text(request["system"]) == Prompts.Planner,
            "Plan should call RunAgent as \"planner\" with Prompts.Planner as the system prompt.",
            Text(request["system"]));
        Expect.That(OnlyReads(request),
            "The Planner only reads: give it ToolSchemas.ReadFile (FindFiles and SearchText help it search), and nothing that "
            + "changes files or runs commands (write_file, run_command, an edit_file of your own).",
            string.Join(", ", ToolNames(request)));
        Expect.That(Text(Messages(request)[0]!["content"]).Contains("THE TASK"),
            "The Planner's user message should contain the task.",
            Text(Messages(request)[0]!["content"]));
    }

    [Fact]
    public async Task Todo6_ReviewChanges_GivesTheReviewerEverything()
    {
        _model.Says("APPROVED\nAll good.");
        GitRepoWithANewFile();

        var review = await Orchestrator.ReviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0);

        var request = _model.Requests.Single();
        var message = Text(Messages(request)[0]!["content"]);
        Expect.That(review.Approved && review.Text == "APPROVED\nAll good.",
            "A review whose first line is APPROVED should come back as Approved = true, with the full review text.",
            review);
        Expect.That(Text(request["system"]) == Prompts.Reviewer && OnlyReads(request),
            "Call RunAgent as \"reviewer\" with Prompts.Reviewer and tools that only read: ToolSchemas.ReadFile (FindFiles "
            + "and SearchText help), nothing that changes files or runs commands. run_tests is fine.",
            $"tools: {string.Join(", ", ToolNames(request))}");
        Expect.That(message.Contains("THE TASK") && message.Contains("THE PLAN") && message.Contains("THE SUMMARY"),
            "The Reviewer's message should contain the task, the plan and the Coder's summary.",
            message);
        Expect.That(message.Contains("THE CRITERIA"),
            "The Reviewer's message should contain the review criteria (the criteria parameter): they say what to check.",
            message);
        Expect.That(message.Contains("TESTS-RAN"),
            "The Reviewer should get the real test output: run the tests with Tools.RunTests() and include its "
            + "output (the test sets Config.TestCommand to \"echo TESTS-RAN\").",
            message);
        Expect.That(message.Contains("NEW-FILE-CONTENT"),
            "The Reviewer should also get the git diff, including new files: run \"git add -N . && git diff\" with "
            + "Tools.RunCommand and include its output. (Plain git diff leaves out files git doesn't track yet, like "
            + "the new test file the Coder wrote here.)",
            message);
        Expect.That(!message.Contains("IsError"),
            "Give the Reviewer the commands' output text (result.Output), not the ToolResult itself: the message "
            + "contains \"ToolResult { Output = ..., IsError = ... }\".",
            message);
    }

    [Fact]
    public async Task Todo6_ReviewChanges_UsesParseVerdict()
    {
        _model.Says("APPROVED with two small notes\n- naming").Says("\n  APPROVED\nAll good.");

        var chatty = await Orchestrator.ReviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0);
        var padded = await Orchestrator.ReviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 1);

        Expect.That(!chatty.Approved,
            "The Reviewer answered \"APPROVED with two small notes\": its first line isn't exactly APPROVED, so that's "
            + "CHANGES_REQUIRED. Decide with ParseVerdict(text), not with your own check (StartsWith accepts this).",
            chatty);
        Expect.That(padded.Approved,
            "The Reviewer answered with a blank line, then \"  APPROVED\": ParseVerdict trims that, so it's approved. "
            + "Decide with ParseVerdict(text), not with your own check.",
            padded);
        var verdicts = Logged("verdict", e => $"{e["revision"]} {e["verdict"]}");
        Expect.That(verdicts == "0 CHANGES_REQUIRED, 1 APPROVED",
            "Log each verdict so the trace shows it: Log.Verdict(revision, verdict) with what ParseVerdict returned.",
            $"verdicts logged: {verdicts}");
    }

    [Fact]
    public async Task Todo6_ReviewChanges_ChangesRequiredIsNotApproved()
    {
        _model.Says("CHANGES_REQUIRED\n- divide truncates");

        var review = await Orchestrator.ReviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0);

        Expect.That(!review.Approved && review.Text == "CHANGES_REQUIRED\n- divide truncates",
            "A CHANGES_REQUIRED review should come back as Approved = false, with the full review text: the Coder "
            + "gets it as feedback.",
            review);
    }

    [Fact]
    public async Task Todo7_Run_RevisesUntilApproved()
    {
        _model.Says("1. THE PLAN").Says("Coded it.").Says("CHANGES_REQUIRED\n- REVIEW-ONE")
            .Says("Fixed it.").Says("APPROVED");

        var approved = await Orchestrator.Run("THE TASK", "THE CRITERIA");

        var roles = string.Join(" → ", _model.Requests.Select(RoleOf));
        Expect.That(roles == "planner → coder → reviewer → coder → reviewer",
            "Run should plan once, then Coder and Reviewer, and after CHANGES_REQUIRED the Coder and Reviewer again.",
            roles);
        Expect.That(approved, "Run should return true once the Reviewer approves.", approved);
        var secondCoder = Text(Messages(_model.Request(3))[0]!["content"]);
        Expect.That(secondCoder.Contains("REVIEW-ONE") && secondCoder.Contains("THE PLAN"),
            "The second Coder run should get the plan and the full review as feedback.",
            secondCoder);
        var sawCriteria = string.Join(" → ", _model.Requests.Select(r =>
            $"{RoleOf(r)}:{(Text(Messages(r)[0]!["content"]).Contains("THE CRITERIA") ? "yes" : "no")}"));
        Expect.That(sawCriteria == "planner:no → coder:no → reviewer:yes → coder:no → reviewer:yes",
            "Only the Reviewer gets the review criteria: pass criteria to ReviewChanges, not to Plan or Code.",
            sawCriteria);
        ExpectFlowLogged(approved: true);
    }

    [Fact]
    public async Task Todo7_Run_StopsAtTheRevisionLimit()
    {
        Config.MaxRevisions = 1;
        _model.Says("1. THE PLAN");
        _model.WhenScriptEnds = model =>
            [new JsonObject { ["type"] = "text", ["text"] = model.Requests.Count % 2 == 0 ? "Coded it." : "CHANGES_REQUIRED\n- still wrong" }];

        var approved = await Orchestrator.Run("THE TASK", "THE CRITERIA");

        var roles = string.Join(" → ", _model.Requests.Select(RoleOf));
        Expect.That(!approved, "Run should return false when the revision limit is reached without approval.", approved);
        Expect.That(roles == "planner → coder → reviewer → coder → reviewer",
            "With Config.MaxRevisions = 1 the Coder runs twice (the first attempt plus one revision), then Run stops.",
            roles);
        ExpectFlowLogged(approved: false);
    }

    // Both Run tests take the same path: plan, code, review, code again, review again.
    // A git repo, made as runner (who runs git in the real workspace), with one committed file and
    // one new, untracked file, as after the Coder added a test file.
    void GitRepoWithANewFile()
    {
        File.SetUnixFileMode(_workspace, (UnixFileMode)0b111_111_111);
        var made = Tools.RunCommand(
            "mkdir repo && cd repo && git init -q && echo old > a.txt && git add a.txt "
            + "&& git -c user.name=t -c user.email=t@t commit -qm init && echo NEW-FILE-CONTENT > new_test.txt");
        Assert.True(made.Output.Contains("exit code 0"), made.Output);
        Config.Workspace = Path.Combine(_workspace, "repo");
    }

    void ExpectFlowLogged(bool approved)
    {
        var transitions = Logged("transition", e => $"{e["from"]}→{e["to"]} {e["revision"]}");
        Expect.That(transitions == "start→planner 0, planner→coder 0, coder→reviewer 0, reviewer→coder 1, coder→reviewer 1, reviewer→end 1",
            "Log each step of the flow with Log.Transition(from, to, revision), as the steps in Run list them.",
            $"transitions logged: {transitions}");
        var summary = Logged("run_summary", e => $"approved {e["approved"]}, revisions {e["revisions"]}");
        Expect.That(summary == $"approved {(approved ? "true" : "false")}, revisions 1",
            $"End the run with Log.RunSummary({(approved ? "true" : "false")}, revision), once.",
            $"run_summary logged: {summary}");
    }

    // The events of one kind (all when kind is null) in this test's log, each shown with show, comma-separated.
    string Logged(string? kind, Func<JsonObject, string> show)
    {
        // Log keeps the file open, so open it for reading in a way that allows that.
        using var stream = new FileStream(Directory.GetFiles(_logDir).Single(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var lines = new StreamReader(stream).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(", ", lines
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(e => kind is null || (string?)e["event"] == kind)
            .Select(show));
    }

    static string Roles(JsonArray messages) => string.Join(",", messages.Select(m => (string?)m!["role"]));

    static List<JsonObject> ToolResults(JsonNode? message) =>
        message?["content"] is JsonArray blocks
            ? blocks.Where(b => (string?)b!["type"] == "tool_result").Select(b => b!.AsObject()).ToList()
            : [];

    static bool SameBlocks(JsonNode? sent, JsonArray received) =>
        sent is JsonArray blocks && blocks.Count == received.Count
        && blocks.Zip(received).All(pair => Essentials(pair.First) == Essentials(pair.Second));

    static string RoleOf(JsonObject request) => Text(request["system"]) switch
    {
        var s when s == Prompts.Planner => "planner",
        var s when s == Prompts.Coder => "coder",
        var s when s == Prompts.Reviewer => "reviewer",
        _ => "?",
    };
}
