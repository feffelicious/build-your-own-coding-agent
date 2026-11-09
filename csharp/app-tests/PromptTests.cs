using Anthropic.Models.Messages;
using CodingAgent;

namespace CodingAgent.Tests;

// The texts the model reads: the tool descriptions (TODO 1, 2 and 4) and the role prompts (TODO 3, 5 and 6).
// No test can tell whether a text is good; these only check that it's written and keeps the
// one rule code depends on. Whether it works shows in a real run.
public class PromptTests
{
    // The tool's description, then each parameter's: "name: description".
    static List<string> Descriptions(Tool tool) =>
        [$"description: {tool.Description}",
         .. tool.InputSchema.Properties!.Select(p => $"{p.Key}: {p.Value.GetProperty("description").GetString()}")];

    static void ExpectDescribed(Tool tool, int todo)
    {
        var stubs = Descriptions(tool).Where(d => d.Split(": ", 2)[1].Trim() is "" || d.Split(": ", 2)[1].StartsWith("TODO")).ToList();
        Expect.That(stubs.Count == 0,
            $"ToolSchemas.{tool.Name}: write the tool's description and each parameter's (TODO {todo}). The model "
            + "knows the tool only from these texts.",
            string.Join("; ", stubs));
    }

    [Fact]
    public void Todo1_Description_ReadFile() => ExpectDescribed(ToolSchemas.ReadFile, 1);

    [Fact]
    public void Todo2_Description_WriteFile() => ExpectDescribed(ToolSchemas.WriteFile, 2);

    [Fact]
    public void Todo4_Description_RunTests() => ExpectDescribed(ToolSchemas.RunTests, 4);

    static void ExpectWritten(string prompt, string name, int todo) =>
        Expect.That(prompt.Trim() != "" && !prompt.StartsWith("TODO"),
            $"Prompts.{name} is still the placeholder: write the {name}'s system prompt (TODO {todo}).",
            prompt);

    [Fact]
    public void Todo3_Prompt_Coder() => ExpectWritten(Prompts.Coder, "Coder", 3);

    [Fact]
    public void Todo5_Prompt_Planner() => ExpectWritten(Prompts.Planner, "Planner", 5);

    [Fact]
    public void Todo6_Prompt_Reviewer()
    {
        ExpectWritten(Prompts.Reviewer, "Reviewer", 6);
        Expect.That(Prompts.Reviewer.Contains("APPROVED") && Prompts.Reviewer.Contains("CHANGES_REQUIRED"),
            "The Reviewer prompt must name both verdicts, APPROVED and CHANGES_REQUIRED, and say the verdict goes alone "
            + "on the first line: that line is all ParseVerdict reads (TODO 6).",
            Prompts.Reviewer);
        Expect.That(Prompts.Reviewer != Prompts.Coder && Prompts.Reviewer != Prompts.Planner,
            "Each role needs its own prompt: the Reviewer's is the same text as another role's (TODO 6).",
            Prompts.Reviewer);
    }
}
