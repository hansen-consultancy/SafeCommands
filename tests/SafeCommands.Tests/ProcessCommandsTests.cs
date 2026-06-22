using System.Runtime.InteropServices;
using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Handler-level tests for the migrated process group. Enumeration/kill go through a
/// <see cref="FakeProcessHost"/> (so no real process is ever listed or killed) and the
/// netstat/ss/lsof port queries through a <see cref="FakeExecutor"/>. The kill-name dev-tools
/// allowlist is a dispatch-level Policy, not handler logic, so it is asserted in
/// MigratedCommandPolicyTests (both the policy-direct block and the new dispatch allow/block pair).
/// </summary>
public class ProcessCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render, FakeProcessHost host) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        var host = new FakeProcessHost();
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), host), exec, render, host);
    }

    private static JsonElement AsJson(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

    // RunPorts queries netstat on Windows; on Unix it first probes `which ss` then runs ss
    // (NextResult exit 0 => ss available). Pins the exact tool+args this slice routed through IExecutor.
    private static void AssertPortsQueried(FakeExecutor exec)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var call = Assert.Single(exec.Calls);
            Assert.Equal("netstat", call.Tool);
            Assert.Equal(new[] { "-ano", "-p", "TCP" }, call.Args);
        }
        else
        {
            Assert.Equal("which", exec.Calls[0].Tool);
            Assert.Equal(new[] { "ss" }, exec.Calls[0].Args);
            Assert.Equal("ss", exec.Calls[1].Tool);
            Assert.Equal(new[] { "-tlnp" }, exec.Calls[1].Args);
        }
    }

    // === list ===

    [Fact]
    public void RunList_NoFilter_EmitsSnapshotOrderedByName()
    {
        var (ports, _, render, host) = Setup();
        host.Table.Add(new ProcessInfo(2, "zebra", 1024 * 1024));
        host.Table.Add(new ProcessInfo(1, "alpha", 2 * 1024 * 1024));
        Assert.Equal(0, ProcessCommands.RunList(ports, []));
        Assert.Equal(2, render.Infos.Count);
        Assert.Contains("alpha", render.Infos[0]); // ordered by name
        Assert.Contains("2 MB", render.Infos[0]);  // memory rendered as MiB
        Assert.Contains("zebra", render.Infos[1]);
        Assert.Contains("1 MB", render.Infos[1]);
    }

    [Fact]
    public void RunList_Filter_MatchesNameCaseInsensitive()
    {
        var (ports, _, render, host) = Setup(jsonMode: true);
        host.Table.Add(new ProcessInfo(1, "node", 4 * 1024 * 1024));
        host.Table.Add(new ProcessInfo(2, "dotnet", 0));
        ProcessCommands.RunList(ports, ["--filter", "NODE"]);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal(1, json.GetProperty("count").GetInt32());
        var proc = json.GetProperty("processes")[0];
        Assert.Equal("node", proc.GetProperty("name").GetString());
        Assert.Equal(4 * 1024 * 1024, proc.GetProperty("memory").GetInt64()); // raw bytes in JSON
    }

    [Fact]
    public void RunList_CapsAtOneHundred()
    {
        var (ports, _, render, host) = Setup();
        for (int i = 0; i < 150; i++) host.Table.Add(new ProcessInfo(i, $"p{i:D3}", 0));
        ProcessCommands.RunList(ports, []);
        Assert.Equal(100, render.Infos.Count);
    }

    // === find ===

    [Fact]
    public void RunFind_NoArgs_EmitsError()
    {
        var (ports, _, render, _) = Setup();
        Assert.Equal(1, ProcessCommands.RunFind(ports, []));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunFind_Match_EmitsRow()
    {
        var (ports, _, render, host) = Setup();
        host.Table.Add(new ProcessInfo(7, "node", 0));
        host.Table.Add(new ProcessInfo(8, "other", 0));
        ProcessCommands.RunFind(ports, ["node"]);
        Assert.Contains("node", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunFind_NoMatch_ReportsNone()
    {
        var (ports, _, render, _) = Setup();
        ProcessCommands.RunFind(ports, ["ghost"]);
        Assert.Equal("No processes found matching 'ghost'", Assert.Single(render.Infos));
    }

    // === kill-name (the dev-tools allowlist is enforced at dispatch, not here) ===

    [Fact]
    public void RunKillName_NoArgs_EmitsError()
    {
        var (ports, _, render, host) = Setup();
        Assert.Equal(1, ProcessCommands.RunKillName(ports, []));
        Assert.Single(render.Errors);
        Assert.Empty(host.KillCalls);
    }

    [Fact]
    public void RunKillName_KillsAllMatches_ThroughHost()
    {
        var (ports, _, render, host) = Setup();
        host.Table.Add(new ProcessInfo(11, "node", 0));
        host.Table.Add(new ProcessInfo(12, "node", 0));
        host.Table.Add(new ProcessInfo(13, "other", 0));
        Assert.Equal(0, ProcessCommands.RunKillName(ports, ["node"]));
        Assert.Equal(new[] { 11, 12 }, host.KillCalls); // only the matching pids, via the host
        Assert.Contains("Killed 2 'node' processes", render.Infos);
    }

    [Fact]
    public void RunKillName_NoMatch_ReportsNone_WithLowercasedName()
    {
        var (ports, _, render, host) = Setup();
        ProcessCommands.RunKillName(ports, ["GHOST"]);
        Assert.Empty(host.KillCalls);
        Assert.Equal("No processes found matching 'ghost'", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunKillName_KillFailure_WarnsAndExcludesFromCount()
    {
        var (ports, _, render, host) = Setup();
        host.Table.Add(new ProcessInfo(21, "node", 0));
        host.Table.Add(new ProcessInfo(22, "node", 0));
        host.FailKills.Add(22);
        Assert.Equal(0, ProcessCommands.RunKillName(ports, ["node"]));
        Assert.Single(render.Warnings);
        Assert.Contains("Killed 1 'node' processes", render.Infos); // only the success counted
    }

    [Fact]
    public void RunKillName_JsonMode_EmitsKilledAndCount()
    {
        var (ports, _, render, host) = Setup(jsonMode: true);
        host.Table.Add(new ProcessInfo(31, "node", 0));
        ProcessCommands.RunKillName(ports, ["node"]);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal(1, json.GetProperty("count").GetInt32());
        Assert.Equal(31, json.GetProperty("killed")[0].GetProperty("pid").GetInt32());
    }

    // === kill-port (port query via executor, kill via host; OS-branched fixture) ===

    [Fact]
    public void RunKillPort_NoArgs_EmitsError()
    {
        var (ports, _, render, _) = Setup();
        Assert.Equal(1, ProcessCommands.RunKillPort(ports, []));
        Assert.Single(render.Errors);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public void RunKillPort_InvalidPort_EmitsError(string port)
    {
        var (ports, exec, render, host) = Setup();
        Assert.Equal(1, ProcessCommands.RunKillPort(ports, [port]));
        Assert.Equal("Invalid port number", Assert.Single(render.Errors));
        Assert.Empty(exec.Calls);
        Assert.Empty(host.KillCalls);
    }

    [Fact]
    public void RunKillPort_ListeningPid_RoutesQueryThroughExecutor_KillThroughHost()
    {
        var (ports, exec, render, host) = Setup();
        host.Table.Add(new ProcessInfo(1234, "node", 0));
        // netstat (Windows) vs lsof -t (Unix) produce different formats; feed the one this OS parses.
        bool win = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        exec.NextResult = win
            ? new ExecResult(0, "  TCP    0.0.0.0:3000    0.0.0.0:0    LISTENING    1234", "")
            : new ExecResult(0, "1234", "");

        Assert.Equal(0, ProcessCommands.RunKillPort(ports, ["3000"]));

        var call = Assert.Single(exec.Calls); // kill-port queries one tool (no `which` probe)
        Assert.Equal(win ? "netstat" : "lsof", call.Tool);
        Assert.Equal(win ? new[] { "-ano", "-p", "TCP" } : ["-t", "-i:3000"], call.Args);
        Assert.Contains(1234, host.KillCalls);  // killed through the host, not a real Process
    }

    [Fact]
    public void RunKillPort_NoMatchingListener_ReportsNone_KillsNothing()
    {
        var (ports, exec, render, host) = Setup();
        exec.NextResult = new ExecResult(0, "", ""); // empty port table on either OS
        Assert.Equal(0, ProcessCommands.RunKillPort(ports, ["3000"]));
        Assert.Empty(host.KillCalls);
        Assert.Contains("No process listening on port 3000", render.Infos);
    }

    // === ports ===

    [Fact]
    public void RunPorts_JsonMode_QueriesThroughExecutor_EmitsOutputPayload()
    {
        var (ports, exec, render, _) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(0, "PORTS-OUTPUT", "");
        Assert.Equal(0, ProcessCommands.RunPorts(ports, []));
        AssertPortsQueried(exec);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("PORTS-OUTPUT", json.GetProperty("output").GetString());
    }

    [Fact]
    public void RunPorts_HumanMode_EmitsOutputViaInfo()
    {
        var (ports, exec, render, _) = Setup();
        exec.NextResult = new ExecResult(0, "listening lines", "");
        ProcessCommands.RunPorts(ports, []);
        AssertPortsQueried(exec);
        Assert.Contains("listening lines", render.Infos);
        Assert.Empty(render.JsonPayloads);
    }

    // === JSON-mode "nothing matched" paths emit a valid empty result (not bare/suppressed output) ===

    [Fact]
    public void RunKillName_JsonMode_NoMatch_EmitsEmptyKilledAndZeroCount()
    {
        var (ports, _, render, _) = Setup(jsonMode: true);
        ProcessCommands.RunKillName(ports, ["ghost"]);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal(0, json.GetProperty("count").GetInt32());
        Assert.Equal(0, json.GetProperty("killed").GetArrayLength());
    }

    [Fact]
    public void RunKillPort_JsonMode_NoListener_EmitsPortAndEmptyKilled()
    {
        var (ports, exec, render, _) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(0, "", ""); // empty port table on either OS
        Assert.Equal(0, ProcessCommands.RunKillPort(ports, ["3000"]));
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal(3000, json.GetProperty("port").GetInt32());
        Assert.Equal(0, json.GetProperty("killed").GetArrayLength());
    }
}
