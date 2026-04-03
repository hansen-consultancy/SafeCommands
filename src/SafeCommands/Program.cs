using SafeCommands.Commands;
using SafeCommands.Infrastructure;
using SafeCommands.Registry;

// Initialize the command registry
CommandRegistry.Initialize();

// Parse arguments
var cliArgs = args;

if (cliArgs.Length == 0)
{
    return MetaCommands.RunHelp([], false);
}

// Check for --json flag and remove it from args
var jsonOutput = cliArgs.Contains("--json");
cliArgs = cliArgs.Where(a => a != "--json").ToArray();

// Handle meta commands
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
            return proxyCmd.Handler(cliArgs.Skip(1).ToArray(), jsonOutput);
        break;
}

// Handle group commands: safe <group> <command> [args...]
if (cliArgs.Length < 2)
{
    // Single arg that's a group name -> show group help
    if (CommandRegistry.FindByGroup(first).Any())
        return MetaCommands.RunHelp([first], jsonOutput);

    OutputFormatter.WriteError($"Unknown command: {first}");
    Console.WriteLine("Run 'safe help' for available commands.");
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
        OutputFormatter.WriteError($"Unknown group: {group}");
        Console.WriteLine($"Available groups: {string.Join(", ", CommandRegistry.Groups.OrderBy(g => g))}");
        Console.WriteLine("Run 'safe help' for details.");
    }
    else
    {
        OutputFormatter.WriteError($"Unknown command: {group} {command}");
        Console.WriteLine($"Available {group} commands:");
        foreach (var c in CommandRegistry.FindByGroup(group))
            Console.WriteLine($"  safe {c.Group} {c.Name,-20} {c.Description}");
    }
    return 1;
}

// Execute the command
try
{
    return cmd.Handler(commandArgs, jsonOutput);
}
catch (Exception ex)
{
    OutputFormatter.WriteError($"Command failed: {ex.Message}");
    if (jsonOutput)
        OutputFormatter.WriteJson(new { error = true, message = ex.Message, command = cmd.FullName });
    return 1;
}
