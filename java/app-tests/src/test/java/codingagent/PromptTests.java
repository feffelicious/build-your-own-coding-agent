package codingagent;

import codingagent.agent.Prompts;
import codingagent.tools.ToolSchemas;

import static codingagent.Explain.explain;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.anthropic.core.JsonValue;
import com.anthropic.models.messages.Tool;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/**
 * The texts the model reads: the tool descriptions (TODO 1, 2 and 4) and the role prompts (TODO 3, 5 and 6).
 * No test can tell whether a text is good; these only check that it's written and keeps the
 * one rule code depends on. Whether it works shows in a real run.
 */
class PromptTests {
    /** The tool's description, then each parameter's: "name: description". */
    private static List<String> descriptions(Tool tool) {
        List<String> all = new ArrayList<>();
        all.add("description: " + tool.description().orElse(""));
        Map<String, JsonValue> properties = tool.inputSchema().properties().orElseThrow()._additionalProperties();
        properties.forEach((name, schema) -> {
            Object description = ((Map<?, ?>) schema.asObject().orElseThrow()).get("description");
            all.add(name + ": " + ((JsonValue) description).asString().map(Object::toString).orElse(""));
        });
        return all;
    }

    private static void expectDescribed(Tool tool, int todo) {
        List<String> stubs = descriptions(tool).stream()
            .filter(d -> {
                String text = d.split(": ", 2)[1];
                return text.isBlank() || text.startsWith("TODO");
            })
            .toList();
        assertTrue(stubs.isEmpty(), explain(
            "ToolSchemas." + tool.name().toUpperCase() + ": write the tool's description and each parameter's (TODO "
                + todo + "). The model knows the tool only from these texts.",
            String.join("; ", stubs)));
    }

    @Test
    void todo1DescriptionReadFile() {
        expectDescribed(ToolSchemas.READ_FILE, 1);
    }

    @Test
    void todo2DescriptionWriteFile() {
        expectDescribed(ToolSchemas.WRITE_FILE, 2);
    }

    @Test
    void todo4DescriptionRunTests() {
        expectDescribed(ToolSchemas.RUN_TESTS, 4);
    }

    private static void expectWritten(String prompt, String name, int todo) {
        assertTrue(!prompt.isBlank() && !prompt.startsWith("TODO"), explain(
            "Prompts." + name.toUpperCase() + " is still the placeholder: write the " + name + "'s system prompt (TODO "
                + todo + ").",
            prompt));
    }

    @Test
    void todo3PromptCoder() {
        expectWritten(Prompts.CODER, "Coder", 3);
    }

    @Test
    void todo5PromptPlanner() {
        expectWritten(Prompts.PLANNER, "Planner", 5);
    }

    @Test
    void todo6PromptReviewer() {
        expectWritten(Prompts.REVIEWER, "Reviewer", 6);
        assertTrue(Prompts.REVIEWER.contains("APPROVED") && Prompts.REVIEWER.contains("CHANGES_REQUIRED"), explain(
            "The Reviewer prompt must name both verdicts, APPROVED and CHANGES_REQUIRED, and say the verdict goes alone "
                + "on the first line: that line is all parseVerdict reads (TODO 6).",
            Prompts.REVIEWER));
        assertTrue(!Prompts.REVIEWER.equals(Prompts.CODER) && !Prompts.REVIEWER.equals(Prompts.PLANNER), explain(
            "Each role needs its own prompt: the Reviewer's is the same text as another role's (TODO 6).",
            Prompts.REVIEWER));
    }
}
