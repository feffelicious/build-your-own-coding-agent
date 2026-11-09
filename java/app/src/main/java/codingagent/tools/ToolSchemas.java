package codingagent.tools;

import com.anthropic.core.JsonValue;
import com.anthropic.models.messages.Tool;
import com.anthropic.models.messages.ToolUnion;
import java.util.Arrays;
import java.util.List;
import java.util.Map;

/**
 * What the model sees of each tool: a name, a description and a JSON schema for
 * the input. The model never runs anything; it only asks us to (see Tools.java).
 * The descriptions are prompts too: the model knows a tool only from them. FIND_FILES and
SEARCH_TEXT, at the bottom, are finished examples. Which role gets which tools is up to
 * you: you pass each role its list in Orchestrator.java.
 */
public final class ToolSchemas {
    private ToolSchemas() {}

    // Test: run-tests -Dtest='*#todo1Description*'
    public static final Tool READ_FILE = Tool.builder()
        .name("read_file")
        .description("Read a text file from the workspace. The path is relative to the workspace root.")
        .inputSchema(Tool.InputSchema.builder()
            .properties(Tool.InputSchema.Properties.builder()
                .putAdditionalProperty("path", string("File path relative to the workspace root"))
                .build())
            .required(List.of("path"))
            .build())
        .build();

    // Test: run-tests -Dtest='*#todo2Description*'
    public static final Tool WRITE_FILE = Tool.builder()
        .name("write_file")
        .description("Create or overwrite a text file in the workspace with the given content. "
            + "Parent directories are created. The path is relative to the workspace root.")
        .inputSchema(Tool.InputSchema.builder()
            .properties(Tool.InputSchema.Properties.builder()
                .putAdditionalProperty("path", string("File path relative to the workspace root"))
                .putAdditionalProperty("content", string("The complete new file content"))
                .build())
            .required(List.of("path", "content"))
            .build())
        .build();

    // No parameters: the model can't choose the command. It writes the tests, though, and a test can do
// anything a command can: what keeps that in check is in Tools.runCommand (the runner user).
    // Test: run-tests -Dtest='*#todo4Description*+todo4Code*'
    // Then try it: reset-workspace, then run-agent --coder, and compare the trace with your first run
    public static final Tool RUN_TESTS = Tool.builder()
        .name("run_tests")
        // TODO 4: describe run_tests for the model
        // Hint: Read Tools.runTests and Tools.runCommand first (provided), then say what it runs, what comes back (the exit code, 0 when all tests pass, and the output, shortened in the middle when long), that build and syntax errors show up there too, and when to use it. Then give it to the Coder: add ToolSchemas.RUN_TESTS to its tools in Orchestrator.code, and tell it in its prompt to run the tests before it finishes.
        .description("TODO 4: describe run_tests")
        .inputSchema(Tool.InputSchema.builder()
            .properties(Tool.InputSchema.Properties.builder().build())
            .build())
        .build();

    // Provided, for the run_command extra (CHEATSHEET.md): no role has it until you add it.
    public static final Tool RUN_COMMAND = Tool.builder()
        .name("run_command")
        .description("Run a shell command (/bin/sh) in the workspace root. Returns the exit code and the "
            + "combined output. Commands time out after 30 seconds. Use it to build and run tests.")
        .inputSchema(Tool.InputSchema.builder()
            .properties(Tool.InputSchema.Properties.builder()
                .putAdditionalProperty("command", string("The shell command to run"))
                .build())
            .required(List.of("command"))
            .build())
        .build();

    // Provided, like its code in Tools.java (the read-only roles have no other way to see which
    // files exist), and an example of a description: what it does, how to call it, what comes back, when to use it.
    public static final Tool FIND_FILES = Tool.builder()
        .name("find_files")
        .description("Find files in the workspace whose path matches a pattern: * matches within one folder, "
            + "** across folders, ? one character. Returns the matching paths relative to the workspace root, one per "
            + "line. Use it to see which files exist before reading them.")
        .inputSchema(Tool.InputSchema.builder()
            .properties(Tool.InputSchema.Properties.builder()
                .putAdditionalProperty("pattern", string("Pattern relative to the workspace root, e.g. **/*.md or src/**"))
                .build())
            .required(List.of("pattern"))
            .build())
        .build();

    // Provided, like FIND_FILES: finds where something is used or defined without reading every file.
    public static final Tool SEARCH_TEXT = Tool.builder()
        .name("search_text")
        .description("Search the text files in the workspace for lines matching a regular expression. Returns "
            + "the matching lines as path:line number: text, at most 100. Use it to find where something is "
            + "defined or used before reading whole files.")
        .inputSchema(Tool.InputSchema.builder()
            .properties(Tool.InputSchema.Properties.builder()
                .putAdditionalProperty("pattern", string("Regular expression to search for, e.g. class \\w+Test or divide\\("))
                .build())
            .required(List.of("pattern"))
            .build())
        .build();

    /** The tools in a list as runAgent takes it, e.g. ToolSchemas.list(READ_FILE, FIND_FILES). Provided. */
    public static List<ToolUnion> list(Tool... tools) {
        return Arrays.stream(tools).map(ToolUnion::ofTool).toList();
    }

    private static JsonValue string(String description) {
        return JsonValue.from(Map.of("type", "string", "description", description));
    }
}
