package codingagent;

import codingagent.support.Config;
import codingagent.tools.Tools;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;

import com.anthropic.core.JsonValue;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

/** The provided glob tool, on a small workspace of its own. */
class GlobTests {
    @TempDir
    Path temp;
    Path workspace;
    String originalWorkspace = Config.WORKSPACE;

    @BeforeEach
    void createWorkspace() throws Exception {
        workspace = temp.resolve("ws");
        for (String file : List.of("README.md", "src/app.txt", "src/deep/more.txt", ".git/config", "bin/out.txt")) {
            Files.createDirectories(workspace.resolve(file).getParent());
            Files.writeString(workspace.resolve(file), "x");
        }
        Files.createSymbolicLink(workspace.resolve("outside"), Path.of("/etc"));
        Config.WORKSPACE = workspace.toString();
    }

    @AfterEach
    void restoreWorkspace() throws Exception {
        Config.WORKSPACE = originalWorkspace;
        // Removed here, or JUnit warns about a link out of its temp folder when it cleans up.
        Files.delete(workspace.resolve("outside"));
    }

    @Test
    void globDoubleStarCrossesFolders() {
        var result = Tools.executeTool("glob", JsonValue.from(Map.of("pattern", "**/*.txt")));

        assertFalse(result.isError());
        assertEquals("src/app.txt\nsrc/deep/more.txt", result.output());
    }

    @Test
    void globSingleStarStaysInOneFolder() throws Exception {
        assertEquals("src/app.txt", Tools.glob("src/*").output());
        assertEquals("README.md", Tools.glob("*").output());
    }

    @Test
    void globSkipsHiddenAndBuildFoldersAndSymlinkedFolders() throws Exception {
        assertEquals("README.md\nsrc/app.txt\nsrc/deep/more.txt", Tools.glob("**").output());
    }

    @Test
    void globNoMatchIsNotAnError() throws Exception {
        var result = Tools.glob("*.cs");

        assertFalse(result.isError());
        assertEquals("No files match *.cs", result.output());
    }

    @Test
    void globListsAtMost200() throws Exception {
        Files.createDirectory(workspace.resolve("many"));
        for (int i = 0; i < 205; i++) Files.writeString(workspace.resolve("many").resolve(String.format("f%03d.txt", i)), "x");

        String[] lines = Tools.glob("many/*").output().split("\n");

        assertEquals(201, lines.length);
        assertEquals("many/f199.txt", lines[199]);
        assertEquals("... and 5 more. Use a narrower pattern.", lines[200]);
    }
}
