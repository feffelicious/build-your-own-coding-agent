package codingagent;

import codingagent.support.Config;
import codingagent.tools.Tools;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.anthropic.core.JsonValue;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.Map;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

/** The provided grep tool, on a small workspace of its own. */
class GrepTests {
    @TempDir
    Path temp;
    Path workspace;
    String originalWorkspace = Config.WORKSPACE;

    @BeforeEach
    void createWorkspace() throws Exception {
        workspace = temp.resolve("ws");
        Map<String, String> files = new LinkedHashMap<>();
        files.put("README.md", "hello\nworld\n");
        files.put("src/app.txt", "one\r\ntwo hello\r\n");
        files.put(".git/config", "hello");
        files.put("bin/out.txt", "hello");
        files.put("big.txt", "hello" + "x".repeat(Config.FILE_LIMIT));
        for (var file : files.entrySet()) {
            Files.createDirectories(workspace.resolve(file.getKey()).getParent());
            Files.writeString(workspace.resolve(file.getKey()), file.getValue());
        }
        byte[] binary = ("\u0000hello").getBytes(StandardCharsets.US_ASCII);
        binary[0] = (byte) 0xff;
        Files.write(workspace.resolve("data.bin"), binary);
        Files.createSymbolicLink(workspace.resolve("passwd"), Path.of("/etc/passwd"));
        Config.WORKSPACE = workspace.toString();
    }

    @AfterEach
    void restoreWorkspace() throws Exception {
        Config.WORKSPACE = originalWorkspace;
        // Removed here, or JUnit warns about a link out of its temp folder when it cleans up.
        Files.delete(workspace.resolve("passwd"));
    }

    @Test
    void grepListsMatchingLinesWithPathAndNumber() {
        var result = Tools.executeTool("grep", JsonValue.from(Map.of("pattern", "hello")));

        // Not .git, bin, a file over the limit, a binary file or a symlink.
        assertFalse(result.isError());
        assertEquals("README.md:1: hello\nsrc/app.txt:2: two hello", result.output());
    }

    @Test
    void grepUsesRegularExpressions() throws Exception {
        assertEquals("README.md:2: world", Tools.grep("^w.r").output());
    }

    @Test
    void grepSkipsSymlinkedFiles() throws Exception {
        assertEquals("No lines match root:", Tools.grep("root:").output());
    }

    @Test
    void grepNoMatchIsNotAnError() throws Exception {
        var result = Tools.grep("zzz");

        assertFalse(result.isError());
        assertEquals("No lines match zzz", result.output());
    }

    @Test
    void grepInvalidPatternIsAnError() throws Exception {
        var result = Tools.grep("(");

        assertTrue(result.isError());
        assertEquals("Invalid regular expression: (", result.output());
    }

    @Test
    void grepListsAtMost100() throws Exception {
        Files.createDirectory(workspace.resolve("many"));
        for (int i = 0; i < 105; i++) Files.writeString(workspace.resolve("many").resolve(String.format("f%03d.txt", i)), "hit");

        String[] lines = Tools.grep("hit").output().split("\n");

        assertEquals(101, lines.length);
        assertEquals("many/f099.txt:1: hit", lines[99]);
        assertEquals("... and 5 more. Use a narrower pattern.", lines[100]);
    }
}
