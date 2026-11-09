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
        // TODO 1: describe read_file for the model
        // Hint: Say what it does and returns, and what the model must know to call it right: the path is relative to the workspace root, text files only. FindFiles below is an example.
        Description = "TODO 1: describe read_file",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                // TODO 1: describe the path parameter
                // Hint: Say what the value is, e.g. relative to what.
                ["path"] = Schema(new { type = "string", description = "TODO 1: describe the path parameter" }),
            },
            Required = ["path"],
        },
    };

    // Test: run-tests --filter Todo2_Description
    public static readonly Tool WriteFile = new()
    {
        Name = "write_file",
        // TODO 2: describe write_file for the model
        // Hint: Say that it creates or overwrites the whole file (a model that thinks it patches sends only the changed part, and the rest of the file is gone), that parent folders are created, and that the path is relative to the workspace root.
        Description = "TODO 2: describe write_file",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                // TODO 2: describe the path and content parameters
                // Hint: Say what each value is; for content, that it is the complete new file content.
                ["path"] = Schema(new { type = "string", description = "TODO 2: describe the path parameter" }),
                ["content"] = Schema(new { type = "string", description = "TODO 2: describe the content parameter" }),
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
        // TODO 4: describe run_tests for the model
        // Hint: Read Tools.RunTests and Tools.RunCommand first (provided), then say what it runs, what comes back (the exit code, 0 when all tests pass, and the output, shortened in the middle when long), that build and syntax errors show up there too, and when to use it. Then give it to the Coder: add ToolSchemas.RunTests to its tools in Orchestrator.Code, and tell it in its prompt to run the tests before it finishes.
        Description = "TODO 4: describe run_tests",
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
