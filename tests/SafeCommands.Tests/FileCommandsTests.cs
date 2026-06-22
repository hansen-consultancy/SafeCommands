using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Handlers are exercised DIRECTLY (not through the dispatcher), so no <c>Policy</c> runs here —
/// path containment is the dispatcher's job and is covered by MigratedCommandPolicyTests. These
/// tests pin the migrated handler behaviour: render routing (Json/Info/Raw/Blocked/Error), that
/// <c>file read</c> routes content to <see cref="IRenderer.Raw"/> unmodified (Raw's own
/// no-added-newline byte-fidelity is pinned at the adapter level in RendererEnvelopeTests, which the
/// FakeRenderer cannot exhibit), the closed <c>--json</c> blocked-envelope fork, and the git probes
/// for delete-tracked/move now routing through <see cref="IExecutor"/> (so a FakeExecutor absorbs
/// them — the SPAWN HAZARD fix). Filesystem-touching handlers use a per-test temp directory.
/// </summary>
public sealed class FileCommandsTests : IDisposable
{
    private readonly string _root;

    public FileCommandsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "safecmd-file-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string At(string name) => Path.Combine(_root, name);

    private string Write(string name, string content)
    {
        var f = At(name);
        File.WriteAllText(f, content);
        return f;
    }

    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), exec, render);
    }

    private static JsonElement AsJson(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

    // === Usage guards (no FS mutation, no spawn) ===

    [Theory]
    [InlineData("read")]
    [InlineData("exists")]
    [InlineData("info")]
    [InlineData("count")]
    [InlineData("find")]
    [InlineData("mkdir")]
    [InlineData("write")]
    [InlineData("delete-tracked")]
    [InlineData("delete-pattern")]
    [InlineData("copy")]
    [InlineData("move")]
    public void NoArgs_EmitsUsageError_NoSpawn(string cmd)
    {
        var (ports, exec, render) = Setup();
        var handler = Handler(cmd);
        Assert.Equal(1, handler(ports, []));
        Assert.Single(render.Errors);
        Assert.Empty(exec.Calls);
        Assert.Empty(render.Blocks);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    public void TwoPathCommands_OneArg_EmitsUsageError(string cmd)
    {
        var (ports, exec, render) = Setup();
        Assert.Equal(1, Handler(cmd)(ports, ["only-one"]));
        Assert.Single(render.Errors);
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void RunDeletePattern_NoInFlag_EmitsError()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(1, FileCommands.RunDeletePattern(ports, ["*.log"])); // glob present, --in absent
        Assert.Contains("--in", Assert.Single(render.Errors));
    }

    // === read: the Raw byte-faithful passthrough (new IRenderer.Raw primitive) ===

    [Fact]
    public void RunRead_HumanMode_EmitsContentVerbatim_NoTrailingNewline()
    {
        // The whole point of Raw: file read reproduces bytes exactly — it must NOT add a newline
        // the way Info/Result would, and it must NOT route through Info (which is colour/suppressed).
        var (ports, _, render) = Setup();
        var f = Write("a.txt", "line1\nline2"); // deliberately no trailing newline
        Assert.Equal(0, FileCommands.RunRead(ports, [f]));
        Assert.Equal("line1\nline2", Assert.Single(render.Raws));
        Assert.Empty(render.Infos);
        Assert.Empty(render.JsonPayloads);
    }

    [Fact]
    public void RunRead_LinesFlag_TruncatesToFirstN()
    {
        var (ports, _, render) = Setup();
        var f = Write("b.txt", "1\n2\n3\n4");
        FileCommands.RunRead(ports, [f, "--lines", "2"]);
        Assert.Equal("1\n2", Assert.Single(render.Raws));
    }

    [Fact]
    public void RunRead_JsonMode_EmitsPayload_NoRaw()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        var f = Write("c.txt", "hello");
        FileCommands.RunRead(ports, [f]);
        Assert.Empty(render.Raws);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("hello", json.GetProperty("content").GetString());
        Assert.Equal(1, json.GetProperty("lineCount").GetInt32());
    }

    [Fact]
    public void RunRead_FileNotFound_EmitsError()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(1, FileCommands.RunRead(ports, [At("nope.txt")]));
        Assert.Single(render.Errors);
        Assert.Empty(render.Raws);
    }

    // === list / exists / info / count ===

    [Fact]
    public void RunList_HumanMode_DirsGetTrailingSlash()
    {
        var (ports, _, render) = Setup();
        Directory.CreateDirectory(At("sub"));
        Write("file.txt", "x");
        Assert.Equal(0, FileCommands.RunList(ports, [_root]));
        Assert.Contains("sub/", render.Infos);
        Assert.Contains("file.txt", render.Infos);
        Assert.Empty(render.JsonPayloads);
    }

    [Fact]
    public void RunList_DirNotFound_EmitsError()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(1, FileCommands.RunList(ports, [At("missing-dir")]));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunExists_File_ReturnsZero_TypeFile()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        var f = Write("e.txt", "x");
        Assert.Equal(0, FileCommands.RunExists(ports, [f]));
        Assert.Equal("file", AsJson(Assert.Single(render.JsonPayloads)).GetProperty("type").GetString());
    }

    [Fact]
    public void RunExists_Missing_ReturnsOne_TypeNone()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        Assert.Equal(1, FileCommands.RunExists(ports, [At("ghost")]));
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.False(json.GetProperty("exists").GetBoolean());
        Assert.Equal("none", json.GetProperty("type").GetString());
    }

    [Fact]
    public void RunInfo_File_JsonHasSizeAndType()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        var f = Write("i.txt", "12345");
        Assert.Equal(0, FileCommands.RunInfo(ports, [f]));
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("file", json.GetProperty("type").GetString());
        Assert.Equal(5, json.GetProperty("size").GetInt64());
    }

    [Fact]
    public void RunInfo_NotFound_EmitsError()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(1, FileCommands.RunInfo(ports, [At("ghost")]));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunCount_DefaultsToLines_JsonHasCounts()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        var f = Write("ct.txt", "a b\nc d e\n");
        FileCommands.RunCount(ports, [f, "--lines", "--words", "--chars"]);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal(2, json.GetProperty("lines").GetInt64());   // two '\n'
        Assert.Equal(5, json.GetProperty("words").GetInt64());   // a b c d e
        Assert.Equal(10, json.GetProperty("chars").GetInt64());
    }

    // === find / tree ===

    [Fact]
    public void RunFind_JsonMode_ReturnsMatchesRelativeToDir()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        Write("hit.cs", "x");
        Write("miss.txt", "x");
        FileCommands.RunFind(ports, ["*.cs", "--in", _root]);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal(1, json.GetProperty("count").GetInt32());
        Assert.Equal("hit.cs", json.GetProperty("files")[0].GetString());
    }

    [Fact]
    public void RunTree_HumanMode_RoutesThroughRenderer()
    {
        var (ports, _, render) = Setup();
        Directory.CreateDirectory(At("nested"));
        Write("top.txt", "x");
        Assert.Equal(0, FileCommands.RunTree(ports, [_root]));
        Assert.NotEmpty(render.Infos); // tree lines went through Render.Info, not Console
    }

    [Fact]
    public void RunTree_JsonMode_EmitsNestedTreePayload()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        Directory.CreateDirectory(At("nested"));
        Write("nested/leaf.txt", "x");
        Assert.Equal(0, FileCommands.RunTree(ports, [_root]));
        Assert.Empty(render.Infos);
        var root = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("dir", root.GetProperty("type").GetString());
        var child = root.GetProperty("children")[0];
        Assert.Equal("nested", child.GetProperty("name").GetString());
        Assert.Equal("dir", child.GetProperty("type").GetString());
    }

    // === mkdir / copy / write (safe writes) ===

    [Fact]
    public void RunMkdir_CreatesDirectory_EmitsInfo()
    {
        var (ports, _, render) = Setup();
        var d = At("made");
        Assert.Equal(0, FileCommands.RunMkdir(ports, [d]));
        Assert.True(Directory.Exists(d));
        Assert.Contains("Created", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunCopy_NewDestination_CopiesFile()
    {
        var (ports, _, render) = Setup();
        var src = Write("src.txt", "data");
        var dest = At("dest.txt");
        Assert.Equal(0, FileCommands.RunCopy(ports, [src, dest]));
        Assert.True(File.Exists(dest));
        Assert.Contains("Copied", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunCopy_DestinationExists_Blocks_NoOverwrite()
    {
        var (ports, exec, render) = Setup();
        var src = Write("s.txt", "new");
        var dest = Write("d.txt", "original");
        Assert.Equal(1, FileCommands.RunCopy(ports, [src, dest]));
        Assert.Equal("file copy", Assert.Single(render.Blocks).Command);
        Assert.Equal("original", File.ReadAllText(dest)); // untouched
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void RunWrite_NewFile_WritesContent()
    {
        var (ports, _, render) = Setup();
        var path = At("w.txt");
        Assert.Equal(0, FileCommands.RunWrite(ports, [path, "--content", "hello", "world"]));
        Assert.Equal("hello world", File.ReadAllText(path));
        Assert.Contains("Written", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunWrite_ExistingFile_Blocks_NoOverwrite()
    {
        var (ports, _, render) = Setup();
        var path = Write("exists.txt", "keep");
        Assert.Equal(1, FileCommands.RunWrite(ports, [path, "--content", "new"]));
        Assert.Equal("file write", Assert.Single(render.Blocks).Command);
        Assert.Equal("keep", File.ReadAllText(path));
    }

    [Fact]
    public void RunWrite_MissingContentFlag_EmitsError()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(1, FileCommands.RunWrite(ports, [At("nc.txt")])); // no --content
        Assert.Single(render.Errors);
        Assert.False(File.Exists(At("nc.txt")));
    }

    // === Blocked --json fork is CLOSED for file (was OutputFormatter markup, now Render.Blocked) ===

    [Fact]
    public void RunCopy_DestExists_UnderJsonMode_EmitsBlockedJson_NotMarkup()
    {
        // Pre-migration this path called OutputFormatter.WriteBlocked, which emitted Spectre markup
        // even under --json (the live "--json blocked-envelope fork"). Now it routes through
        // Render.Blocked, so --json yields a structured {blocked,...} envelope.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var render = new ConsoleRenderer(jsonMode: true, stdout, stderr);
        var ports = new Ports(new FakeExecutor(), render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());
        var src = Write("js.txt", "x");
        var dest = Write("jd.txt", "y");

        Assert.Equal(1, FileCommands.RunCopy(ports, [src, dest]));

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Equal("file copy", doc.RootElement.GetProperty("command").GetString());
    }

    // === delete-tracked: git probes now route through IExecutor (SPAWN HAZARD closed) ===

    [Fact]
    public void RunDeleteTracked_NotTracked_Blocks_RoutesProbeThroughExecutor()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(1, "", ""); // ls-files --error-unmatch -> not tracked
        var f = Write("untracked.txt", "x");

        Assert.Equal(1, FileCommands.RunDeleteTracked(ports, [f]));

        var probe = Assert.Single(exec.Calls);
        Assert.Equal("git", probe.Tool);
        Assert.Equal(new[] { "ls-files", "--error-unmatch", f }, probe.Args);
        Assert.Contains("not tracked", Assert.Single(render.Blocks).Reason);
        Assert.True(File.Exists(f)); // never deleted
    }

    [Fact]
    public void RunDeleteTracked_Uncommitted_Blocks_DoesNotDelete()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(0, "M untracked.txt", ""); // tracked, but diff shows changes
        var f = Write("dirty.txt", "x");

        Assert.Equal(1, FileCommands.RunDeleteTracked(ports, [f]));

        Assert.Equal(3, exec.Calls.Count); // ls-files + 2 diff probes
        Assert.Contains("uncommitted changes", Assert.Single(render.Blocks).Reason);
        Assert.True(File.Exists(f));
    }

    [Fact]
    public void RunDeleteTracked_TrackedAndClean_Deletes_RoutesAllProbes()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(0, "", ""); // tracked + clean diff
        var f = Write("clean.txt", "x");

        Assert.Equal(0, FileCommands.RunDeleteTracked(ports, [f]));

        Assert.Equal(3, exec.Calls.Count);
        Assert.False(File.Exists(f)); // real deletion
        Assert.Contains("Deleted", Assert.Single(render.Infos));
        Assert.Empty(render.Blocks);
    }

    [Fact]
    public void RunDeleteTracked_FileMissing_EmitsError_NoSpawn()
    {
        var (ports, exec, render) = Setup();
        Assert.Equal(1, FileCommands.RunDeleteTracked(ports, [At("ghost")]));
        Assert.Single(render.Errors);
        Assert.Empty(exec.Calls); // File.Exists gate fires before any git probe
    }

    // === move: both git probes route through IExecutor ===

    [Fact]
    public void RunMove_NotTracked_Blocks_RoutesProbeThroughExecutor()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(1, "", ""); // not tracked
        var src = Write("m-src.txt", "x");

        Assert.Equal(1, FileCommands.RunMove(ports, [src, At("m-dest.txt")]));

        var probe = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "ls-files", "--error-unmatch", src }, probe.Args);
        Assert.Equal($"file move {src}", Assert.Single(render.Blocks).Command);
    }

    [Fact]
    public void RunMove_DestinationExists_Blocks_BeforeGitMv()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(0, "", ""); // tracked
        var src = Write("ms.txt", "x");
        var dest = Write("md.txt", "y");

        Assert.Equal(1, FileCommands.RunMove(ports, [src, dest]));

        Assert.Single(exec.Calls); // only ls-files; git mv never reached
        Assert.Equal("file move", Assert.Single(render.Blocks).Command);
    }

    [Fact]
    public void RunMove_TrackedNewDest_RunsGitMvThroughExecutor()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(0, "", ""); // tracked + mv succeeds
        var src = Write("mv-src.txt", "x");
        var dest = At("mv-dest.txt");

        Assert.Equal(0, FileCommands.RunMove(ports, [src, dest]));

        Assert.Equal(2, exec.Calls.Count);
        Assert.Equal(new[] { "ls-files", "--error-unmatch", src }, exec.Calls[0].Args);
        Assert.Equal(new[] { "mv", src, dest }, exec.Calls[1].Args);
        Assert.Contains("Moved", Assert.Single(render.Infos));
    }

    // === delete-temp / delete-locks ===

    [Fact]
    public void RunDeleteTemp_RemovesSafeDirsAndTempFiles()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        Directory.CreateDirectory(At("bin"));
        Write("scratch.tmp", "x");
        Write("keep.cs", "x");
        FileCommands.RunDeleteTemp(ports, [_root]);
        Assert.False(Directory.Exists(At("bin")));
        Assert.False(File.Exists(At("scratch.tmp")));
        Assert.True(File.Exists(At("keep.cs")));
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal(2, json.GetProperty("count").GetInt32());
    }

    [Fact]
    public void RunDeleteTemp_DirNotFound_EmitsError()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(1, FileCommands.RunDeleteTemp(ports, [At("nope")]));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunDeleteLocks_NoneFound_ReportsNoneViaInfo()
    {
        var (ports, _, render) = Setup();
        // _root has no .git tree and no lock candidates -> nothing to delete.
        Assert.Equal(0, FileCommands.RunDeleteLocks(ports, []));
        Assert.Empty(render.JsonPayloads);
        Assert.Empty(render.Errors);
    }

    // --- helper ---

    private static Func<Ports, string[], int> Handler(string cmd) => cmd switch
    {
        "read" => FileCommands.RunRead,
        "exists" => FileCommands.RunExists,
        "info" => FileCommands.RunInfo,
        "count" => FileCommands.RunCount,
        "find" => FileCommands.RunFind,
        "mkdir" => FileCommands.RunMkdir,
        "write" => FileCommands.RunWrite,
        "delete-tracked" => FileCommands.RunDeleteTracked,
        "delete-pattern" => FileCommands.RunDeletePattern,
        "copy" => FileCommands.RunCopy,
        "move" => FileCommands.RunMove,
        _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
    };
}
