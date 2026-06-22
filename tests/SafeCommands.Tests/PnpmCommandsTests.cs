using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class PnpmCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), exec, render);
    }

    [Fact]
    public void RunOutdated_SpawnsPnpmOutdated()
    {
        var (ports, exec, _) = Setup();
        PnpmCommands.RunOutdated(ports, []);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("pnpm", call.Tool);
        Assert.Equal(new[] { "outdated" }, call.Args);
    }

    [Fact]
    public void RunInstall_SpawnsPnpmInstall()
    {
        var (ports, exec, _) = Setup();
        PnpmCommands.RunInstall(ports, []);
        Assert.Equal(new[] { "install" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunBuild_RunsBuildScript()
    {
        var (ports, exec, _) = Setup();
        PnpmCommands.RunBuild(ports, []);
        Assert.Equal(new[] { "run", "build" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunStorePrune_SpawnsStorePrune()
    {
        var (ports, exec, _) = Setup();
        PnpmCommands.RunStorePrune(ports, []);
        Assert.Equal(new[] { "store", "prune" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunWhy_WithPackage_Spawns()
    {
        var (ports, exec, _) = Setup();
        PnpmCommands.RunWhy(ports, ["react"]);
        Assert.Equal(new[] { "why", "react" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunScript_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = PnpmCommands.RunScript(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunScript_WithScript_SpawnsRun()
    {
        var (ports, exec, _) = Setup();
        PnpmCommands.RunScript(ports, ["build"]);
        Assert.Equal(new[] { "run", "build" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunDedupe_PropagatesExecExitCode()
    {
        var (ports, exec, _) = Setup();
        exec.NextResult = new ExecResult(5, "", "");
        Assert.Equal(5, PnpmCommands.RunDedupe(ports, []));
    }
}
