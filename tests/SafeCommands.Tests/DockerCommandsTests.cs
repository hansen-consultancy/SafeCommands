using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class DockerCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        return (new Ports(exec, render, new FakeGitRepo()), exec, render);
    }

    // ---- compose-down volume block ----

    [Theory]
    [InlineData("-v")]
    [InlineData("--volumes")]
    [InlineData("--rmi")]
    public void ComposeDown_VolumeFlag_IsBlocked(string flag)
    {
        var (ports, exec, render) = Setup();

        var rc = DockerCommands.RunComposeDown(ports, [flag]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("volumes", render.Blocks[0].Reason);
    }

    [Fact]
    public void ComposeDown_NoFlags_Spawns()
    {
        var (ports, exec, _) = Setup();

        var rc = DockerCommands.RunComposeDown(ports, []);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("docker", call.Tool);
        Assert.Equal(new[] { "compose", "down" }, call.Args);
    }

    // ---- build flag filter ----

    [Fact]
    public void Build_FiltersDisallowedFlags_Silently()
    {
        var (ports, exec, _) = Setup();

        DockerCommands.RunBuild(ports, ["--privileged", "-t", "myimg"]);

        var call = Assert.Single(exec.Calls);
        // --privileged dropped; -t myimg passed through; "." appended
        Assert.Equal(new[] { "build", "-t", "myimg", "." }, call.Args);
    }

    // ---- compose-up flag filter ----

    [Fact]
    public void ComposeUp_FiltersDisallowedFlags()
    {
        var (ports, exec, _) = Setup();

        DockerCommands.RunComposeUp(ports, ["-d", "--privileged"]);

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "compose", "up", "-d" }, call.Args);
    }

    // ---- logs strips -f ----

    [Fact]
    public void Logs_RemovesFollowFlag()
    {
        var (ports, exec, _) = Setup();

        DockerCommands.RunLogs(ports, ["mycontainer", "-f"]);

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "logs", "mycontainer" }, call.Args);
    }

    [Fact]
    public void Logs_NoArgs_EmitsError()
    {
        var (ports, exec, render) = Setup();

        var rc = DockerCommands.RunLogs(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }
}
