using SafeCommands.Commands;
using SafeCommands.Infrastructure;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;

// Initialize the command registry
CommandRegistry.Initialize();

// Parse arguments
var cliArgs = args;

if (cliArgs.Length == 0)
{
    return MetaCommands.RunHelp([], false);
}

// Check for --json flag and remove it from args.
// For proxy commands, only strip --json from before "proxy" to avoid
// removing --json flags meant for the proxied tool (e.g., gh --json fields).
var jsonOutput = false;
var proxyIdx = Array.FindIndex(cliArgs, a => a.Equals("proxy", StringComparison.OrdinalIgnoreCase));

if (proxyIdx >= 0)
{
    for (var i = 0; i < proxyIdx; i++)
    {
        if (cliArgs[i] == "--json") { jsonOutput = true; break; }
    }
    cliArgs = cliArgs.Take(proxyIdx).Where(a => a != "--json")
        .Concat(cliArgs.Skip(proxyIdx)).ToArray();
}
else
{
    jsonOutput = cliArgs.Contains("--json");
    cliArgs = cliArgs.Where(a => a != "--json").ToArray();
}

// Wire infrastructure ports once. From here every handler receives the same Ports record.
var exec = new ProcessExecutor();
var ports = new Ports(exec, new ConsoleRenderer(jsonOutput), new GitRepoProbe(exec), new FileSystemWorkspace());

// Handle meta commands (still on legacy signature — not migrated in PR #1)
var first = cliArgs[0].ToLowerInvariant();

switch (first)
{
    case "help" or "-h" or "--help" or "h":
        return MetaCommands.RunHelp(cliArgs.Skip(1).ToArray(), jsonOutput);

    case "version" or "-v" or "--version":
        return MetaCommands.RunVersion([], jsonOutput);

    case "instructions" or "setup":
        return MetaCommands.RunInstructions(cliArgs.Skip(1).ToArray(), jsonOutput);

    case "proxy" when cliArgs.Length > 1:
        // Handle "safe proxy <tool> <args...>"
        var proxyCmd = CommandRegistry.Find("proxy", "run");
        if (proxyCmd != null)
            return proxyCmd.Handler(ports, cliArgs.Skip(1).ToArray());
        break;
}

// Handle group commands: safe <group> <command> [args...]
if (cliArgs.Length < 2)
{
    // Single arg that's a group name -> show group help
    if (CommandRegistry.FindByGroup(first).Any())
        return MetaCommands.RunHelp([first], jsonOutput);

    ports.Render.Error($"Unknown command: {first}");
    ports.Render.Info("Run 'safe help' for available commands.");
    return 1;
}

var group = first;
var command = cliArgs[1].ToLowerInvariant();
var commandArgs = cliArgs.Skip(2).ToArray();

// Look up the command
var cmd = CommandRegistry.Find(group, command);

if (cmd == null)
{
    // Check if the group exists at all
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

// Per-command help: `safe <group> <command> --help|-h` prints usage without
// invoking the handler (so `--help` isn't mistaken for a positional argument).
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

// Execute the command
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
