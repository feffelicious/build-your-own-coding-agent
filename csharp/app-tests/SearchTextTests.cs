using System.Text;
using System.Text.Json;
using CodingAgent;

namespace CodingAgent.Tests;

// The provided search_text tool, on a small workspace of its own.
public class SearchTextTests : IDisposable
{
    readonly string _originalWorkspace = Config.Workspace;
    readonly string _workspace = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);

    public SearchTextTests()
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
    public void SearchText_ListsMatchingLinesWithPathAndNumber()
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"pattern": "hello"}""")!;

        var result = Tools.ExecuteTool("search_text", input);

        // Not .git, bin, a file over the limit, a binary file or a symlink.
        Assert.False(result.IsError);
        Assert.Equal("README.md:1: hello\nsrc/app.txt:2: two hello", result.Output);
    }

    [Fact]
    public void SearchText_UsesRegularExpressions()
    {
        Assert.Equal("README.md:2: world", Tools.SearchText("^w.r").Output);
    }

    [Fact]
    public void SearchText_SkipsSymlinkedFiles()
    {
        Assert.Equal("No lines match root:", Tools.SearchText("root:").Output);
    }

    [Fact]
    public void SearchText_NoMatchIsNotAnError()
    {
        var result = Tools.SearchText("zzz");

        Assert.False(result.IsError);
        Assert.Equal("No lines match zzz", result.Output);
    }

    [Fact]
    public void SearchText_InvalidPatternIsAnError()
    {
        var result = Tools.SearchText("(");

        Assert.True(result.IsError);
        Assert.Equal("Invalid regular expression: (", result.Output);
    }

    [Fact]
    public void SearchText_ListsAtMost100()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "many"));
        for (var i = 0; i < 105; i++) File.WriteAllText(Path.Combine(_workspace, "many", $"f{i:000}.txt"), "hit");

        var lines = Tools.SearchText("hit").Output.Split('\n');

        Assert.Equal(101, lines.Length);
        Assert.Equal("many/f099.txt:1: hit", lines[99]);
        Assert.Equal("... and 5 more. Use a narrower pattern.", lines[100]);
    }
}
