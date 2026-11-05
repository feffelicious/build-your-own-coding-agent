using System.Diagnostics;
using CodingAgent;

namespace CodingAgent.Tests;

// TODO 4. run_tests and run_command are provided, but they shorten their output with your
// Truncate, so their tests pass once that is done. They run real commands as the runner
// user, so they only pass inside the workshop container.
public class RunCommandTests
{
    [Fact]
    public void Todo4_RunTests_RunsTheTestCommand()
    {
        var original = Config.TestCommand;
        Config.TestCommand = "echo TESTS-RAN; exit 1";
        try
        {
            var result = Tools.ExecuteTool("run_tests", new Dictionary<string, System.Text.Json.JsonElement>());

            Expect.That(!result.IsError && result.Output.StartsWith("exit code 1") && result.Output.Contains("TESTS-RAN"),
                "run_tests runs Config.TestCommand and returns its exit code and output; failing tests are a normal "
                + "result the model reads, not an error.",
                result);
        }
        finally
        {
            Config.TestCommand = original;
        }
    }

    [Fact]
    public void Todo4_RunCommand_ReportsExitCodeAndOutput()
    {
        var result = Tools.RunCommand("echo out; echo err >&2; exit 3");

        Expect.That(!result.IsError,
            "A non-zero exit code is a normal result, not an error: IsError should be false, so the model can read "
            + "failing test output and fix the code.",
            result);
        Expect.That(result.Output.StartsWith("exit code 3"),
            "The output should start with \"exit code 3\" (the command ran \"exit 3\"), followed by what it printed.",
            result);
        Expect.That(result.Output.Contains("out") && result.Output.Contains("err"),
            "The output should contain both stdout (\"out\") and stderr (\"err\"): read both streams, concurrently, "
            + "or the process can block on a full pipe.",
            result);
    }

    [Fact]
    public void Todo4_RunCommand_RunsInWorkspaceAsRunner()
    {
        var result = Tools.RunCommand("pwd; whoami");

        Expect.That(result.Output.Contains(Config.Workspace),
            $"Commands should run in the workspace ({Config.Workspace}): set the process's working directory.",
            result);
        Expect.That(result.Output.Contains("runner"),
            "Commands should run as the runner user, not as agent: start them with sudo -u runner timeout ... /bin/sh -c <command>.",
            result);
    }

    [Fact]
    public void Todo4_RunCommand_KillsCommandAndChildrenOnTimeout()
    {
        var timer = Stopwatch.StartNew();
        var result = Tools.RunCommand("sleep 100 & sleep 100");
        timer.Stop();
        var seconds = timer.Elapsed.TotalSeconds;

        Expect.That(result.IsError,
            "A command that runs too long should come back as an error result: GNU timeout exits with 124 "
            + "(or 137 if it had to kill), so treat those exit codes as a timeout.",
            result);
        Expect.That(seconds >= Config.CommandTimeoutS - 1 && seconds <= Config.CommandTimeoutS + 3,
            $"It should give up after about Config.CommandTimeoutS ({Config.CommandTimeoutS} s), but it took {seconds:F1} s. "
            + $"Run it under timeout -k 2 {Config.CommandTimeoutS}, and don't wait for the output pipes forever.");
        var left = Tools.RunCommand("pgrep -u runner -fx 'sleep 100' || echo none-left");
        Expect.That(left.Output.Contains("none-left"),
            "No \"sleep 100\" may still be running afterwards: GNU timeout kills the command's whole process group, "
            + "background children included.",
            left);
    }

    [Fact]
    public void Todo4_RunCommand_TruncatesLongOutput()
    {
        var result = Tools.RunCommand("seq 1 20000");
        var length = result.Output.Length;

        Expect.That(result.Output.Contains("[truncated"),
            "Long output should go through Truncate, which puts a [truncated ...] marker in the middle.",
            result);
        Expect.That(length >= Config.OutputLimit && length <= Config.OutputLimit + 100,
            $"The output should be about Config.OutputLimit ({Config.OutputLimit}) characters long, but it is {length}.");
        Expect.That(result.Output.StartsWith("exit code 0\n1\n2\n") && result.Output.EndsWith("19999\n20000\n"),
            "The output should be \"exit code 0\", a newline, then the truncated text: the start and the end of what "
            + "the command printed must both survive.",
            result);
    }

    [Fact]
    public void Todo4_RunCommand_EnvironmentHasNoRealKey()
    {
        var result = Tools.RunCommand("env");

        Expect.That(!result.Output.Contains("sk-ant") && !result.Output.Contains("ANTHROPIC_API_KEY"),
            "The command's environment must not contain the API key: run it with sudo -u runner, which starts from a "
            + "clean environment, rather than passing yours on.",
            result);
    }

    [Fact]
    public void Todo4_RunCommand_CannotReadAgentSource()
    {
        var result = Tools.RunCommand("cat /home/agent/app/Program.cs");

        Expect.That(!result.Output.Contains("exit code 0") && result.Output.Contains("Permission denied"),
            "The runner user must not be able to read /home/agent. If this worked, the command ran as agent: "
            + "start it with sudo -u runner.",
            result);
    }

    [Fact]
    public void Todo4_Truncate_KeepsFirst2000AndLast8000()
    {
        var text = NumberedLines(5000);   // 30,000 characters: "00000\n00001\n...04999\n"

        var result = Tools.Truncate(text);

        Expect.That(result.StartsWith(text[..2000]),
            "Truncate should keep the first 2,000 characters unchanged, so the result should start with "
            + $"{Describe(text[..2000])}.",
            Describe(result));
        Expect.That(!result.Contains("\n02500\n"),
            "The middle of the text should be left out, but your result still contains line 02500 from the middle. "
            + "If your second part is text[8000..], that is everything from index 8,000 to the end; the last 8,000 "
            + "characters are text[^8000..].",
            Describe(result));
        Expect.That(result.EndsWith(text[^8000..]),
            "Truncate should end with the last 8,000 characters: text[^8000..], from index text.Length - 8000, so "
            + $"{Describe(text[^8000..])}. Careful: text[8000..] is everything from index 8,000 to the end.",
            Describe(result));
        Expect.That(result.Contains("[truncated"),
            "Put a [truncated ...] marker between the two parts, so the model knows something was left out.",
            Describe(result));
        Expect.That(result.Length is >= 10_000 and <= 10_100,
            "The result should be the 2,000 + 8,000 characters you kept plus the marker: about Config.OutputLimit "
            + $"({Config.OutputLimit}) characters.",
            Describe(result));
    }

    [Fact]
    public void Todo4_Truncate_OnlyTruncatesTextOverTheLimit()
    {
        var atLimit = new string('x', Config.OutputLimit);
        var overLimit = atLimit + "y";

        var shortResult = Tools.Truncate("short");
        var atLimitResult = Tools.Truncate(atLimit);
        var overLimitResult = Tools.Truncate(overLimit);

        Expect.That(shortResult == "short",
            "Short text should come back unchanged.",
            shortResult);
        Expect.That(atLimitResult == atLimit,
            $"Text of exactly Config.OutputLimit ({Config.OutputLimit}) characters should come back unchanged: only "
            + "longer text is truncated.",
            Describe(atLimitResult));
        Expect.That(overLimitResult.Contains("[truncated"),
            $"Text of Config.OutputLimit + 1 ({Config.OutputLimit + 1}) characters is over the limit and should be "
            + "truncated.",
            Describe(overLimitResult));
    }

    // "00000\n00001\n..." so any piece of it shows where in the text it came from.
    static string NumberedLines(int count) => string.Concat(Enumerable.Range(0, count).Select(i => $"{i:D5}\n"));

    // A long string as its length plus how it starts and ends.
    static string Describe(string text)
    {
        static string Escape(string s) => s.Replace("\n", "\\n");
        return text.Length <= 60
            ? $"\"{Escape(text)}\" ({text.Length} characters)"
            : $"\"{Escape(text[..24])}…{Escape(text[^24..])}\" ({text.Length} characters)";
    }
}
