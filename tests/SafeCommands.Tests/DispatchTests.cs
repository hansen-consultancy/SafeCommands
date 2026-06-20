using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Exercises the single enforcement seam <see cref="CommandDispatcher.Execute"/>: policy is
/// evaluated before the handler runs, a block renders the envelope and never spawns the tool,
/// and a rewrite threads the trimmed safe args into the handler.
/// </summary>
public class DispatchTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace()), exec, render);
    }

    private static CommandDefinition Cmd(Policy policy, Func<Ports, string[], int> handler)
        => new CommandDefinition("grp", "cmd", "desc", "usage", SafetyLevel.SafeWrite, handler)
            { Policy = policy };

    private static int Spawn(Ports p, string[] args)
    {
        p.Exec.Run("tool", args);
        return 0;
    }

    [Fact]
    public void Execute_BlockedPolicy_RendersBlock_NeverSpawns_Returns1()
    {
        var (ports, exec, render) = Setup();
        var cmd = Cmd(Policy.Default.BlockFlags(["--force"], "no force", "drop it"), Spawn);

        var rc = CommandDispatcher.Execute(cmd, ports, "grp", "cmd", ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var block = Assert.Single(render.Blocks);
        Assert.Equal("grp cmd --force", block.Command);
        Assert.Equal("no force", block.Reason);
        Assert.Equal("drop it", block.Suggestion);
    }

    [Fact]
    public void Execute_AllowingPolicy_RunsHandler_NoBlock()
    {
        var (ports, exec, render) = Setup();
        var cmd = Cmd(Policy.Default.BlockFlags(["--force"], "no force", "drop it"), Spawn);

        var rc = CommandDispatcher.Execute(cmd, ports, "grp", "cmd", ["status"]);

        Assert.Equal(0, rc);
        Assert.Single(exec.Calls);
        Assert.Empty(render.Blocks);
    }

    [Fact]
    public void Execute_RewritePolicy_HandlerReceivesTrimmedArgs()
    {
        var (ports, exec, render) = Setup();
        string[]? seen = null;
        var cmd = Cmd(
            Policy.Default.AllowOnlyFlags(["--graph"], [], keepPositionals: true),
            (p, args) => { seen = args; return 0; });

        var rc = CommandDispatcher.Execute(cmd, ports, "grp", "cmd", ["--graph", "--force"]);

        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);
        Assert.Equal(new[] { "--graph" }, seen);  // --force dropped before the handler saw it
    }

    // ---- Relocated bun integration (was BunCommandsTests, now driven through the dispatcher) ----

    [Fact]
    public void Execute_BunRun_UnknownScript_IsBlocked_WithoutSpawningBun()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("bun", "run");
        Assert.NotNull(cmd);
        var (ports, exec, render) = Setup();

        var rc = CommandDispatcher.Execute(cmd, ports, "bun", "run", ["nonsense"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var block = Assert.Single(render.Blocks);
        Assert.Equal("bun run nonsense", block.Command);
        Assert.Contains("not in the allowed list", block.Reason);
    }

    [Fact]
    public void Execute_BunRun_AllowedScript_SpawnsBunRunBuild()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("bun", "run");
        Assert.NotNull(cmd);
        var (ports, exec, render) = Setup();

        var rc = CommandDispatcher.Execute(cmd, ports, "bun", "run", ["build"]);

        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("bun", call.Tool);
        Assert.Equal(new[] { "run", "build" }, call.Args);
    }

    // ---- End-to-end --json fork: real renderer, blocked command parses as JSON ----

    [Fact]
    public void Execute_BlockedUnderJsonMode_EmitsBlockedJson()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var render = new ConsoleRenderer(jsonMode: true, stdout, stderr);
        var exec = new FakeExecutor();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace());
        var cmd = Cmd(Policy.Default.BlockFlags(["--force"], "no force", "drop it"), Spawn);

        var rc = CommandDispatcher.Execute(cmd, ports, "grp", "cmd", ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Equal("grp cmd --force", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("no force", doc.RootElement.GetProperty("reason").GetString());
    }

    // ---- proxy run re-dispatcher (3c): deterministic — these block/usage-error before any spawn ----

    [Fact]
    public void Execute_ProxyRun_NoArgs_UsageError_NoSpawn()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("proxy", "run");
        Assert.NotNull(cmd);
        var (ports, exec, render) = Setup();

        var rc = CommandDispatcher.Execute(cmd, ports, "proxy", "run", []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.NotEmpty(render.Errors);  // usage path
    }

    [Fact]
    public void Execute_ProxyRun_UnknownTool_Blocked_NoSpawn()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("proxy", "run");
        Assert.NotNull(cmd);
        var (ports, exec, render) = Setup();

        var rc = CommandDispatcher.Execute(cmd, ports, "proxy", "run", ["definitely-not-a-tool", "x"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var block = Assert.Single(render.Blocks);
        Assert.Contains("not in the proxy allowlist", block.Reason);
    }

    [Fact]
    public void Execute_ProxyRun_RedispatchEnforcesTargetPolicy_GhApiFlagBlocked_NoSpawn()
    {
        // The single-source-of-truth guarantee: "proxy run gh api -X POST" re-dispatches to gh's
        // command, whose policy blocks the flag (gh api allows no flags) before any spawn.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("proxy", "run");
        Assert.NotNull(cmd);
        var (ports, exec, render) = Setup();

        var rc = CommandDispatcher.Execute(cmd, ports, "proxy", "run", ["gh", "api", "-X", "POST"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);  // gh's policy blocked the flag; CommandExists/Run.Tool never reached
        Assert.NotEmpty(render.Blocks);
    }

    [Fact]
    public void Execute_ProxyRun_MixedCaseTool_ResolvesAndEnforcesTargetPolicy()
    {
        // CommandRegistry.Find is OrdinalIgnoreCase, so "proxy run GH ..." resolves the gh command.
        // The proof is the block REASON: "not allowed for this subcommand" can only arise from gh's
        // own policy (api prefix matched, -X rejected). Had GH failed to resolve, the reason would be
        // "not in the proxy allowlist" instead — so this distinguishes resolution from a generic block.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("proxy", "run");
        Assert.NotNull(cmd);
        var (ports, exec, render) = Setup();

        var rc = CommandDispatcher.Execute(cmd, ports, "proxy", "run", ["GH", "api", "-X", "POST"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var block = Assert.Single(render.Blocks);
        Assert.Contains("not allowed for this subcommand", block.Reason);
    }

    [Fact]
    public void Execute_ProxyGhBlocked_UnderJsonMode_EmitsBlockedJson()
    {
        // Closes the --json fork for proxy: a per-tool policy block renders the JSON envelope.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("proxy", "gh");
        Assert.NotNull(cmd);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var render = new ConsoleRenderer(jsonMode: true, stdout, stderr);
        var exec = new FakeExecutor();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace());

        var rc = CommandDispatcher.Execute(cmd, ports, "proxy", "gh", ["api", "-X", "POST"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Contains("proxy gh", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void Execute_PathOutsideProject_UnderJsonMode_EmitsBlockedJson()
    {
        // Path-containment block flows through the same central render path as flag blocks.
        // A FakeWorkspace whose containment predicate is always-false makes every path "outside".
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("file", "read");
        Assert.NotNull(cmd);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var render = new ConsoleRenderer(jsonMode: true, stdout, stderr);
        var exec = new FakeExecutor();
        var ws = new FakeWorkspace { ProjectRoot = "/proj", WithinPredicate = _ => false };
        var ports = new Ports(exec, render, new FakeRepoProbe(), ws);

        var rc = CommandDispatcher.Execute(cmd, ports, "file", "read", ["/etc/passwd"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);  // handler never ran (the policy blocked before dispatch)
        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Contains("file read", doc.RootElement.GetProperty("command").GetString());
        Assert.Contains("outside the project directory", doc.RootElement.GetProperty("reason").GetString());
    }

    // ---- handler/policy case agreement: the --in flag the handler honors is the one the policy checks ----

    [Fact]
    public void Execute_DeletePattern_UppercaseInFlag_StillEnforcesSafeDir()
    {
        // Safety-mirror regression: handlers read --in via the case-insensitive Args helper, so the
        // policy's PathArg.FlagValue("--in") must also match "--IN". If it stayed ordinal, this would
        // sail past RequireWithinSafeDeleteDir and the handler would delete inside /proj/src.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("file", "delete-pattern");
        Assert.NotNull(cmd);
        var (ports, exec, render) = Setup();  // FakeWorkspace ProjectRoot "/proj", default containment

        var rc = CommandDispatcher.Execute(cmd, ports, "file", "delete-pattern", ["*.log", "--IN", "/proj/src"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var block = Assert.Single(render.Blocks);
        Assert.Contains("not inside a safe delete directory", block.Reason);
    }
}
