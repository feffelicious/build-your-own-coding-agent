namespace CodingAgent;

// Every setting in one place. Each value can be overridden by an environment
// variable with the same name, e.g. MAX_TOOL_CALLS=3 run-agent
public static class Config
{
    public static string Model = Text("MODEL", "claude-haiku-5-5");
    public static int MaxTokens = Number("MAX_TOKENS", 4000);
    public static int MaxToolCalls = Number("MAX_TOOL_CALLS", 20);
    public static int MaxRevisions = Number("MAX_REVISIONS", 2);
    public static string Workspace = Text("WORKSPACE", "/workspace");
    public static string TestCommand = Text("TEST_COMMAND", "dotnet test");
    public static int CommandTimeoutS = Number("COMMAND_TIMEOUT_S", 30);
    public static int OutputLimit = Number("OUTPUT_LIMIT", 10_000);   // keep the first fifth and the last four fifths
    public static int FileLimit = Number("FILE_LIMIT", 100 * 1024);   // bytes
    public static string LogDir = Text("LOG_DIR", "/home/agent/logs");

    // Our own wait on a command, in case GNU timeout itself hangs.
    public static int CommandBackstopS => CommandTimeoutS + 5;

    static string Text(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    static int Number(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
}
