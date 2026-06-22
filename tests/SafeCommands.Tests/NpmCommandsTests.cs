using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class NpmCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace()), exec, render);
    }

    [Fact]
    public void RunOutdated_HumanMode_NoJsonFlag()
    {
        var (ports, exec, _) = Setup();
        NpmCommands.RunOutdated(ports, []);
        Assert.Equal(new[] { "outdated" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunOutdated_JsonMode_AddsJsonFlag()
    {
        var (ports, exec, _) = Setup(jsonMode: true);
        NpmCommands.RunOutdated(ports, []);
        Assert.Equal(new[] { "outdated", "--json" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunList_PassesDepth()
    {
        var (ports, exec, _) = Setup();
        NpmCommands.RunList(ports, ["--depth", "2"]);
        Assert.Equal(new[] { "list", "--depth", "2" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunList_JsonMode_AddsJsonFlag_AndRendersStandardEnvelope()
    {
        // npm list --json was the lone handler that bypassed the envelope (raw passthrough); after
        // migration it routes through Run.Tool -> Render.Result like outdated/audit/view.
        var (ports, exec, render) = Setup(jsonMode: true);
        NpmCommands.RunList(ports, []);
        Assert.Equal(new[] { "list", "--json" }, Assert.Single(exec.Calls).Args);
        Assert.Single(render.Results);
    }

    [Fact]
    public void RunView_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = NpmCommands.RunView(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunView_JsonMode_AddsJsonFlag_ToFirstArgOnly()
    {
        var (ports, exec, _) = Setup(jsonMode: true);
        NpmCommands.RunView(ports, ["react", "ignored"]);
        Assert.Equal(new[] { "view", "react", "--json" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunInstall_WithoutIgnoreScripts_WarnsAndSpawns()
    {
        var (ports, exec, render) = Setup();
        NpmCommands.RunInstall(ports, []);
        Assert.Single(render.Warnings);
        Assert.Contains("postinstall", render.Warnings[0]);
        Assert.Equal(new[] { "install" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunInstall_WithIgnoreScripts_DoesNotWarn()
    {
        var (ports, exec, render) = Setup();
        NpmCommands.RunInstall(ports, ["--ignore-scripts"]);
        Assert.Empty(render.Warnings);
        Assert.Equal(new[] { "install", "--ignore-scripts" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunCi_WithoutIgnoreScripts_WarnsAndSpawns()
    {
        var (ports, exec, render) = Setup();
        NpmCommands.RunCi(ports, []);
        Assert.Single(render.Warnings);
        Assert.Contains("postinstall", render.Warnings[0]);
        Assert.Equal(new[] { "ci" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunCi_WithIgnoreScripts_DoesNotWarn()
    {
        var (ports, exec, render) = Setup();
        NpmCommands.RunCi(ports, ["--ignore-scripts"]);
        Assert.Empty(render.Warnings);
        Assert.Equal(new[] { "ci", "--ignore-scripts" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunAuditFix_SplitsAuditFixIntoTwoTokens()
    {
        var (ports, exec, _) = Setup();
        NpmCommands.RunAuditFix(ports, []);
        Assert.Equal(new[] { "audit", "fix" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunInstall_UnderJson_WarningDoesNotCorruptEnvelope()
    {
        // Real renderer: the postinstall warning must be suppressed under --json so stdout stays
        // valid JSON. (Legacy used OutputFormatter.WriteWarning, which always wrote to stdout.)
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var render = new ConsoleRenderer(jsonMode: true, stdout, stderr);
        var exec = new FakeExecutor();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace());

        NpmCommands.RunInstall(ports, []);

        var doc = JsonDocument.Parse(stdout.ToString()); // throws if warning leaked into stdout
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void RunScript_NoArgs_EmitsErrorAndAllowedScripts_DoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = NpmCommands.RunScript(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
        Assert.Single(render.Warnings); // "Allowed scripts: ..."
    }

    [Fact]
    public void RunScript_WithScript_SpawnsRun()
    {
        var (ports, exec, _) = Setup();
        NpmCommands.RunScript(ports, ["build"]);
        Assert.Equal(new[] { "run", "build" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunCacheClean_ForcesClean()
    {
        var (ports, exec, _) = Setup();
        NpmCommands.RunCacheClean(ports, []);
        Assert.Equal(new[] { "cache", "clean", "--force" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunBuild_RunsBuildScript()
    {
        var (ports, exec, _) = Setup();
        NpmCommands.RunBuild(ports, []);
        Assert.Equal(new[] { "run", "build" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunTest_PropagatesExecExitCode()
    {
        var (ports, exec, _) = Setup();
        exec.NextResult = new ExecResult(1, "", "");
        Assert.Equal(1, NpmCommands.RunTest(ports, []));
    }
}
