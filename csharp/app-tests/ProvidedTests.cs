using System.Text.Json;
using CodingAgent;

namespace CodingAgent.Tests;

// The provided tool dispatcher, and TODO 6 verdict parsing.
public class ProvidedTests
{
    static IReadOnlyDictionary<string, JsonElement> Input(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;

    [Fact]
    public void ExecuteTool_UnknownToolIsAnError()
    {
        var result = Tools.ExecuteTool("delete_everything", Input(new { }));

        Assert.True(result.IsError);
        Assert.Contains("Unknown tool", result.Output);
    }

    [Fact]
    public void ExecuteTool_MissingArgumentIsAnError()
    {
        var result = Tools.ExecuteTool("read_file", Input(new { file = "x" }));

        Assert.True(result.IsError);
        Assert.Contains("path", result.Output);
    }

    [Theory]
    [InlineData("APPROVED", "APPROVED")]
    [InlineData("APPROVED\nAll criteria pass.", "APPROVED")]
    [InlineData("  APPROVED  \n", "APPROVED")]
    [InlineData("CHANGES_REQUIRED\n- divide truncates", "CHANGES_REQUIRED")]
    [InlineData("Approved", "CHANGES_REQUIRED")]
    [InlineData("APPROVED.", "CHANGES_REQUIRED")]
    [InlineData("I think this is APPROVED", "CHANGES_REQUIRED")]
    [InlineData("CHANGES_REQUIRED\nAPPROVED", "CHANGES_REQUIRED")]
    [InlineData("", "CHANGES_REQUIRED")]
    public void Todo6_ParseVerdict(string review, string expected)
    {
        var verdict = Orchestrator.ParseVerdict(review);

        Expect.That(verdict == expected,
            $"ParseVerdict(\"{review.Replace("\n", "\\n")}\") should be {expected}: only a first line that is exactly "
            + "APPROVED after trimming counts as approval; anything else, including no text at all, is CHANGES_REQUIRED.",
            verdict);
    }
}
