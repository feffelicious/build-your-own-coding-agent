package codingagent;

import codingagent.support.Config;
import codingagent.tools.Tools;

import static codingagent.Explain.explain;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

/**
 * TODO 4. runTests and runCommand are provided, but they shorten their output with your
 * truncate, so their tests pass once that is done. They run real commands as the runner
 * user, so they only pass inside the workshop container.
 */
class RunCommandTests {
    @Test
    void todo4RunTestsRunsTheTestCommand() {
        String original = Config.TEST_COMMAND;
        Config.TEST_COMMAND = "echo TESTS-RAN; exit 1";
        try {
            var result = Tools.executeTool("run_tests", com.anthropic.core.JsonValue.from(java.util.Map.of()));

            assertTrue(!result.isError() && result.output().startsWith("exit code 1") && result.output().contains("TESTS-RAN"), explain(
                "run_tests runs Config.TEST_COMMAND and returns its exit code and output; failing tests are a normal "
                    + "result the model reads, not an error.", result));
        } finally {
            Config.TEST_COMMAND = original;
        }
    }

    @Test
    void todo4RunCommandReportsExitCodeAndOutput() throws Exception {
        var result = Tools.runCommand("echo out; echo err >&2; exit 3");

        assertTrue(!result.isError(), explain(
            "A non-zero exit code is a normal result, not an error: isError should be false, so the model can read "
                + "failing test output and fix the code.",
            result));
        assertTrue(result.output().startsWith("exit code 3"), explain(
            "The output should start with \"exit code 3\" (the command ran \"exit 3\"), followed by what it printed.",
            result));
        assertTrue(result.output().contains("out") && result.output().contains("err"), explain(
            "The output should contain both stdout (\"out\") and stderr (\"err\"): use redirectErrorStream(true).",
            result));
    }

    @Test
    void todo4RunCommandRunsInWorkspaceAsRunner() throws Exception {
        var result = Tools.runCommand("pwd; whoami");

        assertTrue(result.output().contains(Config.WORKSPACE), explain(
            "Commands should run in the workspace (" + Config.WORKSPACE + "): set the ProcessBuilder's directory.",
            result));
        assertTrue(result.output().contains("runner"), explain(
            "Commands should run as the runner user, not as agent: start them with sudo -u runner timeout ... /bin/sh -c <command>.",
            result));
    }

    @Test
    void todo4RunCommandKillsCommandAndChildrenOnTimeout() throws Exception {
        long started = System.nanoTime();
        var result = Tools.runCommand("sleep 100 & sleep 100");
        double seconds = (System.nanoTime() - started) / 1e9;

        assertTrue(result.isError(), explain(
            "A command that runs too long should come back as an error result: GNU timeout exits with 124 "
                + "(or 137 if it had to kill), so treat those exit codes as a timeout.",
            result));
        assertTrue(seconds >= Config.COMMAND_TIMEOUT_S - 1 && seconds <= Config.COMMAND_TIMEOUT_S + 3, explain(
            String.format("It should give up after about Config.COMMAND_TIMEOUT_S (%d s), but it took %.1f s. "
                + "Run it under timeout -k 2 %d, and don't wait for the output pipes forever.",
                Config.COMMAND_TIMEOUT_S, seconds, Config.COMMAND_TIMEOUT_S)));
        var left = Tools.runCommand("pgrep -u runner -fx 'sleep 100' || echo none-left");
        assertTrue(left.output().contains("none-left"), explain(
            "No \"sleep 100\" may still be running afterwards: GNU timeout kills the command's whole process group, "
                + "background children included.",
            left));
    }

    @Test
    void todo4RunCommandTruncatesLongOutput() throws Exception {
        var result = Tools.runCommand("seq 1 20000");
        int length = result.output().length();

        assertTrue(result.output().contains("[truncated"), explain(
            "Long output should go through truncate, which puts a [truncated ...] marker in the middle.",
            result));
        assertTrue(length >= Config.OUTPUT_LIMIT && length <= Config.OUTPUT_LIMIT + 100, explain(
            "The output should be about Config.OUTPUT_LIMIT (" + Config.OUTPUT_LIMIT + ") characters long, but it is "
                + length + "."));
        assertTrue(result.output().startsWith("exit code 0\n1\n2\n") && result.output().endsWith("19999\n20000\n"), explain(
            "The output should be \"exit code 0\", a newline, then the truncated text: the start and the end of what "
                + "the command printed must both survive.",
            result));
    }

    @Test
    void todo4RunCommandEnvironmentHasNoRealKey() throws Exception {
        var result = Tools.runCommand("env");

        assertTrue(!result.output().contains("sk-ant") && !result.output().contains("ANTHROPIC_API_KEY"), explain(
            "The command's environment must not contain the API key: run it with sudo -u runner, which starts from a "
                + "clean environment, rather than passing yours on.",
            result));
    }

    @Test
    void todo4RunCommandCannotReadAgentSource() throws Exception {
        var result = Tools.runCommand("cat /home/agent/app/pom.xml");

        assertTrue(!result.output().contains("exit code 0") && result.output().contains("Permission denied"), explain(
            "The runner user must not be able to read /home/agent. If this worked, the command ran as agent: "
                + "start it with sudo -u runner.",
            result));
    }

    @Test
    void todo4TruncateKeepsFirst2000AndLast8000() {
        String text = numberedLines(5000);   // 30,000 characters: "00000\n00001\n...04999\n"
        String head = text.substring(0, 2000);
        String tail = text.substring(text.length() - 8000);

        String result = Tools.truncate(text);

        assertTrue(result.startsWith(head), explain(
            "truncate should keep the first 2,000 characters unchanged, so the result should start with "
                + describe(head) + ".",
            describe(result)));
        assertTrue(!result.contains("\n02500\n"), explain(
            "The middle of the text should be left out, but your result still contains line 02500 from the middle. "
                + "If your second part is text.substring(8000), that is everything from index 8,000 to the end; the "
                + "last 8,000 characters are text.substring(text.length() - 8000).",
            describe(result)));
        assertTrue(result.endsWith(tail), explain(
            "truncate should end with the last 8,000 characters: text.substring(text.length() - 8000), so "
                + describe(tail) + ". Careful: text.substring(8000) is everything from index 8,000 to the end.",
            describe(result)));
        assertTrue(result.contains("[truncated"), explain(
            "Put a [truncated ...] marker between the two parts, so the model knows something was left out.",
            describe(result)));
        assertTrue(result.length() >= 10_000 && result.length() <= 10_100, explain(
            "The result should be the 2,000 + 8,000 characters you kept plus the marker: about Config.OUTPUT_LIMIT ("
                + Config.OUTPUT_LIMIT + ") characters.",
            describe(result)));
    }

    @Test
    void todo4TruncateOnlyTruncatesTextOverTheLimit() {
        String atLimit = "x".repeat(Config.OUTPUT_LIMIT);
        String overLimit = atLimit + "y";

        String shortResult = Tools.truncate("short");
        String atLimitResult = Tools.truncate(atLimit);
        String overLimitResult = Tools.truncate(overLimit);

        assertTrue(shortResult.equals("short"), explain("Short text should come back unchanged.", shortResult));
        assertTrue(atLimitResult.equals(atLimit), explain(
            "Text of exactly Config.OUTPUT_LIMIT (" + Config.OUTPUT_LIMIT + ") characters should come back unchanged: "
                + "only longer text is truncated.",
            describe(atLimitResult)));
        assertTrue(overLimitResult.contains("[truncated"), explain(
            "Text of Config.OUTPUT_LIMIT + 1 (" + (Config.OUTPUT_LIMIT + 1) + ") characters is over the limit and "
                + "should be truncated.",
            describe(overLimitResult)));
    }

    /** "00000\n00001\n..." so any piece of it shows where in the text it came from. */
    private static String numberedLines(int count) {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < count; i++) text.append(String.format("%05d\n", i));
        return text.toString();
    }

    /** A long string as its length plus how it starts and ends. */
    private static String describe(String text) {
        String shown = text.length() <= 60
            ? text
            : text.substring(0, 24) + "…" + text.substring(text.length() - 24);
        return "\"" + shown.replace("\n", "\\n") + "\" (" + text.length() + " characters)";
    }
}
