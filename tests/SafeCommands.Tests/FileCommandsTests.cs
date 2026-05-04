using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>Disables parallelization with other classes in this collection (FileCommandsTests
/// mutates process-wide cwd; running it concurrently with other cwd-sensitive tests is unsafe).</summary>
[CollectionDefinition(nameof(CwdSensitiveCollection), DisableParallelization = true)]
public class CwdSensitiveCollection { }

[Collection(nameof(CwdSensitiveCollection))]
public class FileCommandsTests : IDisposable
{
    // Each test gets a fresh tempdir cd'd into, so ValidatePath's project-root sandbox
    // accepts the relative paths we use here. Restored in Dispose.
    private readonly string _tempDir;
    private readonly string _originalCwd;

    public FileCommandsTests()
    {
        _originalCwd = Directory.GetCurrentDirectory();
        _tempDir = Path.Combine(Path.GetTempPath(), $"safecmd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.SetCurrentDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private static (Ports ports, FakeExecutor exec, FakeRenderer render, FakeGitRepo git) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var git = new FakeGitRepo();
        return (new Ports(exec, render, git), exec, render, git);
    }

    // ---- delete-tracked: IGitRepo-driven gating ----

    [Fact]
    public void RunDeleteTracked_NotTracked_IsBlocked()
    {
        var (ports, exec, render, git) = Setup();
        File.WriteAllText("untracked.txt", "x");
        git.AsRepo();  // file deliberately not in tracked set

        var rc = FileCommands.RunDeleteTracked(ports, ["untracked.txt"]);

        Assert.Equal(1, rc);
        Assert.True(File.Exists("untracked.txt"));
        Assert.Empty(exec.Calls);
        Assert.Contains("not tracked", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunDeleteTracked_HasPendingChanges_IsBlocked()
    {
        var (ports, _, render, git) = Setup();
        File.WriteAllText("modified.txt", "x");
        git.AsRepo().WithTracked("modified.txt").WithPendingChanges("modified.txt");

        var rc = FileCommands.RunDeleteTracked(ports, ["modified.txt"]);

        Assert.Equal(1, rc);
        Assert.True(File.Exists("modified.txt"));
        Assert.Contains("uncommitted changes", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunDeleteTracked_TrackedAndClean_DeletesFile()
    {
        var (ports, _, _, git) = Setup();
        File.WriteAllText("clean.txt", "x");
        git.AsRepo().WithTracked("clean.txt");  // no pending changes

        var rc = FileCommands.RunDeleteTracked(ports, ["clean.txt"]);

        Assert.Equal(0, rc);
        Assert.False(File.Exists("clean.txt"));
    }

    [Fact]
    public void RunDeleteTracked_FileMissing_EmitsError()
    {
        var (ports, _, render, _) = Setup();

        var rc = FileCommands.RunDeleteTracked(ports, ["does-not-exist.txt"]);

        Assert.Equal(1, rc);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunDeleteTracked_NoArgs_EmitsUsageError()
    {
        var (ports, _, render, _) = Setup();

        var rc = FileCommands.RunDeleteTracked(ports, []);

        Assert.Equal(1, rc);
        Assert.Single(render.Errors);
    }

    // ---- move: IGitRepo-driven gating ----

    [Fact]
    public void RunMove_NotTracked_IsBlocked()
    {
        var (ports, exec, render, git) = Setup();
        File.WriteAllText("src.txt", "x");
        git.AsRepo();  // not tracked

        var rc = FileCommands.RunMove(ports, ["src.txt", "dest.txt"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("git-tracked", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunMove_DestExists_IsBlocked()
    {
        var (ports, exec, render, git) = Setup();
        File.WriteAllText("src.txt", "x");
        File.WriteAllText("dest.txt", "y");
        git.AsRepo().WithTracked("src.txt");

        var rc = FileCommands.RunMove(ports, ["src.txt", "dest.txt"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("Destination", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunMove_TrackedAndClean_InvokesGitMv()
    {
        var (ports, exec, _, git) = Setup();
        File.WriteAllText("src.txt", "x");
        git.AsRepo().WithTracked("src.txt");

        FileCommands.RunMove(ports, ["src.txt", "dest.txt"]);

        Assert.Single(exec.Calls);
        Assert.Equal("git", exec.Calls[0].Tool);
        Assert.Equal(new[] { "mv", "src.txt", "dest.txt" }, exec.Calls[0].Args);
    }

    // ---- ValidatePath: path traversal sandbox ----

    [Fact]
    public void RunRead_PathOutsideProject_IsBlocked()
    {
        var (ports, _, render, _) = Setup();

        // Resolve to something definitely outside cwd: parent dir
        var outside = Path.Combine("..", "outside.txt");

        var rc = FileCommands.RunRead(ports, [outside]);

        Assert.Equal(1, rc);
        Assert.Single(render.Blocks);
        Assert.Contains("outside the project directory", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunCopy_DestOutsideProject_IsBlocked()
    {
        var (ports, _, render, _) = Setup();
        File.WriteAllText("inside.txt", "x");

        var rc = FileCommands.RunCopy(ports, ["inside.txt", "../outside.txt"]);

        Assert.Equal(1, rc);
        Assert.Single(render.Blocks);
    }

    // ---- file write: no overwrite ----

    [Fact]
    public void RunWrite_FileExists_IsBlocked()
    {
        var (ports, _, render, _) = Setup();
        File.WriteAllText("existing.txt", "old");

        var rc = FileCommands.RunWrite(ports, ["existing.txt", "--content", "new"]);

        Assert.Equal(1, rc);
        Assert.Equal("old", File.ReadAllText("existing.txt"));
        Assert.Contains("already exists", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunWrite_NewFile_Writes()
    {
        var (ports, _, _, _) = Setup();

        var rc = FileCommands.RunWrite(ports, ["new.txt", "--content", "hello"]);

        Assert.Equal(0, rc);
        Assert.Equal("hello", File.ReadAllText("new.txt"));
    }

    // ---- delete-pattern: safe-dir gate ----

    [Fact]
    public void RunDeletePattern_UnsafeDir_IsBlocked()
    {
        var (ports, _, render, _) = Setup();
        Directory.CreateDirectory("src");

        var rc = FileCommands.RunDeletePattern(ports, ["*.cs", "--in", "src"]);

        Assert.Equal(1, rc);
        Assert.Contains("not in the safe delete list", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunDeletePattern_SafeDir_DeletesFiles()
    {
        var (ports, _, _, _) = Setup();
        Directory.CreateDirectory("bin");
        File.WriteAllText("bin/a.tmp", "x");
        File.WriteAllText("bin/b.tmp", "y");

        var rc = FileCommands.RunDeletePattern(ports, ["*.tmp", "--in", "bin"]);

        Assert.Equal(0, rc);
        Assert.False(File.Exists("bin/a.tmp"));
        Assert.False(File.Exists("bin/b.tmp"));
    }
}
