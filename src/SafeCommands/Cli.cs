using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;

namespace SafeCommands;

/// <summary>
/// The CLI outer shell — <c>--json</c> parsing and command routing — extracted from
/// <c>Program.cs</c>'s top-level statements so the fragile bits (the proxy-aware <c>--json</c> splice
/// and the group/command routing) are table-testable. <c>Program.cs</c> wires the real ports and
/// delegates here; tests drive these directly with a <c>FakeRenderer</c>.
/// </summary>
static class Cli
{
    /// <summary>
    /// Splits the global <c>--json</c> flag out of the arg vector. For proxy commands, only a
    /// <c>--json</c> appearing BEFORE the <c>proxy</c> token is consumed as ours; any <c>--json</c>
    /// after it belongs to the proxied tool (e.g. <c>gh ... --json fields</c>) and is left in place.
    /// </summary>
    public static (bool jsonOutput, string[] args) StripJson(string[] cliArgs)
    {
        var proxyIdx = Array.FindIndex(cliArgs, a => a.Equals("proxy", StringComparison.OrdinalIgnoreCase));
        if (proxyIdx >= 0)
        {
            var json = false;
            for (var i = 0; i < proxyIdx; i++)
                if (cliArgs[i] == "--json") { json = true; break; }
            // Strip --json only from the pre-proxy segment; everything from "proxy" on is passed through.
            var kept = cliArgs.Take(proxyIdx).Where(a => a != "--json").Concat(cliArgs.Skip(proxyIdx)).ToArray();
            return (json, kept);
        }

        return (cliArgs.Contains("--json"), cliArgs.Where(a => a != "--json").ToArray());
    }

    /// <summary>
    /// Routes a (json-stripped) arg vector to the right place: meta commands, group/command lookup
    /// with friendly unknown-group/unknown-command errors, per-command <c>--help</c>, and the guarded
    /// handler dispatch (see <see cref="CommandDispatcher"/>). Returns the process exit code.
    /// </summary>
    public static int Route(Ports ports, string[] cliArgs, bool jsonOutput)
    {
        if (cliArgs.Length == 0)
            return MetaCommands.RunHelp([], jsonOutput);

        var first = cliArgs[0].ToLowerInvariant();
        switch (first)
        {
            case "help" or "-h" or "--help" or "h":
                return MetaCommands.RunHelp(cliArgs.Skip(1).ToArray(), jsonOutput);
            case "version" or "-v" or "--version":
                return MetaCommands.RunVersion([], jsonOutput);
            case "instructions" or "setup":
                return MetaCommands.RunInstructions(cliArgs.Skip(1).ToArray(), jsonOutput);
        }

        // safe <group> <command> [args...]
        if (cliArgs.Length < 2)
        {
            // A bare group name shows that group's help.
            if (CommandRegistry.FindByGroup(first).Any())
                return MetaCommands.RunHelp([first], jsonOutput);

            ports.Render.Error($"Unknown command: {first}");
            ports.Render.Info("Run 'safe help' for available commands.");
            return 1;
        }

        var group = first;
        var command = cliArgs[1].ToLowerInvariant();
        var commandArgs = cliArgs.Skip(2).ToArray();

        var cmd = CommandRegistry.Find(group, command);
        if (cmd == null)
        {
            if (!CommandRegistry.FindByGroup(group).Any())
            {
                ports.Render.Error($"Unknown group: {group}");
                ports.Render.Info($"Available groups: {string.Join(", ", CommandRegistry.Groups.OrderBy(g => g))}");
                ports.Render.Info("Run 'safe help' for details.");
            }
            else
            {
                ports.Render.Error($"Unknown command: {group} {command}");
                ports.Render.Info($"Available {group} commands:");
                foreach (var c in CommandRegistry.FindByGroup(group))
                    ports.Render.Info($"  safe {c.Group} {c.Name,-20} {c.Description}");
            }
            return 1;
        }

        // Per-command help: `safe <group> <command> --help|-h` prints usage without invoking the
        // handler (so `--help` isn't mistaken for a positional argument).
        if (commandArgs.Any(a => a is "--help" or "-h"))
        {
            if (jsonOutput)
                ports.Render.Json(new
                {
                    command = cmd.FullName,
                    description = cmd.Description,
                    usage = cmd.Usage,
                    safety = cmd.SafetyLabel,
                });
            else
            {
                ports.Render.Info($"safe {cmd.FullName} — {cmd.Description}");
                ports.Render.Info($"Usage: {cmd.Usage}");
                ports.Render.Info($"Safety: {cmd.SafetyLabel}");
            }
            return 0;
        }

        try
        {
            return CommandDispatcher.Execute(cmd, ports, group, command, commandArgs);
        }
        catch (Exception ex)
        {
            ports.Render.Error($"Command failed: {ex.Message}");
            if (jsonOutput)
                ports.Render.Json(new { error = true, message = ex.Message, command = cmd.FullName });
            return 1;
        }
    }
}
