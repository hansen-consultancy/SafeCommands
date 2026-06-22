using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class DockerCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace()), exec, render);
    }

    [Fact]
    public void RunPs_SpawnsDockerPs()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunPs(ports, ["--all"]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("docker", call.Tool);
        Assert.Equal(new[] { "ps", "--all" }, call.Args);
    }

    [Fact]
    public void RunStats_InjectsNoStream()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunStats(ports, []);
        Assert.Equal(new[] { "stats", "--no-stream" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunComposeUp_PrependsCompose()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunComposeUp(ports, ["-d", "web"]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("docker", call.Tool);
        Assert.Equal(new[] { "compose", "up", "-d", "web" }, call.Args);
    }

    [Fact]
    public void RunBuild_AppendsContextDot()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunBuild(ports, ["-t", "img"]);
        Assert.Equal(new[] { "build", "-t", "img", "." }, Assert.Single(exec.Calls).Args);
    }

    [Theory]
    [InlineData("-f")]
    [InlineData("--follow")]
    public void RunLogs_StripsFollowFlag(string follow)
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunLogs(ports, ["web", follow, "--tail", "10"]);
        Assert.Equal(new[] { "logs", "web", "--tail", "10" }, Assert.Single(exec.Calls).Args);
    }

    [Theory]
    [InlineData("-f")]
    [InlineData("--follow")]
    public void RunComposeLogs_StripsFollowFlag(string follow)
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunComposeLogs(ports, [follow, "web"]);
        Assert.Equal(new[] { "compose", "logs", "web" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunNetworkLs_SplitsNetworkLs()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunNetworkLs(ports, []);
        Assert.Equal(new[] { "network", "ls" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunVolumeLs_SplitsVolumeLs()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunVolumeLs(ports, []);
        Assert.Equal(new[] { "volume", "ls" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunInspect_UsesOnlyFirstArg()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunInspect(ports, ["web", "extra"]);
        Assert.Equal(new[] { "inspect", "web" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunStop_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = DockerCommands.RunStop(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunComposeDown_SpawnsComposeDown()
    {
        var (ports, exec, _) = Setup();
        DockerCommands.RunComposeDown(ports, []);
        Assert.Equal(new[] { "compose", "down" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunPs_PropagatesExecExitCode()
    {
        var (ports, exec, _) = Setup();
        exec.NextResult = new ExecResult(7, "", "boom");
        Assert.Equal(7, DockerCommands.RunPs(ports, []));
    }
}
