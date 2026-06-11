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
}
