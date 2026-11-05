namespace CodingAgent.Tests;

// Assertions that say what your code should do when they fail, and show what it
// actually returned.
static class Expect
{
    public static void That(bool condition, string why)
    {
        if (!condition) Assert.Fail(why);
    }

    public static void That(bool condition, string why, object? actual)
    {
        if (condition) return;
        var shown = actual?.ToString() ?? "null";
        if (shown.Length > 400) shown = shown[..400] + "…";
        Assert.Fail($"{why}\nYour code returned: {shown}");
    }
}
