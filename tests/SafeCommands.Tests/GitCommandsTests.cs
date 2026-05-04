using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class GitCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render, FakeGitRepo git) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var git = new FakeGitRepo();
        return (new Ports(exec, render, git), exec, render, git);
    }

    // ---- The previously-impossible assertion: blocked policy never spawns git ----

    [Theory]
    [InlineData("--force")]
    [InlineData("-f")]
    [InlineData("--delete")]
    [InlineData("--no-verify")]
    public void RunPush_BlockedFlag_IsRejected_WithoutSpawningGit(string flag)
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo();

        var rc = GitCommands.RunPush(ports, [flag, "origin", "main"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Blocks);
    }

    [Fact]
    public void RunPush_PlainPush_Spawns()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo();

        GitCommands.RunPush(ports, ["origin", "main"]);

        Assert.Single(exec.Calls);
        Assert.Equal("git", exec.Calls[0].Tool);
        Assert.Equal(new[] { "push", "origin", "main" }, exec.Calls[0].Args);
    }

    [Fact]
    public void RunPush_ForceWithLease_IsAllowed()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo();

        GitCommands.RunPush(ports, ["--force-with-lease", "origin", "main"]);

        Assert.Single(exec.Calls);
        Assert.Contains("--force-with-lease", exec.Calls[0].Args);
    }

    // ---- IGitRepo-driven: commit-amend ----

    [Fact]
    public void RunCommitAmend_PushedHead_IsBlocked()
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo().WithPushedHead("main", "origin/main");

        var rc = GitCommands.RunCommitAmend(ports, ["-m", "tweak"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Blocks);
        Assert.Contains("origin/main", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunCommitAmend_UnpushedHead_Spawns()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo().WithUnpushedHead("feature/x");

        GitCommands.RunCommitAmend(ports, ["-m", "tweak"]);

        Assert.Single(exec.Calls);
        Assert.Equal(new[] { "commit", "--amend", "-m", "tweak" }, exec.Calls[0].Args);
    }

    [Fact]
    public void RunCommitAmend_DetachedHead_Spawns()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo();  // default head: not pushed, no upstream

        GitCommands.RunCommitAmend(ports, []);

        Assert.Single(exec.Calls);
    }

    // ---- IGitRepo-driven: not a repo ----

    [Fact]
    public void RunStatus_NotInRepo_EmitsError()
    {
        var (ports, exec, render, git) = Setup();
        git.AsNotRepo();

        var rc = GitCommands.RunStatus(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
        Assert.Contains("Not a git repository", render.Errors[0]);
    }

    // ---- IGitRepo-driven: clean tree gating ----

    [Fact]
    public void RunCheckout_DirtyTree_IsBlocked()
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo().WithDirtyTree();

        var rc = GitCommands.RunCheckout(ports, ["main"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Blocks);
        Assert.Contains("uncommitted changes", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunCheckout_CleanTree_Spawns()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo().WithCleanTree();

        GitCommands.RunCheckout(ports, ["main"]);

        Assert.Single(exec.Calls);
        Assert.Equal(new[] { "checkout", "main" }, exec.Calls[0].Args);
    }

    [Fact]
    public void RunCheckout_DotArg_IsBlocked()
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo();

        var rc = GitCommands.RunCheckout(ports, ["."]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("Discarding all changes", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunPull_DirtyTree_IsBlocked()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo().WithDirtyTree();

        var rc = GitCommands.RunPull(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void RunMerge_DirtyTree_IsBlocked()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo().WithDirtyTree();

        var rc = GitCommands.RunMerge(ports, ["feature/x"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
    }

    // ---- add policy ----

    [Theory]
    [InlineData("-A")]
    [InlineData("--all")]
    [InlineData(".")]
    public void RunAdd_WildcardArg_IsBlocked(string arg)
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo();

        var rc = GitCommands.RunAdd(ports, [arg]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Blocks);
    }

    [Fact]
    public void RunAdd_SpecificFile_Spawns()
    {
        var (ports, exec, _, git) = Setup();
        git.AsRepo();

        GitCommands.RunAdd(ports, ["src/foo.cs"]);

        Assert.Single(exec.Calls);
        Assert.Equal(new[] { "add", "src/foo.cs" }, exec.Calls[0].Args);
    }

    // ---- commit policy ----

    [Theory]
    [InlineData("--no-verify")]
    [InlineData("-n")]
    public void RunCommit_NoVerify_IsBlocked(string flag)
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo();

        var rc = GitCommands.RunCommit(ports, [flag, "-m", "msg"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Blocks);
    }

    [Fact]
    public void RunCommit_AmendThroughCommit_IsBlocked()
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo();

        var rc = GitCommands.RunCommit(ports, ["--amend", "-m", "msg"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("commit-amend", render.Blocks[0].Reason);
    }

    [Fact]
    public void RunCommit_NoMessage_EmitsUsageError()
    {
        var (ports, exec, render, git) = Setup();
        git.AsRepo();

        var rc = GitCommands.RunCommit(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }
}
