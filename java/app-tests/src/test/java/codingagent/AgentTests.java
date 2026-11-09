package codingagent;

import codingagent.agent.AgentLoop;
import codingagent.agent.Orchestrator;
import codingagent.agent.Prompts;
import codingagent.support.Config;
import codingagent.support.Log;
import codingagent.tools.ToolSchemas;
import codingagent.tools.Tools;

import static codingagent.Explain.explain;
import static codingagent.FakeModel.essentials;
import static codingagent.FakeModel.text;
import static codingagent.FakeModel.toolNames;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.anthropic.models.messages.ToolUnion;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ArrayNode;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.PosixFilePermissions;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/**
 * TODO 3 to 7, against a scripted model (FakeModel), so no API key is needed.
 * Each test scripts what the model answers, then checks what your code sent.
 */
class AgentTests {
    // Any tool list will do for the loop's own tests.
    private static final List<ToolUnion> SOME_TOOLS = ToolSchemas.list(ToolSchemas.READ_FILE, ToolSchemas.WRITE_FILE, ToolSchemas.FIND_FILES);
    private final String originalWorkspace = Config.WORKSPACE;
    private final String originalLogDir = Config.LOG_DIR;
    private final String originalTestCommand = Config.TEST_COMMAND;
    private final int originalMaxToolCalls = Config.MAX_TOOL_CALLS;
    private final int originalMaxRevisions = Config.MAX_REVISIONS;
    private Path workspace;
    private Path logDir;
    private FakeModel model;

    @BeforeEach
    void setUp() throws Exception {
        // A workspace runner can enter too (temp folders are private to agent by default).
        workspace = Files.createTempDirectory("ws-", PosixFilePermissions.asFileAttribute(PosixFilePermissions.fromString("rwxr-xr-x")));
        Files.writeString(workspace.resolve("notes.txt"), "hello from the workspace");
        Config.WORKSPACE = workspace.toString();
        Config.TEST_COMMAND = "echo TESTS-RAN";
        model = new FakeModel();
        // A log file of its own per test, so a test can check what your code logged.
        logDir = Files.createTempDirectory("logs-");
        Config.LOG_DIR = logDir.toString();
        Log.start();
    }

    @AfterEach
    void tearDown() throws Exception {
        model.close();
        if (Files.isDirectory(workspace.resolve("repo"))) {
            Config.WORKSPACE = workspace.toString();
            Tools.runCommand("rm -rf repo"); // runner made it, so runner removes it
        }
        Config.WORKSPACE = originalWorkspace;
        Config.LOG_DIR = originalLogDir;
        Config.TEST_COMMAND = originalTestCommand;
        Config.MAX_TOOL_CALLS = originalMaxToolCalls;
        Config.MAX_REVISIONS = originalMaxRevisions;
        for (Path dir : List.of(workspace, logDir)) {
            try (var files = Files.walk(dir)) {
                files.sorted(java.util.Comparator.reverseOrder()).forEach(p -> p.toFile().delete());
            }
        }
    }

    @Test
    void todo3RunAgentReturnsTheFinalText() {
        model.says("All done.");

        String result = AgentLoop.runAgent("coder", "You are a test.", SOME_TOOLS, "Do the task.");

        assertTrue("All done.".equals(result), explain(
            "When the model stops asking for tools, runAgent should return its text: finalText(response).", result));
        assertTrue(model.requests.size() == 1, explain(
            "With no tool calls, runAgent should call the model exactly once, but it called it " + model.requests.size() + " times."));
        JsonNode request = model.request(0);
        assertTrue(request.path("model").asText().equals(Config.MODEL) && request.path("max_tokens").asInt() == Config.MAX_TOKENS, explain(
            "Send model(Config.MODEL) (\"" + Config.MODEL + "\") and maxTokens(Config.MAX_TOKENS) (" + Config.MAX_TOKENS + ").",
            "model " + request.path("model") + ", max_tokens " + request.path("max_tokens")));
        assertTrue(text(request.get("system")).equals("You are a test."), explain(
            "Send the systemPrompt argument as system(...).", text(request.get("system"))));
        assertTrue(toolNames(request).equals(List.of("read_file", "write_file", "find_files")), explain(
            "Send the tools you were given as tools(...).", toolNames(request)));
        JsonNode messages = request.path("messages");
        assertTrue(messages.size() == 1 && messages.get(0).path("role").asText().equals("user")
                && text(messages.get(0).get("content")).equals("Do the task."), explain(
            "A fresh conversation starts with one user message: the userMessage argument.", messages));
        assertTrue(request.has("cache_control"), explain(
            "Set cacheControl(CacheControlEphemeral.builder().build()) on the request, so the growing conversation is "
                + "cached between calls."));
    }

    @Test
    void todo3RunAgentRunsTheToolAndSendsTheResultBack() {
        model.callsTools("read_file", Map.of("path", "notes.txt")).says("I read it.");

        String result = AgentLoop.runAgent("coder", "You are a test.", SOME_TOOLS, "Read notes.txt.");

        assertTrue(model.requests.size() == 2, explain(
            "After a reply with stop_reason \"tool_use\", run the tool and call the model again. Expected 2 calls, got "
                + model.requests.size() + "."));
        assertTrue("I read it.".equals(result), explain(
            "Return the text of the last reply, the one after the tool result.", result));
        JsonNode messages = model.request(1).path("messages");
        assertTrue(roles(messages).equals("user,assistant,user"), explain(
            "The second call should send the whole conversation: your user message, the assistant turn, then a user "
                + "message with the tool result.", roles(messages)));
        assertTrue(sameBlocks(messages.get(1).path("content"), model.replies.get(0)), explain(
            "Append the assistant turn exactly as received, every block including the thinking block: "
                + "messages.add(response.toParam()).", messages.get(1).path("content")));
        List<JsonNode> results = toolResults(messages.get(2));
        String toolUseId = model.replies.get(0).get(1).path("id").asText();
        assertTrue(results.size() == 1 && results.get(0).path("tool_use_id").asText().equals(toolUseId), explain(
            "Answer the tool call with a tool_result whose toolUseId is the call's id (" + toolUseId + ").",
            messages.get(2).path("content")));
        assertTrue(text(results.get(0).get("content")).contains("hello from the workspace")
                && !results.get(0).path("is_error").asBoolean(false), explain(
            "The tool_result should carry what the tool returned (the text of notes.txt) and isError false: run it "
                + "with Tools.executeTool(call.name(), call._input()).", results.get(0)));
        String logged = logged(null, e -> e.path("event").asText() + "(" + e.path("role").asText() + ")");
        assertTrue(logged.equals("model_call(coder), tool_call(coder), tool_result(coder), model_call(coder)"), explain(
            "Log every step with the role you were given, so the trace shows it: Log.modelCall after each model call, "
                + "Log.toolCall before running a tool and Log.toolResult after it.", logged.isEmpty() ? "nothing logged" : "log: " + logged));
    }

    @Test
    void todo3RunAgentSendsAllResultsInOneMessage() {
        model.callsTools("read_file", Map.of("path", "notes.txt"), "read_file", Map.of("path", "missing.txt")).says("ok");

        AgentLoop.runAgent("coder", "You are a test.", SOME_TOOLS, "Read two files.");

        JsonNode messages = model.request(1).path("messages");
        assertTrue(messages.size() == 3, explain(
            "When the model asks for several tools in one turn, send ALL the results back in ONE user message, not "
                + "one message per result.", roles(messages)));
        List<JsonNode> results = toolResults(messages.get(2));
        List<String> ids = new ArrayList<>();
        model.replies.get(0).forEach(block -> { if (block.has("id")) ids.add(block.get("id").asText()); });
        List<String> resultIds = results.stream().map(r -> r.path("tool_use_id").asText()).toList();
        assertTrue(resultIds.equals(ids), explain(
            "There should be one tool_result per tool call, matched by id, in order (" + String.join(", ", ids) + ").",
            messages.get(2).path("content")));
        assertTrue(results.get(1).path("is_error").asBoolean(false), explain(
            "A tool that fails (missing.txt doesn't exist) is still answered, with isError true, so the model knows "
                + "it went wrong.", results.get(1)));
    }

    @Test
    void todo3RunAgentRefusesToolsItWasNotGiven() {
        model.callsTools("write_file", Map.of("path", "sneaky.txt", "content", "should not be written")).says("Tried.");

        AgentLoop.runAgent("planner", "You are a test.", ToolSchemas.list(ToolSchemas.READ_FILE, ToolSchemas.FIND_FILES), "Read only.");

        JsonNode second = model.request(1).path("messages");
        assertTrue(second.size() == 3, explain(
            "After refusing the tool, call the model again with your user message, the assistant turn, and one user "
                + "message with the tool results, the refusal among them.", roles(second)));
        List<JsonNode> results = toolResults(second.get(2));
        assertTrue(!Files.exists(workspace.resolve("sneaky.txt")), explain(
            "The model asked for write_file, which this role wasn't given, and your loop ran it. The tools you send "
                + "only tell the model what it may use; your code has to enforce it: run a tool only if "
                + "AgentLoop.offers(tools, call.name())."));
        assertTrue(results.size() == 1 && results.get(0).path("is_error").asBoolean(false)
                && text(results.get(0).get("content")).contains("not one of your tools"), explain(
            "Answer a tool the role wasn't given with an error result, \"Refused: write_file is not one of your "
                + "tools\", so the model knows, instead of running it.", results));
    }

    @Test
    void todo3RunAgentStopsAtTheToolCallCap() {
        Config.MAX_TOOL_CALLS = 3;
        model.whenScriptEnds = fake -> fake.toolCalls("read_file", Map.of("path", "notes.txt"));

        String result = AgentLoop.runAgent("coder", "You are a test.", SOME_TOOLS, "Keep reading.");

        assertTrue("[stopped: more than 3 tool calls]".equals(result), explain(
            "A model that never stops asking for tools must be stopped: after Config.MAX_TOOL_CALLS tool calls, "
                + "Log.capReached and return \"[stopped: more than N tool calls]\" with N = Config.MAX_TOOL_CALLS.", result));
        assertTrue(model.requests.size() == 4, explain(
            "With Config.MAX_TOOL_CALLS = 3, run 3 tools (3 calls to the model), and stop when the 4th reply asks for "
                + "another: 4 calls in total, but there were " + model.requests.size() + "."));
        String capped = logged("cap_reached", e -> e.path("role").asText() + " " + e.path("tool_calls").asText());
        assertTrue(capped.equals("coder 3"), explain(
            "Call Log.capReached(role, toolCalls) before you return, so the trace shows why the agent stopped, with "
                + "the number of tools that ran (3 here, Config.MAX_TOOL_CALLS). Logged 4? Then the count went up before "
                + "Log.capReached: \"if (toolCalls++ == Config.MAX_TOOL_CALLS)\" compares 3 but leaves 4. Compare with "
                + "toolCalls == Config.MAX_TOOL_CALLS, and do toolCalls++ after the if.",
            "cap_reached logged (role, tool_calls): " + capped));
    }

    @Test
    void todo3RunAgentCountsToolsNotModelCalls() {
        Config.MAX_TOOL_CALLS = 3;
        model.whenScriptEnds = fake -> fake.toolCalls("read_file", Map.of("path", "notes.txt"), "read_file", Map.of("path", "notes.txt"));

        String result = AgentLoop.runAgent("coder", "You are a test.", SOME_TOOLS, "Keep reading, two at a time.");

        String calls = logged("tool_call", e -> "x");
        int ran = calls.isEmpty() ? 0 : calls.split(", ").length;
        assertTrue(ran > 0, explain(
            "No tool_call in the log: this test counts the tools you ran from it. Log every tool before running it (Log.toolCall), then the cap is checked."));
        assertTrue("[stopped: more than 3 tool calls]".equals(result) && ran == 3, explain(
            "The cap counts tools, not model calls: here each reply asks for two tools, so stop after the 3rd tool, "
                + "in the middle of the 2nd reply. Count each tool you run (toolCalls++ per tool_use block).",
            "result \"" + result + "\", tools run: " + ran));
    }

    @Test
    void todo3CodeSendsTaskPlanAndFeedback() {
        model.says("Changed it.");

        String summary = Orchestrator.code("THE TASK", "THE PLAN", "THE FEEDBACK");

        assertTrue(model.requests.size() == 1, explain("code should call the model once here.", model.requests.size()));
        JsonNode request = model.request(0);
        String message = text(request.path("messages").get(0).get("content"));
        assertTrue("Changed it.".equals(summary), explain("code should return what runAgent returns.", summary));
        assertTrue(text(request.get("system")).equals(Prompts.CODER), explain(
            "code should call AgentLoop.runAgent with \"coder\" and Prompts.CODER as the system prompt.",
            text(request.get("system"))));
        assertTrue(toolNames(request).containsAll(List.of("read_file", "write_file")), explain(
            "The Coder needs at least ToolSchemas.READ_FILE and WRITE_FILE (FIND_FILES and SEARCH_TEXT help it find code; run_tests "
                + "follows in TODO 4).",
            toolNames(request)));
        assertTrue(message.contains("THE TASK") && message.contains("THE PLAN") && message.contains("THE FEEDBACK"), explain(
            "The Coder's user message should contain the task, the plan and, when there is some, the reviewer's feedback.",
            message));

        model.says("Coded it.");
        Orchestrator.code("THE TASK", "THE PLAN", null);

        String first = text(model.request(1).path("messages").get(0).get("content"));
        assertTrue(first.contains("THE TASK") && first.contains("THE PLAN") && !first.contains("null"), explain(
            "On the first attempt there is no feedback (feedback is null): send the task and the plan, and leave the "
                + "feedback part out instead of sending \"null\".", first));
    }

    @Test
    void todo4CodeCanRunTheTests() {
        model.says("Changed it.");

        Orchestrator.code("THE TASK", "THE PLAN", null);

        assertTrue(toolNames(model.request(0)).contains("run_tests"), explain(
            "Give the Coder run_tests: add ToolSchemas.RUN_TESTS to its tools in Orchestrator.code (TODO 4). Without it "
                + "the Coder can't check its own work.", toolNames(model.request(0))));
    }

    // The tools that change files or run commands. A role that only reads gets none of them.
    static final List<String> WRITERS = List.of("write_file", "edit_file", "run_command");

    static boolean onlyReads(JsonNode request) {
        return toolNames(request).contains("read_file") && toolNames(request).stream().noneMatch(WRITERS::contains);
    }

    @Test
    void todo5PlanAsksThePlanner() {
        model.says("1. Do it.");

        String plan = Orchestrator.plan("THE TASK");

        assertTrue(model.requests.size() == 1, explain("plan should call the model once here.", model.requests.size()));
        JsonNode request = model.request(0);
        assertTrue("1. Do it.".equals(plan), explain("plan should return the Planner's answer.", plan));
        assertTrue(text(request.get("system")).equals(Prompts.PLANNER), explain(
            "plan should call runAgent as \"planner\" with Prompts.PLANNER as the system prompt.", text(request.get("system"))));
        assertTrue(onlyReads(request), explain(
            "The Planner only reads: give it ToolSchemas.READ_FILE (FIND_FILES and SEARCH_TEXT help it search), and nothing that "
                + "changes files or runs commands (write_file, run_command, an edit_file of your own).",
            toolNames(request)));
        assertTrue(text(request.path("messages").get(0).get("content")).contains("THE TASK"), explain(
            "The Planner's user message should contain the task.", text(request.path("messages").get(0).get("content"))));
    }

    @Test
    void todo6ReviewChangesGivesTheReviewerEverything() throws Exception {
        model.says("APPROVED\nAll good.");
        gitRepoWithANewFile();

        Orchestrator.Review review = Orchestrator.reviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0);

        assertTrue(model.requests.size() == 1, explain("reviewChanges should call the model once here.", model.requests.size()));
        JsonNode request = model.request(0);
        String message = text(request.path("messages").get(0).get("content"));
        assertTrue(review.approved() && review.text().equals("APPROVED\nAll good."), explain(
            "A review whose first line is APPROVED should come back as approved = true, with the full review text.", review));
        assertTrue(text(request.get("system")).equals(Prompts.REVIEWER)
                && onlyReads(request), explain(
            "Call runAgent as \"reviewer\" with Prompts.REVIEWER and tools that only read: ToolSchemas.READ_FILE (FIND_FILES "
                + "and SEARCH_TEXT help), nothing that changes files or runs commands. run_tests is fine.",
            toolNames(request)));
        assertTrue(message.contains("THE TASK") && message.contains("THE PLAN") && message.contains("THE SUMMARY"), explain(
            "The Reviewer's message should contain the task, the plan and the Coder's summary.", message));
        assertTrue(message.contains("THE CRITERIA"), explain(
            "The Reviewer's message should contain the review criteria (the criteria parameter): they say what to check.",
            message));
        assertTrue(message.contains("TESTS-RAN"), explain(
            "The Reviewer should get the real test output: run the tests with Tools.runTests() and include "
                + "its output (the test sets Config.TEST_COMMAND to \"echo TESTS-RAN\").", message));
        assertTrue(message.contains("NEW-FILE-CONTENT"), explain(
            "The Reviewer should also get the git diff, including new files: run \"git add -N . && git diff\" with "
                + "Tools.runCommand and include its output. (Plain git diff leaves out files git doesn't track yet, "
                + "like the new test file the Coder wrote here.)", message));
        assertTrue(!message.contains("isError"), explain(
            "Give the Reviewer the commands' output text (result.output()), not the ToolResult itself: the message "
                + "contains \"ToolResult[output=..., isError=...]\".", message));
    }

    @Test
    void todo6ReviewChangesUsesParseVerdict() throws Exception {
        model.says("APPROVED with two small notes\n- naming").says("\n  APPROVED\nAll good.");

        Orchestrator.Review chatty = Orchestrator.reviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0);
        Orchestrator.Review padded = Orchestrator.reviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 1);

        assertTrue(!chatty.approved(), explain(
            "The Reviewer answered \"APPROVED with two small notes\": its first line isn't exactly APPROVED, so that's "
                + "CHANGES_REQUIRED. Decide with parseVerdict(text), not with your own check (startsWith accepts this).",
            chatty));
        assertTrue(padded.approved(), explain(
            "The Reviewer answered with a blank line, then \"  APPROVED\": parseVerdict trims that, so it's approved. "
                + "Decide with parseVerdict(text), not with your own check.",
            padded));
        String verdicts = logged("verdict", e -> e.path("revision").asText() + " " + e.path("verdict").asText());
        assertTrue(verdicts.equals("0 CHANGES_REQUIRED, 1 APPROVED"), explain(
            "Log each verdict so the trace shows it: Log.verdict(revision, verdict) with what parseVerdict returned.",
            "verdicts logged: " + verdicts));
    }

    @Test
    void todo6ReviewChangesChangesRequiredIsNotApproved() throws Exception {
        model.says("CHANGES_REQUIRED\n- divide truncates");

        Orchestrator.Review review = Orchestrator.reviewChanges("THE TASK", "THE CRITERIA", "THE PLAN", "THE SUMMARY", 0);

        assertTrue(!review.approved() && review.text().equals("CHANGES_REQUIRED\n- divide truncates"), explain(
            "A CHANGES_REQUIRED review should come back as approved = false, with the full review text: the Coder "
                + "gets it as feedback.", review));
    }

    @Test
    void todo7RunRevisesUntilApproved() throws Exception {
        model.says("1. THE PLAN").says("Coded it.").says("CHANGES_REQUIRED\n- REVIEW-ONE").says("Fixed it.").says("APPROVED");

        boolean approved = Orchestrator.run("THE TASK", "THE CRITERIA");

        String roles = model.requests.stream().map(AgentTests::roleOf).collect(Collectors.joining(" → "));
        assertTrue(roles.equals("planner → coder → reviewer → coder → reviewer"), explain(
            "run should plan once, then Coder and Reviewer, and after CHANGES_REQUIRED the Coder and Reviewer again.", roles));
        assertTrue(approved, explain("run should return true once the Reviewer approves.", approved));
        String secondCoder = text(model.request(3).path("messages").get(0).get("content"));
        assertTrue(secondCoder.contains("REVIEW-ONE") && secondCoder.contains("THE PLAN"), explain(
            "The second Coder run should get the plan and the full review as feedback.", secondCoder));
        String sawCriteria = model.requests.stream()
            .map(r -> roleOf(r) + ":" + (text(r.path("messages").get(0).get("content")).contains("THE CRITERIA") ? "yes" : "no"))
            .collect(Collectors.joining(" → "));
        assertTrue(sawCriteria.equals("planner:no → coder:no → reviewer:yes → coder:no → reviewer:yes"), explain(
            "Only the Reviewer gets the review criteria: pass criteria to reviewChanges, not to plan or code.",
            sawCriteria));
        expectFlowLogged(true);
    }

    @Test
    void todo7RunStopsAtTheRevisionLimit() throws Exception {
        Config.MAX_REVISIONS = 1;
        model.says("1. THE PLAN");
        model.whenScriptEnds = fake -> FakeModel.JSON.createArrayNode().add(FakeModel.textBlock(
            fake.requests.size() % 2 == 0 ? "Coded it." : "CHANGES_REQUIRED\n- still wrong"));

        boolean approved = Orchestrator.run("THE TASK", "THE CRITERIA");

        String roles = model.requests.stream().map(AgentTests::roleOf).collect(Collectors.joining(" → "));
        assertTrue(!approved, explain("run should return false when the revision limit is reached without approval.", approved));
        assertTrue(roles.equals("planner → coder → reviewer → coder → reviewer"), explain(
            "With Config.MAX_REVISIONS = 1 the Coder runs twice (the first attempt plus one revision), then run stops.", roles));
        expectFlowLogged(false);
    }

    // Both run tests take the same path: plan, code, review, code again, review again.
    // A git repo, made as runner (who runs git in the real workspace), with one committed file and
    // one new, untracked file, as after the Coder added a test file.
    private void gitRepoWithANewFile() throws Exception {
        Files.setPosixFilePermissions(workspace, PosixFilePermissions.fromString("rwxrwxrwx"));
        var made = Tools.runCommand(
            "mkdir repo && cd repo && git init -q && echo old > a.txt && git add a.txt "
                + "&& git -c user.name=t -c user.email=t@t commit -qm init && echo NEW-FILE-CONTENT > new_test.txt");
        assertTrue(made.output().contains("exit code 0"), made.output());
        Config.WORKSPACE = workspace.resolve("repo").toString();
    }

    private void expectFlowLogged(boolean approved) {
        String transitions = logged("transition",
            e -> e.path("from").asText() + "→" + e.path("to").asText() + " " + e.path("revision").asText());
        assertTrue(transitions.equals("start→planner 0, planner→coder 0, coder→reviewer 0, reviewer→coder 1, "
                + "coder→reviewer 1, reviewer→end 1"), explain(
            "Log each step of the flow with Log.transition(from, to, revision), as the steps in run list them.",
            "transitions logged: " + transitions));
        String summary = logged("run_summary",
            e -> "approved " + e.path("approved").asText() + ", revisions " + e.path("revisions").asText());
        assertTrue(summary.equals("approved " + approved + ", revisions 1"), explain(
            "End the run with Log.runSummary(" + approved + ", revision), once.", "run_summary logged: " + summary));
    }

    // The events of one kind (all when kind is null) in this test's log, each shown with show, comma-separated.
    private String logged(String kind, Function<JsonNode, String> show) {
        List<String> shown = new ArrayList<>();
        try (var files = Files.list(logDir)) {
            for (String line : Files.readAllLines(files.findFirst().orElseThrow())) {
                if (line.isBlank()) continue;
                JsonNode event = FakeModel.JSON.readTree(line);
                if (kind == null || event.path("event").asText().equals(kind)) shown.add(show.apply(event));
            }
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
        return String.join(", ", shown);
    }

    private static String roles(JsonNode messages) {
        List<String> roles = new ArrayList<>();
        messages.forEach(m -> roles.add(m.path("role").asText()));
        return String.join(",", roles);
    }

    private static List<JsonNode> toolResults(JsonNode message) {
        List<JsonNode> results = new ArrayList<>();
        message.path("content").forEach(block -> { if (block.path("type").asText().equals("tool_result")) results.add(block); });
        return results;
    }

    private static boolean sameBlocks(JsonNode sent, ArrayNode received) {
        if (!sent.isArray() || sent.size() != received.size()) return false;
        for (int i = 0; i < sent.size(); i++) {
            if (!essentials(sent.get(i)).equals(essentials(received.get(i)))) return false;
        }
        return true;
    }

    private static String roleOf(JsonNode request) {
        String system = text(request.get("system"));
        if (system.equals(Prompts.PLANNER)) return "planner";
        if (system.equals(Prompts.CODER)) return "coder";
        if (system.equals(Prompts.REVIEWER)) return "reviewer";
        return "?";
    }
}
