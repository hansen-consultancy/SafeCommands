using SafeCommands.Registry;
using Spectre.Console;

namespace SafeCommands.Commands;

static class MetaCommands
{
    public static int RunHelp(string[] args, bool json)
    {
        if (args.Length > 0)
            return RunGroupHelp(args[0], json);

        if (json)
        {
            var groups = CommandRegistry.Groups.Select(g => new
            {
                group = g,
                commands = CommandRegistry.FindByGroup(g).Select(c => new
                {
                    name = c.Name,
                    description = c.Description,
                    usage = c.Usage,
                    safety = c.SafetyLabel,
                }).ToArray()
            }).ToArray();

            Infrastructure.OutputFormatter.WriteJson(new { version = GetVersion(), groups });
            return 0;
        }

        AnsiConsole.Write(new FigletText("SafeCommands").Color(Color.Green));
        AnsiConsole.MarkupLine($"[dim]v{GetVersion()} - Safe command gateway for AI agents[/]\n");

        AnsiConsole.MarkupLine("[bold]Usage:[/] safe <group> <command> [[args...]] [[--json]]");
        AnsiConsole.MarkupLine("[bold]       [/] safe help <group>                  [dim]Show group commands[/]");
        AnsiConsole.MarkupLine("[bold]       [/] safe instructions                  [dim]Print CLAUDE.md setup[/]");
        Console.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Group[/]").PadRight(2))
            .AddColumn(new TableColumn("[bold]Commands[/]").PadRight(2))
            .AddColumn("[bold]Description[/]");

        foreach (var group in CommandRegistry.Groups.OrderBy(g => g))
        {
            var cmds = CommandRegistry.FindByGroup(group).ToArray();
            var cmdNames = string.Join(", ", cmds.Take(6).Select(c => c.Name));
            if (cmds.Length > 6) cmdNames += $" (+{cmds.Length - 6} more)";

            var desc = group switch
            {
                "git" => "Git operations with safety checks",
                "file" => "File system operations (read, delete temp/locks)",
                "process" => "Process management (list, kill dev tools)",
                "docker" => "Docker & Compose operations",
                "npm" => "npm/Node.js package management",
                "pnpm" => "pnpm package management (safer: no lifecycle scripts by default)",
                "bun" => "Bun runtime and package management",
                "db" => "Database migrations (Prisma, Drizzle, EF, Laravel, Django)",
                "dotnet" => ".NET CLI operations",
                "env" => "Environment info and tool checks",
                "proxy" => "Proxy to external tools (gh, az, kubectl, etc.)",
                _ => ""
            };

            table.AddRow($"[green]{group}[/]", $"[dim]{cmdNames}[/]", desc);
        }

        AnsiConsole.Write(table);

        Console.WriteLine();
        AnsiConsole.MarkupLine("[dim]Safety levels: [green]read-only[/] | [yellow]safe-write[/] | [red]checked-write[/][/]");
        AnsiConsole.MarkupLine("[dim]All commands support [bold]--json[/] for machine-readable output[/]");

        return 0;
    }

    private static int RunGroupHelp(string group, bool json)
    {
        var commands = CommandRegistry.FindByGroup(group).ToArray();
        if (commands.Length == 0)
        {
            Infrastructure.OutputFormatter.WriteError($"Unknown group: {group}");
            Console.WriteLine($"Available groups: {string.Join(", ", CommandRegistry.Groups.OrderBy(g => g))}");
            return 1;
        }

        if (json)
        {
            Infrastructure.OutputFormatter.WriteJson(new
            {
                group,
                commands = commands.Select(c => new { c.Name, c.Description, c.Usage, safety = c.SafetyLabel }).ToArray()
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"\n[bold green]{group}[/] commands:\n");

        var table = new Table()
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("[bold]Command[/]").PadRight(2))
            .AddColumn(new TableColumn("[bold]Safety[/]").PadRight(2))
            .AddColumn("[bold]Description[/]");

        foreach (var cmd in commands)
        {
            var safetyColor = cmd.Safety switch
            {
                SafetyLevel.ReadOnly => "green",
                SafetyLevel.SafeWrite => "yellow",
                SafetyLevel.TargetedWrite => "red",
                _ => "white"
            };

            table.AddRow(
                $"[white]safe {cmd.Group} {cmd.Name}[/]",
                $"[{safetyColor}]{cmd.SafetyLabel}[/]",
                cmd.Description);
        }

        AnsiConsole.Write(table);

        Console.WriteLine();
        AnsiConsole.MarkupLine("[dim]Usage examples:[/]");
        foreach (var cmd in commands.Take(3))
            AnsiConsole.MarkupLine($"  [dim]$[/] {cmd.Usage.EscapeMarkup()}");

        return 0;
    }

    public static int RunVersion(string[] args, bool json)
    {
        var version = GetVersion();
        if (json)
            Infrastructure.OutputFormatter.WriteJson(new { version, tool = "SafeCommands", command = "safe" });
        else
            Console.WriteLine($"SafeCommands v{version}");
        return 0;
    }

    public static int RunInstructions(string[] args, bool json)
    {
        var instructions = GetInstructionsContent();

        if (args.Contains("--install"))
        {
            return InstallInstructions(instructions);
        }

        Console.WriteLine(instructions);
        return 0;
    }

    private static int InstallInstructions(string instructions)
    {
        var claudeMdPath = Path.Combine(Directory.GetCurrentDirectory(), "CLAUDE.md");
        var marker = "<!-- SafeCommands -->";

        if (File.Exists(claudeMdPath))
        {
            var existing = File.ReadAllText(claudeMdPath);
            if (existing.Contains(marker))
            {
                Console.WriteLine("SafeCommands instructions already present in CLAUDE.md");
                return 0;
            }

            File.AppendAllText(claudeMdPath, $"\n\n{marker}\n{instructions}\n");
        }
        else
        {
            File.WriteAllText(claudeMdPath, $"{marker}\n{instructions}\n");
        }

        Infrastructure.OutputFormatter.WriteSuccess($"Instructions appended to {claudeMdPath}");
        return 0;
    }

    private static string GetInstructionsContent()
    {
        var commands = CommandRegistry.Commands;
        var groups = CommandRegistry.Groups.OrderBy(g => g).ToArray();

        var allowlistRules = string.Join(",\n      ",
            new[] { "help", "version", "instructions" }.Concat(groups)
                .Select(g => $"\"Bash(safe {g}:*)\""));

        return $$"""
            ## SafeCommands (`safe`)

            This project uses **SafeCommands** - a safe command gateway for AI agents.

            ### Claude Code Allowlist

            Add to `.claude/settings.local.json` to allow all `safe` commands without approval:

            ```json
            {
              "permissions": {
                "allow": [
                  {{allowlistRules}}
                ]
              }
            }
            ```

            ### Quick Reference

            | Group | Example | Description |
            |-------|---------|-------------|
            | git | `safe git status` | Git with safety checks (no force push, no hard reset) |
            | file | `safe file delete-temp` | File ops (read, delete build artifacts/locks) |
            | process | `safe process kill-port 3000` | Process management (dev tools only) |
            | docker | `safe docker compose-up -d` | Docker & Compose operations |
            | npm | `safe npm test` | npm/Node.js (allowed scripts only) |
            | dotnet | `safe dotnet build` | .NET CLI operations |
            | db | `safe db prisma-status` | Database migrations (blocks --force flags) |
            | env | `safe env check node` | Environment info and tool checks |
            | proxy | `safe proxy gh pr list` | Proxy to gh, az, kubectl, terraform, etc. |

            ### Safety Guarantees

            All commands through `safe` are pre-validated:
            - No destructive git operations (no `--force`, no `reset --hard`, no `checkout .`)
            - No deletion of untracked/uncommitted files (except temp/build directories)
            - All file operations sandboxed to project directory
            - Database migration `--force`/`--force-reset`/`--accept-data-loss` blocked
            - Process kills limited to dev tooling (node, dotnet, python, etc.)
            - Docker compose down without `-v` (protects volumes)
            - npm scripts limited to known safe scripts (build, test, lint, etc.)
            - curl limited to GET/HEAD requests only

            ### Flags

            - `--json` on any command for machine-readable JSON output
            - `safe help <group>` for detailed command list per group
            - `safe help` for full overview

            ### Total commands: {{commands.Count}} across {{groups.Length}} groups
            """;
    }

    private static string GetVersion()
    {
        var assembly = typeof(MetaCommands).Assembly;
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.1.0";
    }
}
