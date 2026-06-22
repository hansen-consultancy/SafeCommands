using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// MetaCommands migrated onto (Ports, string[]): the JSON branches now route through IRenderer and
/// are testable via FakeRenderer (they used the now-deleted OutputFormatter before). Human-mode help
/// renders Spectre tables/figlet to the console and is not asserted here — CliTests cover its exit code.
/// </summary>
public class MetaCommandsTests
{
    private static (Ports ports, FakeRenderer render) Setup(bool jsonMode = false)
    {
        CommandRegistry.Initialize();
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(new FakeExecutor(), render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), render);
    }

    private static JsonElement AsJson(object payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

    [Fact]
    public void RunHelp_JsonMode_EmitsVersionAndGroups()
    {
        var (ports, render) = Setup(jsonMode: true);
        Assert.Equal(0, MetaCommands.RunHelp(ports, []));
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        var groups = json.GetProperty("groups");
        Assert.True(groups.GetArrayLength() > 0);
        Assert.Contains(groups.EnumerateArray(), g => g.GetProperty("group").GetString() == "git");
    }

    [Fact]
    public void RunHelp_GroupJsonMode_EmitsGroupCommands()
    {
        var (ports, render) = Setup(jsonMode: true);
        Assert.Equal(0, MetaCommands.RunHelp(ports, ["git"]));
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("git", json.GetProperty("group").GetString());
        Assert.True(json.GetProperty("commands").GetArrayLength() > 0);
    }

    [Fact]
    public void RunHelp_UnknownGroup_EmitsErrorAndAvailableGroups_Returns1()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, MetaCommands.RunHelp(ports, ["nosuchgroup"]));
        Assert.Contains("Unknown group: nosuchgroup", render.Errors);
        Assert.Contains(render.Infos, i => i.StartsWith("Available groups:"));
        Assert.Empty(render.JsonPayloads);
    }

    [Fact]
    public void RunVersion_JsonMode_EmitsToolPayload()
    {
        var (ports, render) = Setup(jsonMode: true);
        Assert.Equal(0, MetaCommands.RunVersion(ports, []));
        var json = AsJson(Assert.Single(render.JsonPayloads));
        Assert.Equal("SafeCommands", json.GetProperty("tool").GetString());
        Assert.Equal("safe", json.GetProperty("command").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
    }

    [Fact]
    public void RunVersion_HumanMode_EmitsVersionLine_NoJson()
    {
        var (ports, render) = Setup();
        Assert.Equal(0, MetaCommands.RunVersion(ports, []));
        Assert.Contains(render.Infos, i => i.StartsWith("SafeCommands v"));
        Assert.Empty(render.JsonPayloads);
    }

    [Fact]
    public void RunInstructions_RoutesMarkdownThroughRaw_NotInfoOrJson()
    {
        // Instructions are markdown (no JSON form): the handler must route them through Raw, not Info
        // (which would suppress under --json) or Json. Run under jsonMode to make that distinction
        // matter. (That Raw itself isn't suppressed under JsonMode is the adapter's contract, pinned by
        // RendererEnvelopeTests.Raw_UnderJsonMode_StillWritesVerbatim_NotSuppressed — the fake can't show it.)
        var (ports, render) = Setup(jsonMode: true);
        Assert.Equal(0, MetaCommands.RunInstructions(ports, []));
        Assert.Contains("## SafeCommands", Assert.Single(render.Raws));
        Assert.Empty(render.Infos);
        Assert.Empty(render.JsonPayloads);
    }
}
