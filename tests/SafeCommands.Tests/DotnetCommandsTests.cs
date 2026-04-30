using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class DotnetCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        return (new Ports(exec, render), exec, render);
    }

    [Fact]
    public void Build_Passthrough()
    {
        var (ports, exec, _) = Setup();

        DotnetCommands.RunBuild(ports, ["-c", "Release"]);

        var call = Assert.Single(exec.Calls);
        Assert.Equal("dotnet", call.Tool);
        Assert.Equal(new[] { "build", "-c", "Release" }, call.Args);
    }

    [Fact]
    public void ListPackage_NoProject_OmitsProjectArg()
    {
        var (ports, exec, _) = Setup();

        DotnetCommands.RunListPackage(ports, []);

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "list", "package" }, call.Args);
    }

    [Fact]
    public void ListPackage_WithProject_PassesProject()
    {
        var (ports, exec, _) = Setup();

        DotnetCommands.RunListPackage(ports, ["src/Foo.csproj"]);

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "list", "src/Foo.csproj", "package" }, call.Args);
    }

    [Fact]
    public void ToolInstall_RequiresName()
    {
        var (ports, exec, render) = Setup();

        var rc = DotnetCommands.RunToolInstall(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void ToolInstall_WithName_Spawns()
    {
        var (ports, exec, _) = Setup();

        DotnetCommands.RunToolInstall(ports, ["dotnet-ef"]);

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "tool", "install", "-g", "dotnet-ef" }, call.Args);
    }

    [Fact]
    public void AddPackage_RequiresName()
    {
        var (ports, exec, render) = Setup();

        var rc = DotnetCommands.RunAddPackage(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }
}
