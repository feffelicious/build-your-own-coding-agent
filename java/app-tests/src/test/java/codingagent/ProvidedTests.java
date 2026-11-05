package codingagent;

import codingagent.agent.Orchestrator;
import codingagent.tools.Tools;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.anthropic.core.JsonValue;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

/** The provided tool dispatcher, and TODO 6 verdict parsing. */
class ProvidedTests {
    @Test
    void executeToolUnknownToolIsAnError() {
        var result = Tools.executeTool("delete_everything", JsonValue.from(Map.of()));

        assertTrue(result.isError());
        assertTrue(result.output().contains("Unknown tool"));
    }

    @Test
    void executeToolMissingArgumentIsAnError() {
        var result = Tools.executeTool("read_file", JsonValue.from(Map.of("file", "x")));

        assertTrue(result.isError());
        assertTrue(result.output().contains("path"), result.output());
    }

    @ParameterizedTest
    @CsvSource(delimiter = '|', quoteCharacter = '"', textBlock = """
        "APPROVED"                          | APPROVED
        "APPROVED\\nAll criteria pass."     | APPROVED
        "  APPROVED  \\n"                   | APPROVED
        "CHANGES_REQUIRED\\n- divide truncates" | CHANGES_REQUIRED
        "Approved"                          | CHANGES_REQUIRED
        "APPROVED."                         | CHANGES_REQUIRED
        "I think this is APPROVED"          | CHANGES_REQUIRED
        "CHANGES_REQUIRED\\nAPPROVED"       | CHANGES_REQUIRED
        ""                                  | CHANGES_REQUIRED
        """)
    void todo6ParseVerdict(String review, String expected) {
        String verdict = Orchestrator.parseVerdict(review.replace("\\n", "\n"));

        assertEquals(expected, verdict, Explain.explain(
            "parseVerdict(\"" + review + "\") should be " + expected + ": only a first line that is exactly APPROVED "
                + "after trimming counts as approval; anything else, including no text at all, is CHANGES_REQUIRED.",
            verdict));
    }
}
