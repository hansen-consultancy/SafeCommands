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
        return (new Ports(exec, render), exec, render);
    }

    [Fact]
    public void RunScript_UnknownScript_IsBlocked()
    {
        var (ports, exec, render) = Setup();

        var rc = PnpmCommands.RunScript(ports, ["nonsense"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("not in the allowed list", render.Blocks[0].Reason);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("lint")]
    public void RunScript_AllowedScript_Spawns(string script)
    {
        var (ports, exec, _) = Setup();

        var rc = PnpmCommands.RunScript(ports, [script]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("pnpm", call.Tool);
        Assert.Equal(new[] { "run", script }, call.Args);
    }

    [Fact]
    public void RunWhy_RequiresPackage()
    {
        var (ports, exec, render) = Setup();

        var rc = PnpmCommands.RunWhy(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunInstall_NoWarning_BecauseLifecycleScriptsDisabledByDefault()
    {
        // Unlike npm, pnpm doesn't run lifecycle scripts by default.
        var (ports, _, render) = Setup();

        PnpmCommands.RunInstall(ports, []);

        Assert.Empty(render.Warnings);
    }
}
