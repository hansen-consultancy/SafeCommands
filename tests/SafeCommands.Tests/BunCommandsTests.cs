using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class BunCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), exec, render);
    }

    // NOTE: the "blocked policy never spawns bun" assertion lives in DispatchTests now.
    // Policy moved off RunScript to the dispatch site, so calling RunScript directly with an
    // unknown script would spawn bun — the guarantee is only meaningful through CommandDispatcher.

    [Theory]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("lint")]
    public void RunScript_AllowedScripts_AreAccepted_AndSpawnBun(string script)
    {
        var (ports, exec, render) = Setup();

        var rc = BunCommands.RunScript(ports, [script]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("bun", call.Tool);
        Assert.Equal(new[] { "run", script }, call.Args);
        Assert.Empty(render.Blocks);
    }

    [Fact]
    public void RunScript_AllowedScript_IsCaseInsensitive()
    {
        var (ports, exec, _) = Setup();

        var rc = BunCommands.RunScript(ports, ["BUILD"]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "run", "BUILD" }, call.Args);  // arg case preserved; only policy match is case-insensitive
    }

    [Fact]
    public void RunScript_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();

        var rc = BunCommands.RunScript(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    // ---- Install warning ----

    [Fact]
    public void RunInstall_WithoutIgnoreScripts_EmitsWarning_AndStillSpawns()
    {
        var (ports, exec, render) = Setup();

        var rc = BunCommands.RunInstall(ports, []);

        Assert.Equal(0, rc);
        Assert.Single(render.Warnings);
        Assert.Contains("lifecycle scripts", render.Warnings[0]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "install" }, call.Args);
    }

    [Fact]
    public void RunInstall_WithIgnoreScripts_DoesNotWarn()
    {
        var (ports, exec, render) = Setup();

        var rc = BunCommands.RunInstall(ports, ["--ignore-scripts"]);

        Assert.Equal(0, rc);
        Assert.Empty(render.Warnings);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "install", "--ignore-scripts" }, call.Args);
    }

    // ---- Plain passthroughs ----

    [Fact]
    public void RunOutdated_SpawnsBunOutdated()
    {
        var (ports, exec, _) = Setup();
        BunCommands.RunOutdated(ports, []);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("bun", call.Tool);
        Assert.Equal(new[] { "outdated" }, call.Args);
    }

    [Fact]
    public void RunPmLs_SpawnsBunPmLs()
    {
        var (ports, exec, _) = Setup();
        BunCommands.RunPmLs(ports, []);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "pm", "ls" }, call.Args);
    }

    [Fact]
    public void RunBuild_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();

        var rc = BunCommands.RunBuild(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunBuild_WithEntrypoint_Spawns()
    {
        var (ports, exec, _) = Setup();
        BunCommands.RunBuild(ports, ["src/index.ts"]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "build", "src/index.ts" }, call.Args);
    }

    // ---- Exit code propagation ----

    [Fact]
    public void RunBun_PropagatesExecExitCode()
    {
        var (ports, exec, _) = Setup();
        exec.NextResult = new ExecResult(42, "stdout", "stderr");

        var rc = BunCommands.RunTest(ports, []);

        Assert.Equal(42, rc);
    }
}
