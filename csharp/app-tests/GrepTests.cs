using System.Text;
using System.Text.Json;
using CodingAgent;

namespace CodingAgent.Tests;

// The provided grep tool, on a small workspace of its own.
public class GrepTests : IDisposable
{
    readonly string _originalWorkspace = Config.Workspace;
    readonly string _workspace = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);

    public GrepTests()
    {
        var files = new Dictionary<string, string>
        {
            ["README.md"] = "hello\nworld\n",
            ["src/app.txt"] = "one\r\ntwo hello\r\n",
            [".git/config"] = "hello",
            ["bin/out.txt"] = "hello",
            ["big.txt"] = "hello" + new string('x', Config.FileLimit),
        };
        foreach (var (file, text) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_workspace, file))!);
            File.WriteAllText(Path.Combine(_workspace, file), text);
        }
        File.WriteAllBytes(Path.Combine(_workspace, "data.bin"), [0xff, .. Encoding.ASCII.GetBytes("hello")]);
        File.CreateSymbolicLink(Path.Combine(_workspace, "passwd"), "/etc/passwd");
        Config.Workspace = _workspace;
    }

    public void Dispose()
    {
        Config.Workspace = _originalWorkspace;
        Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public void Grep_ListsMatchingLinesWithPathAndNumber()
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"pattern": "hello"}""")!;

        var result = Tools.ExecuteTool("grep", input);

        // Not .git, bin, a file over the limit, a binary file or a symlink.
        Assert.False(result.IsError);
        Assert.Equal("README.md:1: hello\nsrc/app.txt:2: two hello", result.Output);
    }

    [Fact]
    public void Grep_UsesRegularExpressions()
    {
        Assert.Equal("README.md:2: world", Tools.Grep("^w.r").Output);
    }

    [Fact]
    public void Grep_SkipsSymlinkedFiles()
    {
        Assert.Equal("No lines match root:", Tools.Grep("root:").Output);
    }

    [Fact]
    public void Grep_NoMatchIsNotAnError()
    {
        var result = Tools.Grep("zzz");

        Assert.False(result.IsError);
        Assert.Equal("No lines match zzz", result.Output);
    }

    [Fact]
    public void Grep_InvalidPatternIsAnError()
    {
        var result = Tools.Grep("(");

        Assert.True(result.IsError);
        Assert.Equal("Invalid regular expression: (", result.Output);
    }

    [Fact]
    public void Grep_ListsAtMost100()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "many"));
        for (var i = 0; i < 105; i++) File.WriteAllText(Path.Combine(_workspace, "many", $"f{i:000}.txt"), "hit");

        var lines = Tools.Grep("hit").Output.Split('\n');

        Assert.Equal(101, lines.Length);
        Assert.Equal("many/f099.txt:1: hit", lines[99]);
        Assert.Equal("... and 5 more. Use a narrower pattern.", lines[100]);
    }
}
