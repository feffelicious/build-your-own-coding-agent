package codingagent.support;

/**
 * Every setting in one place. Each value can be overridden by an environment
 * variable with the same name, e.g. MAX_TOOL_CALLS=3 run-agent
 */
public final class Config {
    public static String MODEL = text("MODEL", "claude-haiku-5-5");
    public static int MAX_TOKENS = number("MAX_TOKENS", 4000);
    public static int MAX_TOOL_CALLS = number("MAX_TOOL_CALLS", 20);
    public static int MAX_REVISIONS = number("MAX_REVISIONS", 2);
    public static String WORKSPACE = text("WORKSPACE", "/workspace");
    public static String TEST_COMMAND = text("TEST_COMMAND", "mvn -q -o test");
    public static int COMMAND_TIMEOUT_S = number("COMMAND_TIMEOUT_S", 30);
    public static int OUTPUT_LIMIT = number("OUTPUT_LIMIT", 10_000);   // keep the first fifth and the last four fifths
    public static int FILE_LIMIT = number("FILE_LIMIT", 100 * 1024);   // bytes
    public static String LOG_DIR = text("LOG_DIR", "/home/agent/logs");

    private Config() {}

    /** Our own wait on a command, in case GNU timeout itself hangs. */
    public static int commandBackstopS() {
        return COMMAND_TIMEOUT_S + 5;
    }

    private static String text(String name, String fallback) {
        String value = System.getenv(name);
        return value == null || value.isEmpty() ? fallback : value;
    }

    private static int number(String name, int fallback) {
        try {
            return Integer.parseInt(System.getenv(name));
        } catch (NumberFormatException e) {
            return fallback;
        }
    }
}
