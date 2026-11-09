using System.Text.Json;
using CodingAgent;

namespace CodingAgent.Tests;

// The provided find_files tool, on a small workspace of its own.
public class FindFilesTests : IDisposable
{
    readonly string _originalWorkspace = Config.Workspace;
    readonly string _workspace = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);

    public FindFilesTests()
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
    public void FindFiles_DoubleStarCrossesFolders()
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"pattern": "**/*.txt"}""")!;

        var result = Tools.ExecuteTool("find_files", input);

        Assert.False(result.IsError);
        Assert.Equal("src/app.txt\nsrc/deep/more.txt", result.Output);
    }

    [Fact]
    public void FindFiles_SingleStarStaysInOneFolder()
    {
        Assert.Equal("src/app.txt", Tools.FindFiles("src/*").Output);
        Assert.Equal("README.md", Tools.FindFiles("*").Output);
    }

    [Fact]
    public void FindFiles_SkipsHiddenAndBuildFoldersAndSymlinkedFolders()
    {
        Assert.Equal("README.md\nsrc/app.txt\nsrc/deep/more.txt", Tools.FindFiles("**").Output);
    }

    [Fact]
    public void FindFiles_NoMatchIsNotAnError()
    {
        var result = Tools.FindFiles("*.cs");

        Assert.False(result.IsError);
        Assert.Equal("No files match *.cs", result.Output);
    }

    [Fact]
    public void FindFiles_ListsAtMost200()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "many"));
        for (var i = 0; i < 205; i++) File.WriteAllText(Path.Combine(_workspace, "many", $"f{i:000}.txt"), "x");

        var lines = Tools.FindFiles("many/*").Output.Split('\n');

        Assert.Equal(201, lines.Length);
        Assert.Equal("many/f199.txt", lines[199]);
        Assert.Equal("... and 5 more. Use a narrower pattern.", lines[200]);
    }
}
