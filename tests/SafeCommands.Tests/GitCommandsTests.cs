using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class GitCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), exec, render);
    }

    private static JsonElement AsJson(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

    // === status (dual-mode) ===

    [Fact]
    public void RunStatus_HumanMode_PassesThroughWithArgs()
    {
        var (ports, exec, render) = Setup();
        RunAssertExit(GitCommands.RunStatus(ports, ["-s"]));
        var call = Assert.Single(exec.Calls);
        Assert.Equal("git", call.Tool);
        Assert.Equal(new[] { "status", "-s" }, call.Args);
        Assert.Single(render.Results);          // passthrough envelope, not custom JSON
        Assert.Empty(render.JsonPayloads);
    }

    [Fact]
    public void RunStatus_JsonMode_ParsesPorcelainIntoBranchAndFiles_IgnoresArgs()
    {
        var (ports, exec, render) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(0, "## main\n M src/a.cs\n?? b.txt", "");
        GitCommands.RunStatus(ports, ["-s"]); // args ignored in JSON branch

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "status", "--porcelain", "-b" }, call.Args);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("main", json.GetProperty("branch").GetString());
        Assert.False(json.GetProperty("clean").GetBoolean());
        var files = json.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal("M", files[0].GetProperty("status").GetString());
        Assert.Equal("src/a.cs", files[0].GetProperty("file").GetString());
        Assert.Equal("??", files[1].GetProperty("status").GetString());
    }

    [Fact]
    public void RunStatus_JsonMode_NoChanges_IsClean()
    {
        var (ports, exec, render) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(0, "## main", "");
        GitCommands.RunStatus(ports, []);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.True(json.GetProperty("clean").GetBoolean());
        Assert.Equal(0, json.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public void RunStatus_JsonMode_PropagatesProbeExitCode()
    {
        // The dual-mode JSON branch returns the probe's exit code (a path distinct from Run.Tool).
        var (ports, exec, _) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(128, "## main", "");
        Assert.Equal(128, GitCommands.RunStatus(ports, []));
    }

    [Fact]
    public void RunBranch_JsonMode_PropagatesProbeExitCode()
    {
        var (ports, exec, _) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(128, "* main", "");
        Assert.Equal(128, GitCommands.RunBranch(ports, []));
    }

    // === branch (dual-mode) ===

    [Fact]
    public void RunBranch_HumanMode_PassesThroughList()
    {
        var (ports, exec, render) = Setup();
        GitCommands.RunBranch(ports, ["-a"]);
        Assert.Equal(new[] { "branch", "--list", "-a" }, Assert.Single(exec.Calls).Args);
        Assert.Single(render.Results);          // passthrough envelope, not custom JSON
        Assert.Empty(render.JsonPayloads);
    }

    [Fact]
    public void RunBranch_JsonMode_ParsesBranchesWithCurrentMarker()
    {
        var (ports, exec, render) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(0, "* main\n  feature", "");
        GitCommands.RunBranch(ports, []);

        Assert.Equal(new[] { "branch", "--list", "--no-color" }, Assert.Single(exec.Calls).Args);
        var branches = AsJson(Assert.Single(render.JsonPayloads)).GetProperty("branches");
        Assert.Equal(2, branches.GetArrayLength());
        Assert.Equal("main", branches[0].GetProperty("name").GetString());
        Assert.True(branches[0].GetProperty("current").GetBoolean());
        Assert.Equal("feature", branches[1].GetProperty("name").GetString());
        Assert.False(branches[1].GetProperty("current").GetBoolean());
    }

    // === passthrough handlers (args splat) ===

    [Theory]
    [InlineData("log")]
    [InlineData("diff")]
    [InlineData("ls-files")]
    [InlineData("shortlog")]
    [InlineData("fetch")]
    [InlineData("pull")]
    [InlineData("push")]
    public void PassthroughHandlers_PrependSubcommand_KeepArgs(string sub)
    {
        var (ports, exec, _) = Setup();
        var handler = sub switch
        {
            "log" => (Func<Ports, string[], int>)GitCommands.RunLog,
            "diff" => GitCommands.RunDiff,
            "ls-files" => GitCommands.RunLsFiles,
            "shortlog" => GitCommands.RunShortlog,
            "fetch" => GitCommands.RunFetch,
            "pull" => GitCommands.RunPull,
            "push" => GitCommands.RunPush,
            _ => throw new ArgumentOutOfRangeException(nameof(sub)),
        };
        handler(ports, ["--extra"]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("git", call.Tool);
        Assert.Equal(new[] { sub, "--extra" }, call.Args);
    }

    [Fact]
    public void RunTag_PrependsListFlag() => AssertArgs(GitCommands.RunTag, ["v1"], "tag", "--list", "v1");

    [Fact]
    public void RunCommitAmend_InsertsAmend() => AssertArgs(GitCommands.RunCommitAmend, ["-m", "x"], "commit", "--amend", "-m", "x");

    [Fact]
    public void RunAddTracked_UsesDashU() => AssertArgs(GitCommands.RunAddTracked, [], "add", "-u");

    // === stash family ===

    [Fact]
    public void RunStash_UsesPush() => AssertArgs(GitCommands.RunStash, ["-m", "wip"], "stash", "push", "-m", "wip");

    [Fact]
    public void RunStashList_IsListOnly() => AssertArgs(GitCommands.RunStashList, ["ignored"], "stash", "list");

    [Fact]
    public void RunStashPop_IsPopOnly() => AssertArgs(GitCommands.RunStashPop, [], "stash", "pop");

    [Fact]
    public void RunStashApply_DefaultsToTopStash() => AssertArgs(GitCommands.RunStashApply, [], "stash", "apply", "stash@{0}");

    [Fact]
    public void RunStashApply_UsesGivenRef() => AssertArgs(GitCommands.RunStashApply, ["stash@{2}"], "stash", "apply", "stash@{2}");

    // === remote (default vs show) ===

    [Fact]
    public void RunRemote_NoArgs_UsesDashV() => AssertArgs(GitCommands.RunRemote, [], "remote", "-v");

    [Fact]
    public void RunRemote_Show_PassesThrough() => AssertArgs(GitCommands.RunRemote, ["show", "origin"], "remote", "show", "origin");

    // === single-positional handlers use only args[0] ===

    [Fact]
    public void RunShow_UsesOnlyFirstArg() => AssertArgs(GitCommands.RunShow, ["HEAD", "extra"], "show", "HEAD");

    [Fact]
    public void RunBlame_UsesOnlyFirstArg() => AssertArgs(GitCommands.RunBlame, ["file.cs", "extra"], "blame", "file.cs");

    [Fact]
    public void RunRevParse_SplatsAllArgs() => AssertArgs(GitCommands.RunRevParse, ["--abbrev-ref", "HEAD"], "rev-parse", "--abbrev-ref", "HEAD");

    [Fact]
    public void RunBranchCreate_UsesOnlyFirstArg() => AssertArgs(GitCommands.RunBranchCreate, ["feat", "extra"], "branch", "feat");

    [Fact]
    public void RunMerge_UsesOnlyFirstArg() => AssertArgs(GitCommands.RunMerge, ["feat", "extra"], "merge", "feat");

    [Fact]
    public void RunCherryPick_UsesOnlyFirstArg() => AssertArgs(GitCommands.RunCherryPick, ["abc123", "extra"], "cherry-pick", "abc123");

    [Fact]
    public void RunCheckout_SplatsAllArgs() => AssertArgs(GitCommands.RunCheckout, ["-b", "feat"], "checkout", "-b", "feat");

    [Fact]
    public void RunCheckoutFile_InsertsDoubleDash_UsesFirstArgOnly()
        => AssertArgs(GitCommands.RunCheckoutFile, ["src/a.cs", "extra"], "checkout", "--", "src/a.cs");

    [Fact]
    public void RunCommit_WithMessage_PassesThrough() => AssertArgs(GitCommands.RunCommit, ["-m", "msg"], "commit", "-m", "msg");

    [Fact]
    public void RunAdd_WithFiles_PassesThrough() => AssertArgs(GitCommands.RunAdd, ["a.cs", "b.cs"], "add", "a.cs", "b.cs");

    // === Usage guards ===
    // The positional-count guards (show / blame / rev-parse / branch-create / checkout / checkout-file /
    // merge / cherry-pick) moved to the declarative MinArgs check at the dispatch seam — covered by
    // DispatchTests.Execute_BelowMinArgs_*. `add` keeps an inline guard because its message carries an
    // extra hint ("use add-tracked"), and `commit` keeps one because it requires the -m *flag*, not a count.

    [Fact]
    public void RunAdd_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        Assert.Equal(1, GitCommands.RunAdd(ports, []));
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunCommit_WithoutMessage_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        Assert.Equal(1, GitCommands.RunCommit(ports, ["--all"])); // no -m
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunPush_PropagatesExecExitCode()
    {
        var (ports, exec, _) = Setup();
        exec.NextResult = new ExecResult(128, "", "rejected");
        Assert.Equal(128, GitCommands.RunPush(ports, []));
    }

    // --- helpers ---

    private static void AssertArgs(Func<Ports, string[], int> handler, string[] input, params string[] expected)
    {
        var (ports, exec, _) = Setup();
        handler(ports, input);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("git", call.Tool);
        Assert.Equal(expected, call.Args);
    }

    private static void RunAssertExit(int rc) => Assert.Equal(0, rc);
}
