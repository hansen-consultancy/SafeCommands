using System.Text.Json;
using SafeCommands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Pins the CLI outer shell extracted from Program.cs: the proxy-aware <c>--json</c> splice
/// (<see cref="Cli.StripJson"/>, pure) and the routing (<see cref="Cli.Route"/>) — unknown-group /
/// unknown-command errors, per-command <c>--help</c>, and the guarded handler dispatch — none of
/// which were testable while they lived as Program.cs top-level statements. Meta commands
/// (help/version) still render to the console (not yet migrated), so those branches are asserted
/// only by exit code.
/// </summary>
public class CliTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        CommandRegistry.Initialize();
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), exec, render);
    }

    private static JsonElement AsJson(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

    // === StripJson (proxy-aware) ===

    [Fact]
    public void StripJson_NoFlag_LeavesArgsUnchanged()
    {
        var (json, args) = Cli.StripJson(["git", "status"]);
        Assert.False(json);
        Assert.Equal(new[] { "git", "status" }, args);
    }

    [Fact]
    public void StripJson_NonProxy_ConsumesFlagAnywhere()
    {
        var (json, args) = Cli.StripJson(["git", "status", "--json"]);
        Assert.True(json);
        Assert.Equal(new[] { "git", "status" }, args);
    }

    [Fact]
    public void StripJson_BeforeProxy_IsConsumedAndStripped()
    {
        var (json, args) = Cli.StripJson(["--json", "proxy", "gh", "pr", "list"]);
        Assert.True(json);
        Assert.Equal(new[] { "proxy", "gh", "pr", "list" }, args);
    }

    [Fact]
    public void StripJson_AfterProxy_IsLeftForProxiedTool()
    {
        // `gh pr list --json number` — the --json belongs to gh, not us.
        var (json, args) = Cli.StripJson(["proxy", "gh", "pr", "list", "--json", "number"]);
        Assert.False(json);
        Assert.Equal(new[] { "proxy", "gh", "pr", "list", "--json", "number" }, args);
    }

    [Fact]
    public void StripJson_ProxyFirst_FlagAfterProxyKept()
    {
        // proxy at index 0 -> no pre-proxy segment -> the --json at index 1 is the proxied tool's.
        var (json, args) = Cli.StripJson(["proxy", "--json", "gh"]);
        Assert.False(json);
        Assert.Equal(new[] { "proxy", "--json", "gh" }, args);
    }

    [Fact]
    public void StripJson_Empty_ReturnsFalseAndEmpty()
    {
        var (json, args) = Cli.StripJson([]);
        Assert.False(json);
        Assert.Empty(args);
    }

    // === Route: meta + empty/bare-group paths (meta handlers render to the console, so these are
    // asserted by exit code only — but that exit code is the load-bearing routing signal) ===

    [Theory]
    [InlineData("help")]
    [InlineData("-h")]
    [InlineData("h")]
    [InlineData("version")]
    [InlineData("--version")]
    [InlineData("instructions")]
    [InlineData("setup")]
    public void Route_MetaCommand_ReturnsZero_NoSpawn(string arg)
    {
        var (ports, exec, _) = Setup();
        Assert.Equal(0, Cli.Route(ports, [arg], jsonOutput: false));
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void Route_EmptyArgs_ReturnsHelp()
    {
        var (ports, _, _) = Setup();
        Assert.Equal(0, Cli.Route(ports, [], jsonOutput: false));
    }

    [Fact]
    public void Route_JsonOnly_StripsToEmpty_ReturnsHelpCleanly_NoThrow()
    {
        // Regression pin for the slice's one intentional fix: `safe --json` (only the flag) used to throw
        // IndexOutOfRange (the length check ran BEFORE the --json strip), uncaught. Now it strips to []
        // and Route returns help cleanly. Drives the full StripJson -> Route path.
        var (ports, _, _) = Setup();
        var (json, args) = Cli.StripJson(["--json"]);
        Assert.True(json);
        Assert.Empty(args);
        Assert.Equal(0, Cli.Route(ports, args, json));
    }

    [Fact]
    public void Route_BareKnownGroup_ShowsGroupHelp_ReturnsZero_NoSpawn()
    {
        // Single arg that IS a known group -> group help (the sibling of the unknown-command branch).
        var (ports, exec, _) = Setup();
        Assert.Equal(0, Cli.Route(ports, ["git"], jsonOutput: false));
        Assert.Empty(exec.Calls);
    }

    // === Route: error paths (rendered via ports.Render -> testable) ===

    [Fact]
    public void Route_UnknownGroup_EmitsErrorAndAvailableGroups()
    {
        var (ports, exec, render) = Setup();
        var rc = Cli.Route(ports, ["bogusgroup", "cmd"], jsonOutput: false);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("Unknown group: bogusgroup", render.Errors);
        Assert.Contains(render.Infos, i => i.StartsWith("Available groups:"));
    }

    [Fact]
    public void Route_KnownGroupUnknownCommand_EmitsErrorAndAvailableCommands()
    {
        var (ports, exec, render) = Setup();
        var rc = Cli.Route(ports, ["git", "definitely-not-a-command"], jsonOutput: false);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("Unknown command: git definitely-not-a-command", render.Errors);
        Assert.Contains(render.Infos, i => i.Contains("Available git commands:"));
    }

    [Fact]
    public void Route_BareUnknownArg_EmitsUnknownCommand()
    {
        var (ports, _, render) = Setup();
        var rc = Cli.Route(ports, ["notagroup"], jsonOutput: false);
        Assert.Equal(1, rc);
        Assert.Contains("Unknown command: notagroup", render.Errors);
    }

    // === Route: per-command --help (dual-mode, via ports.Render) ===

    [Fact]
    public void Route_PerCommandHelp_HumanMode_EmitsUsage_NoSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = Cli.Route(ports, ["docker", "ps", "--help"], jsonOutput: false);
        Assert.Equal(0, rc);
        Assert.Empty(exec.Calls);            // handler not invoked
        Assert.Contains(render.Infos, i => i.StartsWith("Usage:"));
    }

    [Fact]
    public void Route_PerCommandHelp_JsonMode_EmitsHelpPayload()
    {
        var (ports, exec, render) = Setup(jsonMode: true);
        var rc = Cli.Route(ports, ["docker", "ps", "--help"], jsonOutput: true);
        Assert.Equal(0, rc);
        Assert.Empty(exec.Calls);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("docker ps", json.GetProperty("command").GetString());
        Assert.Equal("safe docker ps [--all]", json.GetProperty("usage").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("safety").GetString()));
    }

    // === Route: execute delegation ===

    [Fact]
    public void Route_AllowedCommand_DelegatesToDispatcher_Spawns()
    {
        var (ports, exec, render) = Setup();
        var rc = Cli.Route(ports, ["docker", "ps"], jsonOutput: false);
        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("docker", call.Tool);
        Assert.Equal(new[] { "ps" }, call.Args);
    }

    [Fact]
    public void Route_BlockedCommand_RendersBlock_NoSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = Cli.Route(ports, ["git", "push", "--force"], jsonOutput: false);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Blocks);
    }

    // === Route: the global try/catch around the handler ===

    private sealed class ThrowingExecutor : IExecutor
    {
        public ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null)
            => throw new InvalidOperationException("spawn exploded");
    }

    [Fact]
    public void Route_HandlerThrows_IsCaught_RendersCommandFailed_Returns1()
    {
        CommandRegistry.Initialize();
        var render = new FakeRenderer();
        // docker ps reaches Run.Tool -> p.Exec.Run, which throws here; Route's try/catch must contain it.
        var ports = new Ports(new ThrowingExecutor(), render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = Cli.Route(ports, ["docker", "ps"], jsonOutput: false);

        Assert.Equal(1, rc);
        Assert.Contains(render.Errors, e => e.StartsWith("Command failed:"));
    }

    [Fact]
    public void Route_HandlerThrows_JsonMode_EmitsErrorPayload()
    {
        CommandRegistry.Initialize();
        var render = new FakeRenderer { JsonMode = true };
        var ports = new Ports(new ThrowingExecutor(), render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = Cli.Route(ports, ["docker", "ps"], jsonOutput: true);

        Assert.Equal(1, rc);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.True(json.GetProperty("error").GetBoolean());
        Assert.Equal("docker ps", json.GetProperty("command").GetString());
    }
}
