using CodingAgent;

namespace CodingAgent.Tests;

// TODO 1 and 2. Each test gets its own temporary workspace.
public class FileToolTests : IDisposable
{
    readonly string _originalWorkspace = Config.Workspace;
    readonly string _workspace = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);

    public FileToolTests()
    {
        Directory.CreateDirectory(_workspace);
        Config.Workspace = _workspace;
    }

    public void Dispose()
    {
        Config.Workspace = _originalWorkspace;
        Directory.Delete(_workspace, recursive: true);
        if (Directory.Exists(_workspace + "-evil")) Directory.Delete(_workspace + "-evil", recursive: true);
    }

    [Fact]
    public void Todo1_ResolveInWorkspace_AcceptsNestedPath()
    {
        var expected = Path.Combine(_workspace, "src", "deep", "a.txt");

        var full = Tools.ResolveInWorkspace("src/deep/a.txt");

        Expect.That(full == expected,
            $"ResolveInWorkspace(\"src/deep/a.txt\") should return {expected}. The model's paths are relative to "
            + "Config.Workspace, not to the current directory: combine them first, e.g. Path.Combine(Config.Workspace, path).",
            full);
    }

    [Fact]
    public void Todo1_ResolveInWorkspace_RefusesParentTraversal()
    {
        var full = Tools.ResolveInWorkspace("../../../../etc/passwd");

        Expect.That(full is null,
            "ResolveInWorkspace(\"../../../../etc/passwd\") should return null: after resolving the ..s it points outside the workspace.",
            full);

        var parent = Tools.ResolveInWorkspace("sub/../..");

        Expect.That(parent is null,
            "ResolveInWorkspace(\"sub/../..\") should return null: it is the folder above the workspace. Its relative "
            + "path is exactly \"..\", without a slash, so a check for \"../\" alone misses it.",
            parent);
    }

    [Fact]
    public void Todo1_ResolveInWorkspace_RefusesAbsolutePathOutside()
    {
        var full = Tools.ResolveInWorkspace("/etc/passwd");

        Expect.That(full is null,
            "ResolveInWorkspace(\"/etc/passwd\") should return null. Note that Path.Combine(workspace, \"/etc/passwd\") "
            + "returns \"/etc/passwd\" itself, so your containment check has to catch it.",
            full);
    }

    [Fact]
    public void Todo1_ResolveInWorkspace_RefusesSymlinkPointingOutside()
    {
        File.CreateSymbolicLink(Path.Combine(_workspace, "innocent.txt"), "/etc/passwd");
        Directory.CreateSymbolicLink(Path.Combine(_workspace, "etc-link"), "/etc");

        var viaFile = Tools.ResolveInWorkspace("innocent.txt");
        var viaDirectory = Tools.ResolveInWorkspace("etc-link/passwd");

        Expect.That(viaFile is null,
            "ResolveInWorkspace(\"innocent.txt\") should return null: it is a symlink inside the workspace that points to "
            + "/etc/passwd. Resolve symlinks with RealPath before checking where the path really is.",
            viaFile);
        Expect.That(viaDirectory is null,
            "ResolveInWorkspace(\"etc-link/passwd\") should return null: etc-link is a symlinked folder pointing to /etc. "
            + "Resolve symlinks with RealPath before checking where the path really is.",
            viaDirectory);
    }

    [Fact]
    public void Todo1_ResolveInWorkspace_RefusesSiblingWithSamePrefix()
    {
        var evil = _workspace + "-evil";
        Directory.CreateDirectory(evil);

        var absolute = Tools.ResolveInWorkspace(Path.Combine(evil, "x"));
        var relative = Tools.ResolveInWorkspace($"../{Path.GetFileName(evil)}/x");

        Expect.That(absolute is null && relative is null,
            $"ResolveInWorkspace should return null for {evil}/x: a sibling folder whose name starts with the "
            + "workspace's name. A string test like full.StartsWith(workspace) wrongly accepts it; check with "
            + "Path.GetRelativePath(workspace, full) instead.",
            absolute ?? relative);
    }

    [Fact]
    public void Todo1_ResolveInWorkspace_AcceptsNameStartingWithDots()
    {
        var expected = Path.Combine(_workspace, "..notes.md");

        var full = Tools.ResolveInWorkspace("..notes.md");

        Expect.That(full == expected,
            $"ResolveInWorkspace(\"..notes.md\") should return {expected}: a name that starts with two dots is still "
            + "inside the workspace. Only a relative path that is exactly \"..\" or starts with \"../\" leaves it, so a "
            + "test like relative.StartsWith(\"..\") is too broad.",
            full);
    }

    [Fact]
    public void Todo1_ReadFile_ReadsNestedFile()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "src", "deep"));
        File.WriteAllText(Path.Combine(_workspace, "src", "deep", "a.txt"), "hello");

        var result = Tools.ReadFile("src/deep/a.txt");

        Expect.That(!result.IsError && result.Output == "hello",
            "ReadFile(\"src/deep/a.txt\") should succeed (IsError false) with the file's text \"hello\" as the output.",
            result);
    }

    [Fact]
    public void Todo1_ReadFile_RefusesPathOutside()
    {
        var result = Tools.ReadFile("../../../../etc/passwd");

        Expect.That(result.IsError && !result.Output.Contains("root:"),
            "ReadFile(\"../../../../etc/passwd\") should return an error result (IsError true) without the file's "
            + "content: check the path with ResolveInWorkspace first.",
            result);
    }

    [Fact]
    public void Todo1_ReadFile_ReportsMissingFile()
    {
        var result = Tools.ReadFile("nope.txt");

        Expect.That(result.IsError,
            "ReadFile(\"nope.txt\") should return an error result (IsError true) for a file that doesn't exist, not throw.",
            result);
    }

    [Fact]
    public void Todo1_ReadFile_SaysWhichProblemItIs()
    {
        var outside = Tools.ReadFile("../outside.txt");
        var missing = Tools.ReadFile("missing.txt");

        Expect.That(outside.IsError && missing.IsError && outside.Output != missing.Output,
            "A path outside the workspace and a file that doesn't exist need different errors: the model fixes the "
            + "path in one case and looks for the file elsewhere in the other. Check ResolveInWorkspace's null and "
            + "File.Exists separately.",
            $"outside: {outside.Output} | missing: {missing.Output}");
    }

    [Fact]
    public void Todo1_ReadFile_RefusesFileOverLimitAndSuggestsSmaller()
    {
        File.WriteAllText(Path.Combine(_workspace, "big.txt"), new string('x', Config.FileLimit + 1));

        var result = Tools.ReadFile("big.txt");

        Expect.That(result.IsError,
            $"ReadFile should refuse a file over Config.FileLimit ({Config.FileLimit} bytes) with an error result (IsError true).",
            result);
        Expect.That(result.Output.Contains("smaller"),
            "The model reads this error to decide what to do next, so tell it: suggest reading a smaller file "
            + "(this test looks for the word \"smaller\").",
            result);
        Expect.That(result.Output.Contains((Config.FileLimit + 1).ToString()),
            $"Say how big the file is, so the model knows how far over the limit it is: the error should contain "
            + $"its size in bytes ({Config.FileLimit + 1}).",
            result);
    }

    [Fact]
    public void Todo2_WriteFile_CreatesParentDirectories()
    {
        var result = Tools.WriteFile("new/dir/file.txt", "content");
        var file = Path.Combine(_workspace, "new", "dir", "file.txt");

        Expect.That(!result.IsError,
            "WriteFile(\"new/dir/file.txt\", ...) should succeed (IsError false) and create the missing folders new/dir.",
            result);
        Expect.That(File.Exists(file) && File.ReadAllText(file) == "content",
            $"{file} should exist and contain exactly the content that was passed in.",
            result);
    }

    [Fact]
    public void Todo2_WriteFile_OverwritesAndReportsBytes()
    {
        Tools.WriteFile("f.txt", "old content that is longer");

        var result = Tools.WriteFile("f.txt", "nyå");

        Expect.That(!result.IsError && result.Output.Contains("4 bytes"),
            "WriteFile(\"f.txt\", \"nyå\") should succeed and report the bytes written: \"nyå\" is 4 bytes in UTF-8 "
            + "(å takes 2), so the output should contain \"4 bytes\".",
            result);
        Expect.That(File.ReadAllText(Path.Combine(_workspace, "f.txt")) == "nyå",
            "f.txt should be overwritten with exactly the new content, not appended to.",
            File.ReadAllText(Path.Combine(_workspace, "f.txt")));
    }

    [Fact]
    public void Todo2_WriteFile_RefusesPathsOutside()
    {
        var evil = _workspace + "-evil";
        Directory.CreateDirectory(evil);

        var parent = Tools.WriteFile($"../{Path.GetFileName(evil)}/x", "bad");
        var absolute = Tools.WriteFile(Path.Combine(evil, "y"), "bad");

        Expect.That(parent.IsError && absolute.IsError,
            "WriteFile should return an error result for paths outside the workspace (\"../...\" and absolute): "
            + "check them with ResolveInWorkspace, the same as ReadFile.",
            parent.IsError ? absolute : parent);
        Expect.That(Directory.GetFiles(evil).Length == 0,
            $"Nothing may be written outside the workspace, but {evil} now has files in it.");
    }

    [Fact]
    public void Todo2_WriteFile_RefusesContentOverLimit()
    {
        var result = Tools.WriteFile("big.txt", new string('x', Config.FileLimit + 1));

        Expect.That(result.IsError,
            $"WriteFile should refuse content over Config.FileLimit ({Config.FileLimit} bytes, counted as UTF-8) "
            + "with an error result (IsError true).",
            result);
        Expect.That(!File.Exists(Path.Combine(_workspace, "big.txt")),
            "WriteFile should check the size before writing: big.txt should not have been created.");

        // Under the limit in characters, over it in bytes: å takes 2 bytes in UTF-8.
        var wide = Tools.WriteFile("wide.txt", new string('å', Config.FileLimit / 2 + 1));

        Expect.That(wide.IsError && !File.Exists(Path.Combine(_workspace, "wide.txt")),
            $"WriteFile should count the limit in bytes, not characters: {Config.FileLimit / 2 + 1} å characters are "
            + $"{Config.FileLimit + 2} bytes in UTF-8, over the limit. Use Encoding.UTF8.GetByteCount(content), not content.Length.",
            wide);
    }
}
