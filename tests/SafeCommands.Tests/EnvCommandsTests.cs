using System.Runtime.InteropServices;
using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class EnvCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace()), exec, render);
    }

    // The handler picks "where" on Windows, "which" elsewhere — mirror that so the assertion is cross-platform.
    private static string Which => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";

    private static JsonElement AsJson(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

    // === info ===

    [Fact]
    public void RunInfo_HumanMode_EmitsInfoLines_NoJsonPayload()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(0, EnvCommands.RunInfo(ports, []));
        Assert.NotEmpty(render.Infos);
        Assert.Empty(render.JsonPayloads);
    }

    [Fact]
    public void RunInfo_JsonMode_EmitsSinglePayload_NoInfoLines()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        EnvCommands.RunInfo(ports, []);
        var payload = Assert.Single(render.JsonPayloads);
        Assert.Empty(render.Infos);
        var json = AsJson(payload);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("platform").GetString()));
        Assert.True(json.GetProperty("processorCount").GetInt32() > 0);
    }

    // === path ===

    [Fact]
    public void RunPath_JsonMode_EmitsSinglePayload_NoInfoLines()
    {
        var (ports, _, render) = Setup(jsonMode: true);
        Assert.Equal(0, EnvCommands.RunPath(ports, []));
        Assert.Single(render.JsonPayloads);
        Assert.Empty(render.Infos);
    }

    [Fact]
    public void RunPath_HumanMode_DoesNotEmitJsonPayload()
    {
        var (ports, _, render) = Setup();
        Assert.Equal(0, EnvCommands.RunPath(ports, []));
        Assert.Empty(render.JsonPayloads);
    }

    // === check (routes through the executor) ===

    [Fact]
    public void RunCheck_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = EnvCommands.RunCheck(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunCheck_Available_ProbesThenQueriesVersion_ParsesFirstLine()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(0, "node v20.1.0\ntrailing", "");
        var rc = EnvCommands.RunCheck(ports, ["node"]);

        Assert.Equal(0, rc);
        Assert.Equal(2, exec.Calls.Count);
        Assert.Equal(Which, exec.Calls[0].Tool);
        Assert.Equal(new[] { "node" }, exec.Calls[0].Args);
        Assert.Equal("node", exec.Calls[1].Tool);
        Assert.Equal(new[] { "--version" }, exec.Calls[1].Args);
        Assert.Contains("available", Assert.Single(render.Infos));
        Assert.Contains("node v20.1.0", render.Infos[0]);
    }

    [Fact]
    public void RunCheck_NotAvailable_DoesNotQueryVersion_ReturnsOne()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(1, "", "");
        var rc = EnvCommands.RunCheck(ports, ["ghost-tool"]);

        Assert.Equal(1, rc);
        var probe = Assert.Single(exec.Calls); // no --version probe followed
        Assert.Equal(Which, probe.Tool);
        Assert.Equal(new[] { "ghost-tool" }, probe.Args);
        Assert.Equal("ghost-tool: not found", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunCheck_JsonMode_EmitsPayload_NoInfo()
    {
        var (ports, exec, render) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(0, "v1.2.3", "");
        EnvCommands.RunCheck(ports, ["tool"]);

        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Empty(render.Infos);
        Assert.Equal("tool", json.GetProperty("tool").GetString());
        Assert.True(json.GetProperty("available").GetBoolean());
        Assert.Equal("v1.2.3", json.GetProperty("version").GetString());
    }

    // === which (routes through the executor) ===

    [Fact]
    public void RunWhich_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = EnvCommands.RunWhich(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunWhich_Found_EmitsFirstLinePath()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(0, "/usr/bin/node\n/usr/local/bin/node", "");
        var rc = EnvCommands.RunWhich(ports, ["node"]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal(Which, call.Tool);
        Assert.Equal(new[] { "node" }, call.Args);
        Assert.Equal("/usr/bin/node", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunWhich_NotFound_ReturnsOne()
    {
        var (ports, exec, render) = Setup();
        exec.NextResult = new ExecResult(1, "", "");
        var rc = EnvCommands.RunWhich(ports, ["ghost-tool"]);
        Assert.Equal(1, rc);
        Assert.Equal("ghost-tool: not found", Assert.Single(render.Infos));
    }

    [Fact]
    public void RunWhich_JsonMode_EmitsFoundAndPath()
    {
        var (ports, exec, render) = Setup(jsonMode: true);
        exec.NextResult = new ExecResult(0, "/usr/bin/git\n", "");
        EnvCommands.RunWhich(ports, ["git"]);
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("git", json.GetProperty("tool").GetString());
        Assert.True(json.GetProperty("found").GetBoolean());
        Assert.Equal("/usr/bin/git", json.GetProperty("path").GetString());
    }

    // === vars (secret masking is the security-critical behavior) ===

    [Fact]
    public void RunVars_All_MasksSecretLikeVariable()
    {
        const string key = "SAFECMD_TEST_API_TOKEN"; // contains "TOKEN" -> secret pattern
        Environment.SetEnvironmentVariable(key, "super-secret-value");
        try
        {
            var (ports, _, render) = Setup(jsonMode: true);
            EnvCommands.RunVars(ports, ["--all", key]); // filter narrows to just our var
            var dict = Assert.IsType<Dictionary<string, string>>(Assert.Single(render.JsonPayloads));
            Assert.Equal("***masked***", dict[key]);
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Fact]
    public void RunVars_SafeOnly_ExcludesUnsafeVariableEntirely()
    {
        const string key = "SAFECMD_TEST_RANDOM_SETTING"; // not in the safe-prefix allowlist
        Environment.SetEnvironmentVariable(key, "value");
        try
        {
            var (ports, _, render) = Setup(jsonMode: true);
            EnvCommands.RunVars(ports, [key]); // no --all -> safe-list only
            var dict = Assert.IsType<Dictionary<string, string>>(Assert.Single(render.JsonPayloads));
            Assert.DoesNotContain(key, dict.Keys);
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Fact]
    public void RunVars_All_HumanMode_EmitsMaskingWarning()
    {
        var (ports, _, render) = Setup();
        EnvCommands.RunVars(ports, ["--all", "SAFECMD_NO_SUCH_FILTER_MATCH"]);
        Assert.Contains("masked", Assert.Single(render.Warnings));
    }

    [Fact]
    public void RunVars_All_JsonMode_DoesNotEmitWarning()
    {
        // Under --json the handler takes the JSON branch and never calls Render.Warning, so no warning
        // can reach stdout to corrupt the payload. (ConsoleRenderer also suppresses Warning under
        // JsonMode as a second layer; this FakeRenderer test pins only the handler-branch behavior —
        // empty-valued-secret masking, the other half of the guard, isn't pinned: an empty env var
        // can't be created via Environment.SetEnvironmentVariable, which deletes on empty.)
        var (ports, _, render) = Setup(jsonMode: true);
        EnvCommands.RunVars(ports, ["--all", "SAFECMD_NO_SUCH_FILTER_MATCH"]);
        Assert.Empty(render.Warnings);
    }

    [Fact]
    public void RunVars_SafeOnly_NoMaskingWarning()
    {
        var (ports, _, render) = Setup();
        EnvCommands.RunVars(ports, []);
        Assert.Empty(render.Warnings);
    }

    // === executor-failure propagation (pins the dropped CommandExists try/catch) ===

    private sealed class ThrowingExecutor : IExecutor
    {
        public ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null)
            => throw new InvalidOperationException("spawn failed");
    }

    private static Ports ThrowingPorts()
        => new(new ThrowingExecutor(), new FakeRenderer(), new FakeRepoProbe(), new FakeWorkspace());

    [Fact]
    public void RunCheck_WhenProbeThrows_Propagates_DoesNotSwallowToNotFound()
    {
        // The migration deliberately dropped CommandExists's swallow-to-false: a missing where/which
        // (or any spawn failure) now propagates to the global handler rather than silently reporting
        // available=false. Pin the contract so a revert can't re-introduce the swallow unnoticed.
        Assert.Throws<InvalidOperationException>(() => EnvCommands.RunCheck(ThrowingPorts(), ["node"]));
    }

    [Fact]
    public void RunWhich_WhenProbeThrows_Propagates()
        => Assert.Throws<InvalidOperationException>(() => EnvCommands.RunWhich(ThrowingPorts(), ["node"]));
}
