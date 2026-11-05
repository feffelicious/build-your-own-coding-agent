using System.Text.Json;
using CodingAgent;

namespace CodingAgent.Tests;

// The provided glob tool, on a small workspace of its own.
public class GlobTests : IDisposable
{
    readonly string _originalWorkspace = Config.Workspace;
    readonly string _workspace = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);

    public GlobTests()
    {
        foreach (var file in new[] { "README.md", "src/app.txt", "src/deep/more.txt", ".git/config", "bin/out.txt" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_workspace, file))!);
            File.WriteAllText(Path.Combine(_workspace, file), "x");
        }
        Directory.CreateSymbolicLink(Path.Combine(_workspace, "outside"), "/etc");
        Config.Workspace = _workspace;
    }

    public void Dispose()
    {
        Config.Workspace = _originalWorkspace;
        Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public void Glob_DoubleStarCrossesFolders()
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"pattern": "**/*.txt"}""")!;

        var result = Tools.ExecuteTool("glob", input);

        Assert.False(result.IsError);
        Assert.Equal("src/app.txt\nsrc/deep/more.txt", result.Output);
    }

    [Fact]
    public void Glob_SingleStarStaysInOneFolder()
    {
        Assert.Equal("src/app.txt", Tools.Glob("src/*").Output);
        Assert.Equal("README.md", Tools.Glob("*").Output);
    }

    [Fact]
    public void Glob_SkipsHiddenAndBuildFoldersAndSymlinkedFolders()
    {
        Assert.Equal("README.md\nsrc/app.txt\nsrc/deep/more.txt", Tools.Glob("**").Output);
    }

    [Fact]
    public void Glob_NoMatchIsNotAnError()
    {
        var result = Tools.Glob("*.cs");

        Assert.False(result.IsError);
        Assert.Equal("No files match *.cs", result.Output);
    }

    [Fact]
    public void Glob_ListsAtMost200()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "many"));
        for (var i = 0; i < 205; i++) File.WriteAllText(Path.Combine(_workspace, "many", $"f{i:000}.txt"), "x");

        var lines = Tools.Glob("many/*").Output.Split('\n');

        Assert.Equal(201, lines.Length);
        Assert.Equal("many/f199.txt", lines[199]);
        Assert.Equal("... and 5 more. Use a narrower pattern.", lines[200]);
    }
}
