package codingagent;

import codingagent.support.Config;
import codingagent.tools.Tools;

import static codingagent.Explain.explain;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

/** TODO 1 and 2. Each test gets its own temporary workspace. */
class FileToolTests {
    @TempDir
    Path temp;
    Path workspace;
    String originalWorkspace = Config.WORKSPACE;

    @BeforeEach
    void createWorkspace() throws Exception {
        workspace = Files.createDirectory(temp.resolve("ws"));
        Config.WORKSPACE = workspace.toString();
    }

    @AfterEach
    void restoreWorkspace() {
        Config.WORKSPACE = originalWorkspace;
    }

    @Test
    void todo1ResolveInWorkspaceAcceptsNestedPath() throws Exception {
        Path expected = workspace.resolve("src/deep/a.txt");

        Path full = Tools.resolveInWorkspace("src/deep/a.txt");

        assertTrue(expected.equals(full), explain(
            "resolveInWorkspace(\"src/deep/a.txt\") should return " + expected + ". The model's paths are relative to "
                + "Config.WORKSPACE, not to the current directory: resolve them against it first, "
                + "e.g. Path.of(Config.WORKSPACE).resolve(path).",
            full));
    }

    @Test
    void todo1ResolveInWorkspaceRefusesParentTraversal() throws Exception {
        Path full = Tools.resolveInWorkspace("../../../../etc/passwd");

        assertTrue(full == null, explain(
            "resolveInWorkspace(\"../../../../etc/passwd\") should return null: after resolving the ..s it points "
                + "outside the workspace.",
            full));

        Path parent = Tools.resolveInWorkspace("sub/../..");

        assertTrue(parent == null, explain(
            "resolveInWorkspace(\"sub/../..\") should return null: it is the folder above the workspace. Its relative "
                + "path is exactly \"..\", without a slash, so a check for \"../\" alone misses it.",
            parent));
    }

    @Test
    void todo1ResolveInWorkspaceRefusesAbsolutePathOutside() throws Exception {
        Path full = Tools.resolveInWorkspace("/etc/passwd");

        assertTrue(full == null, explain(
            "resolveInWorkspace(\"/etc/passwd\") should return null. Note that workspace.resolve(\"/etc/passwd\") "
                + "returns \"/etc/passwd\" itself, so your containment check has to catch it.",
            full));
    }

    @Test
    void todo1ResolveInWorkspaceRefusesSymlinkPointingOutside() throws Exception {
        Files.createSymbolicLink(workspace.resolve("innocent.txt"), Path.of("/etc/passwd"));
        Files.createSymbolicLink(workspace.resolve("etc-link"), Path.of("/etc"));

        Path viaFile = Tools.resolveInWorkspace("innocent.txt");
        Path viaDirectory = Tools.resolveInWorkspace("etc-link/passwd");

        assertTrue(viaFile == null, explain(
            "resolveInWorkspace(\"innocent.txt\") should return null: it is a symlink inside the workspace that points "
                + "to /etc/passwd. Resolve symlinks with realPath before checking where the path really is.",
            viaFile));
        assertTrue(viaDirectory == null, explain(
            "resolveInWorkspace(\"etc-link/passwd\") should return null: etc-link is a symlinked folder pointing to "
                + "/etc. Resolve symlinks with realPath before checking where the path really is.",
            viaDirectory));
        Files.delete(workspace.resolve("innocent.txt"));
        Files.delete(workspace.resolve("etc-link"));
    }

    @Test
    void todo1ResolveInWorkspaceRefusesSiblingWithSamePrefix() throws Exception {
        Path evil = Files.createDirectory(temp.resolve("ws-evil"));

        Path absolute = Tools.resolveInWorkspace(evil.resolve("x").toString());
        Path relative = Tools.resolveInWorkspace("../ws-evil/x");

        assertTrue(absolute == null && relative == null, explain(
            "resolveInWorkspace should return null for " + evil + "/x: a sibling folder whose name starts with the "
                + "workspace's name. A string test like full.toString().startsWith(...) wrongly accepts it; use "
                + "Path.startsWith, which compares whole path elements.",
            absolute != null ? absolute : relative));
    }

    @Test
    void todo1ResolveInWorkspaceAcceptsNameStartingWithDots() throws Exception {
        Path expected = workspace.resolve("..notes.md");

        Path full = Tools.resolveInWorkspace("..notes.md");

        assertTrue(expected.equals(full), explain(
            "resolveInWorkspace(\"..notes.md\") should return " + expected + ": a name that starts with two dots is "
                + "still inside the workspace. Only a relative path that is exactly \"..\" or starts with \"../\" "
                + "leaves it, so a test like relative.toString().startsWith(\"..\") is too broad; Path.startsWith "
                + "compares whole path elements.",
            full));
    }

    @Test
    void todo1ReadFileReadsNestedFile() throws Exception {
        Files.createDirectories(workspace.resolve("src/deep"));
        Files.writeString(workspace.resolve("src/deep/a.txt"), "hello");

        var result = Tools.readFile("src/deep/a.txt");

        assertTrue(!result.isError() && result.output().equals("hello"), explain(
            "readFile(\"src/deep/a.txt\") should succeed (isError false) with the file's text \"hello\" as the output.",
            result));
    }

    @Test
    void todo1ReadFileRefusesPathOutside() throws Exception {
        var result = Tools.readFile("../../../../etc/passwd");

        assertTrue(result.isError() && !result.output().contains("root:"), explain(
            "readFile(\"../../../../etc/passwd\") should return an error result (isError true) without the file's "
                + "content: check the path with resolveInWorkspace first.",
            result));
    }

    @Test
    void todo1ReadFileReportsMissingFile() throws Exception {
        var result = Tools.readFile("nope.txt");

        assertTrue(result.isError(), explain(
            "readFile(\"nope.txt\") should return an error result (isError true) for a file that doesn't exist, not throw.",
            result));
    }

    @Test
    void todo1ReadFileSaysWhichProblemItIs() throws Exception {
        var outside = Tools.readFile("../outside.txt");
        var missing = Tools.readFile("missing.txt");

        assertTrue(outside.isError() && missing.isError() && !outside.output().equals(missing.output()), explain(
            "A path outside the workspace and a file that doesn't exist need different errors: the model fixes the "
                + "path in one case and looks for the file elsewhere in the other. Check resolveInWorkspace's null and "
                + "Files.isRegularFile separately.",
            "outside: " + outside.output() + " | missing: " + missing.output()));
    }

    @Test
    void todo1ReadFileRefusesFileOverLimitAndSuggestsSmaller() throws Exception {
        Files.writeString(workspace.resolve("big.txt"), "x".repeat(Config.FILE_LIMIT + 1));

        var result = Tools.readFile("big.txt");

        assertTrue(result.isError(), explain(
            "readFile should refuse a file over Config.FILE_LIMIT (" + Config.FILE_LIMIT + " bytes) with an error "
                + "result (isError true).",
            result));
        assertTrue(result.output().contains("smaller"), explain(
            "The model reads this error to decide what to do next, so tell it: suggest reading a smaller file "
                + "(this test looks for the word \"smaller\").",
            result));
        assertTrue(result.output().contains(String.valueOf(Config.FILE_LIMIT + 1)), explain(
            "Say how big the file is, so the model knows how far over the limit it is: the error should contain its "
                + "size in bytes (" + (Config.FILE_LIMIT + 1) + ").",
            result));
    }

    @Test
    void todo2WriteFileCreatesParentDirectories() throws Exception {
        var result = Tools.writeFile("new/dir/file.txt", "content");
        Path file = workspace.resolve("new/dir/file.txt");

        assertTrue(!result.isError(), explain(
            "writeFile(\"new/dir/file.txt\", ...) should succeed (isError false) and create the missing folders new/dir.",
            result));
        assertTrue(Files.exists(file) && Files.readString(file).equals("content"), explain(
            file + " should exist and contain exactly the content that was passed in.",
            result));
    }

    @Test
    void todo2WriteFileOverwritesAndReportsBytes() throws Exception {
        Tools.writeFile("f.txt", "old content that is longer");

        var result = Tools.writeFile("f.txt", "nyå");
        String written = Files.readString(workspace.resolve("f.txt"));

        assertTrue(!result.isError(), explain(
            "writeFile(\"f.txt\", \"nyå\") should succeed (isError false): replacing an existing file is a normal write.",
            result));
        assertTrue(result.output().contains("4 bytes"), explain(
            "writeFile(\"f.txt\", \"nyå\") should report the bytes written: \"nyå\" is 4 bytes in UTF-8 "
                + "(å takes 2), so the output should contain \"4 bytes\".",
            result));
        assertTrue(written.equals("nyå"), explain(
            "f.txt should be overwritten with exactly the new content, not appended to.",
            written));
    }

    @Test
    void todo2WriteFileRefusesPathsOutside() throws Exception {
        Path evil = Files.createDirectory(temp.resolve("ws-evil"));

        var parent = Tools.writeFile("../ws-evil/x", "bad");
        var absolute = Tools.writeFile(evil.resolve("y").toString(), "bad");

        assertTrue(parent.isError() && absolute.isError(), explain(
            "writeFile should return an error result for paths outside the workspace (\"../...\" and absolute): "
                + "check them with resolveInWorkspace, the same as readFile.",
            parent.isError() ? absolute : parent));
        try (var files = Files.list(evil)) {
            assertTrue(files.findAny().isEmpty(), explain(
                "Nothing may be written outside the workspace, but " + evil + " now has files in it."));
        }
    }

    @Test
    void todo2WriteFileRefusesContentOverLimit() throws Exception {
        var result = Tools.writeFile("big.txt", "x".repeat(Config.FILE_LIMIT + 1));

        assertTrue(result.isError(), explain(
            "writeFile should refuse content over Config.FILE_LIMIT (" + Config.FILE_LIMIT + " bytes, counted as "
                + "UTF-8) with an error result (isError true).",
            result));
        assertTrue(!Files.exists(workspace.resolve("big.txt")), explain(
            "writeFile should check the size before writing: big.txt should not have been created."));

        // Under the limit in characters, over it in bytes: å takes 2 bytes in UTF-8.
        var wide = Tools.writeFile("wide.txt", "å".repeat(Config.FILE_LIMIT / 2 + 1));

        assertTrue(wide.isError() && !Files.exists(workspace.resolve("wide.txt")), explain(
            "writeFile should count the limit in bytes, not characters: " + (Config.FILE_LIMIT / 2 + 1) + " å "
                + "characters are " + (Config.FILE_LIMIT + 2) + " bytes in UTF-8, over the limit. Use "
                + "content.getBytes(StandardCharsets.UTF_8).length, not content.length().",
            wide));
    }
}
