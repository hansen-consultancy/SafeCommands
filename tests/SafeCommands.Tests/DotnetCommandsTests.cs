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
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace()), exec, render);
    }

    [Fact]
    public void RunListPackage_NoProject_OmitsProjectToken()
    {
        var (ports, exec, _) = Setup();
        DotnetCommands.RunListPackage(ports, []);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("dotnet", call.Tool);
        Assert.Equal(new[] { "list", "package" }, call.Args);
    }

    [Fact]
    public void RunListPackage_WithProject_InsertsProjectBetweenListAndPackage()
    {
        var (ports, exec, _) = Setup();
        DotnetCommands.RunListPackage(ports, ["src/App.csproj"]);
        Assert.Equal(new[] { "list", "src/App.csproj", "package" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunListReference_WithProject_InsertsProjectBetweenListAndReference()
    {
        var (ports, exec, _) = Setup();
        DotnetCommands.RunListReference(ports, ["App.csproj"]);
        Assert.Equal(new[] { "list", "App.csproj", "reference" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunSlnList_WithSolution_InsertsSolutionBetweenSlnAndList()
    {
        var (ports, exec, _) = Setup();
        DotnetCommands.RunSlnList(ports, ["App.sln"]);
        Assert.Equal(new[] { "sln", "App.sln", "list" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunInfo_PassesInfoFlag()
    {
        var (ports, exec, _) = Setup();
        DotnetCommands.RunInfo(ports, []);
        Assert.Equal(new[] { "--info" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunBuild_PassesProjectArgs()
    {
        var (ports, exec, _) = Setup();
        DotnetCommands.RunBuild(ports, ["App.csproj", "-c", "Release"]);
        Assert.Equal(new[] { "build", "App.csproj", "-c", "Release" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunToolInstall_WithTool_BuildsGlobalInstall()
    {
        var (ports, exec, _) = Setup();
        DotnetCommands.RunToolInstall(ports, ["dotnet-ef"]);
        Assert.Equal(new[] { "tool", "install", "-g", "dotnet-ef" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunToolInstall_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = DotnetCommands.RunToolInstall(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunNew_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = DotnetCommands.RunNew(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunTest_PropagatesExecExitCode()
    {
        var (ports, exec, _) = Setup();
        exec.NextResult = new ExecResult(3, "", "");
        Assert.Equal(3, DotnetCommands.RunTest(ports, []));
    }
}
