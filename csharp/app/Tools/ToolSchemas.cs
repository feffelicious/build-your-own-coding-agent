using System.Text.Json;
using Anthropic.Models.Messages;

namespace CodingAgent;

// What the model sees of each tool: a name, a description and a JSON schema for
// the input. The model never runs anything; it only asks us to (see Tools.cs).
// The descriptions are prompts too: the model knows a tool only from them. FindFiles and
// SearchText, at the bottom, are finished examples. Which role gets which tools is up to
// you: you pass each role its list in Orchestrator.cs.
public static class ToolSchemas
{
    // Test: run-tests --filter Todo1_Description
    public static readonly Tool ReadFile = new()
    {
        Name = "read_file",
        Description = "Read a text file from the workspace. The path is relative to the workspace root.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["path"] = Schema(new { type = "string", description = "File path relative to the workspace root" }),
            },
            Required = ["path"],
        },
    };

    // Test: run-tests --filter Todo2_Description
    public static readonly Tool WriteFile = new()
    {
        Name = "write_file",
        Description = "Create or overwrite a text file in the workspace with the given content. "
            + "Parent directories are created. The path is relative to the workspace root.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["path"] = Schema(new { type = "string", description = "File path relative to the workspace root" }),
                ["content"] = Schema(new { type = "string", description = "The complete new file content" }),
            },
            Required = ["path", "content"],
        },
    };

    // No parameters: the model can't choose the command. It writes the tests, though, and a test can do
// anything a command can: what keeps that in check is in Tools.RunCommand (the runner user).
    // Test: run-tests --filter "Todo4_Description|Todo4_Code"
    // Then try it: reset-workspace, then run-agent --coder, and compare the trace with your first run
    public static readonly Tool RunTests = new()
    {
        Name = "run_tests",
        Description = "Run the project's tests in the workspace. Returns the exit code (0 means all tests "
            + "passed) and the combined output, with the middle cut out when it is long. Build and syntax errors "
            + "show up here too. Run it after every change and before you finish.",
        InputSchema = new() { Properties = new Dictionary<string, JsonElement>() },
    };

    // Provided, for the run_command extra (CHEATSHEET.md): no role has it until you add it.
    public static readonly Tool RunCommand = new()
    {
        Name = "run_command",
        Description = "Run a shell command (/bin/sh) in the workspace root. Returns the exit code and the "
            + "combined output. Commands time out after 30 seconds. Use it to build and run tests.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["command"] = Schema(new { type = "string", description = "The shell command to run" }),
            },
            Required = ["command"],
        },
    };

    // Provided, like its code in Tools.cs (the read-only roles have no other way to see which
    // files exist), and an example of a description: what it does, how to call it, what comes back, when to use it.
    public static readonly Tool FindFiles = new()
    {
        Name = "find_files",
        Description = "Find files in the workspace whose path matches a pattern: * matches within one folder, "
            + "** across folders, ? one character. Returns the matching paths relative to the workspace root, one per "
            + "line. Use it to see which files exist before reading them.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["pattern"] = Schema(new { type = "string", description = "Pattern relative to the workspace root, e.g. **/*.md or src/**" }),
            },
            Required = ["pattern"],
        },
    };

    // Provided, like FindFiles: finds where something is used or defined without reading every file.
    public static readonly Tool SearchText = new()
    {
        Name = "search_text",
        Description = "Search the text files in the workspace for lines matching a regular expression. Returns "
            + "the matching lines as path:line number: text, at most 100. Use it to find where something is "
            + "defined or used before reading whole files.",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["pattern"] = Schema(new { type = "string", description = "Regular expression to search for, e.g. class \\w+Test or divide\\(" }),
            },
            Required = ["pattern"],
        },
    };

    static JsonElement Schema(object value) => JsonSerializer.SerializeToElement(value);
}
