using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class NpmCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render), exec, render);
    }

    // ---- audit-fix --force block ----

    [Fact]
    public void AuditFix_Force_IsBlocked()
    {
        var (ports, exec, render) = Setup();

        var rc = NpmCommands.RunAuditFix(ports, ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("breaking major version", render.Blocks[0].Reason);
    }

    [Fact]
    public void AuditFix_NoForce_Spawns()
    {
        var (ports, exec, _) = Setup();

        var rc = NpmCommands.RunAuditFix(ports, []);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "audit", "fix" }, call.Args);
    }

    // ---- script allowlist ----

    [Fact]
    public void RunScript_UnknownScript_IsBlocked()
    {
        var (ports, exec, render) = Setup();

        var rc = NpmCommands.RunScript(ports, ["nonsense"]);

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

        var rc = NpmCommands.RunScript(ports, [script]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "run", script }, call.Args);
    }

    [Fact]
    public void RunScript_NoArgs_EmitsErrorAndAllowedHint()
    {
        var (ports, exec, render) = Setup();

        var rc = NpmCommands.RunScript(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
        Assert.Single(render.Warnings);
        Assert.Contains("Allowed scripts", render.Warnings[0]);
    }

    // ---- install/ci warnings ----

    [Fact]
    public void Install_WithoutIgnoreScripts_EmitsWarning()
    {
        var (ports, _, render) = Setup();

        NpmCommands.RunInstall(ports, []);

        Assert.Single(render.Warnings);
        Assert.Contains("postinstall", render.Warnings[0]);
    }

    [Fact]
    public void Install_WithIgnoreScripts_NoWarning()
    {
        var (ports, _, render) = Setup();

        NpmCommands.RunInstall(ports, ["--ignore-scripts"]);

        Assert.Empty(render.Warnings);
    }

    [Fact]
    public void Ci_WithoutIgnoreScripts_EmitsWarning()
    {
        var (ports, _, render) = Setup();

        NpmCommands.RunCi(ports, []);

        Assert.Single(render.Warnings);
    }

    // ---- json-mode aware passthroughs ----

    [Fact]
    public void Outdated_InJsonMode_AppendsJsonFlag()
    {
        var (ports, exec, _) = Setup(jsonMode: true);

        NpmCommands.RunOutdated(ports, []);

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "outdated", "--json" }, call.Args);
    }

    [Fact]
    public void Outdated_InHumanMode_NoJsonFlag()
    {
        var (ports, exec, _) = Setup(jsonMode: false);

        NpmCommands.RunOutdated(ports, []);

        var call = Assert.Single(exec.Calls);
        Assert.Equal(new[] { "outdated" }, call.Args);
    }

    [Fact]
    public void View_RequiresPackage()
    {
        var (ports, exec, render) = Setup();

        var rc = NpmCommands.RunView(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }
}
