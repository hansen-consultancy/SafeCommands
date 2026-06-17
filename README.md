# SafeCommands

![SafeCommands Logo](https://raw.githubusercontent.com/hansen-consultancy/SafeCommands/main/logo.png)

[![NuGet version](https://img.shields.io/nuget/v/HC.SafeCommands.svg)](https://www.nuget.org/packages/HC.SafeCommands/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://opensource.org/licenses/MIT)

A safe command gateway CLI for AI coding agents. Provides **161 pre-validated commands** across 12 groups so agents can be allowlisted on `safe` without per-command approval overhead.

## Why?

AI coding agents (Claude Code, Cursor, Gemini CLI, Codex, etc.) need to run CLI commands as part of their workflow, but many commands have dangerous modes:

- `git checkout .` silently destroys all uncommitted work
- `rm -rf` can expand beyond its target (deleted a user's entire home directory)
- `prisma db push --force-reset` dropped 60+ production database tables
- `terraform destroy` wiped 2.5 years of production data
- `npm install` executes arbitrary postinstall scripts (supply chain attacks)

These aren't theoretical risks. Research across 70+ documented incidents from Claude Code, Gemini CLI, Cursor, Codex, and Replit GitHub issues showed these exact commands causing real data loss.

**SafeCommands** solves this by providing a curated command surface where every operation is pre-validated for safety. Allowlist `safe` once, and all commands through it are guaranteed safe by design.

## Installation

```bash
dotnet tool install --global HC.SafeCommands
```

To update:

```bash
dotnet tool update --global HC.SafeCommands
```

## Quick Start

```bash
safe help                    # Show all command groups
safe git status              # Git status (read-only)
safe git add myfile.cs       # Stage specific file (blocks 'git add .')
safe git commit -m "msg"     # Commit (blocks --no-verify, --amend)
safe dotnet build             # Build project
safe dotnet test              # Run tests
safe file delete-temp         # Clean bin/obj/node_modules/etc.
safe process kill-port 3000   # Kill process on port
safe generate uuid             # Generate a v4 UUID
safe generate secret           # Crypto-random base64 secret
safe help git                 # Show all git commands with safety levels
```

## Command Groups

| Group | Commands | Description |
|-------|----------|-------------|
| **git** | 27 | Git with safety checks (no force push, no hard reset, clean tree required to switch branches — `checkout -b` exempt) |
| **file** | 15 | File operations (read, count, delete only tracked files or temp/build dirs) |
| **process** | 5 | Process management (kill limited to dev tooling only) |
| **docker** | 18 | Docker & Compose (no volume removal, no system prune) |
| **npm** | 12 | npm with script allowlist, install warnings for supply chain risk |
| **pnpm** | 10 | pnpm (safer default: lifecycle scripts disabled) |
| **bun** | 6 | Bun runtime operations |
| **dotnet** | 18 | .NET CLI (build, test, restore, publish, format, etc.) |
| **db** | 22 | Database migrations - Prisma, Drizzle, EF Core, Laravel, Django (blocks all --force flags) |
| **env** | 5 | Environment info, tool checks, PATH (secrets masked) |
| **generate** | 14 | UUIDs, secrets, passwords, hashes, timestamps, nanoids, encoding, JWT decode, slugs |
| **proxy** | 9 | Proxy to gh, az, kubectl, terraform, curl, cargo, pip, make |

## Safety Levels

Every command has a safety classification:

| Level | Color | Meaning |
|-------|-------|---------|
| **read-only** | Green | No side effects at all |
| **safe-write** | Yellow | Additive or easily recoverable writes |
| **checked-write** | Red | Write with pre-validation (e.g., requires clean working tree) |

## What's Blocked

Based on real-world incident research, SafeCommands explicitly prevents:

| Blocked Command | Reason | Safe Alternative |
|-----------------|--------|-----------------|
| `git reset --hard` | #1 cause of data loss in AI agents | Not available |
| `git checkout .` | Destroys all uncommitted changes | `safe git checkout-file <specific-file>` |
| `git push --force` | Rewrites remote history | `safe git push --force-with-lease` |
| `git add .` / `git add -A` | May stage secrets or unwanted files | `safe git add <file>` or `safe git add-tracked` |
| `git commit --no-verify` | Bypasses safety hooks | Fix the hook issue instead |
| `git commit --amend` (if pushed) | Creates diverged history | `safe git commit-amend` (blocks if pushed) |
| `git branch -D` | Deletes potentially only copy | Not available |
| `rm -rf` (arbitrary) | Can expand beyond target | `safe file delete-temp` / `safe file delete-tracked` |
| `terraform destroy` / `apply` | Wiped production infrastructure | `safe proxy terraform plan` (read-only) |
| `prisma db push --force-reset` | Drops all tables | `safe db prisma-migrate-dev` (no force flags) |
| `drizzle-kit push --force` | Drops all tables | `safe db drizzle-migrate` (no force flags) |
| `npm audit fix --force` | Breaking major version changes | `safe npm audit-fix` (semver-safe only) |
| `docker compose down -v` | Destroys data volumes | `safe docker compose-down` (no -v) |

## JSON Output

All commands support `--json` for machine-readable output, designed for AI agent consumption:

```bash
safe git status --json
```

```json
{
  "branch": "main",
  "clean": true,
  "files": []
}
```

```bash
safe env check node --json
```

```json
{
  "tool": "node",
  "available": true,
  "version": "v22.12.0"
}
```

## AI Agent Integration

### Setup

Run `safe instructions` to print CLAUDE.md-ready integration text, or auto-install it:

```bash
safe instructions --install    # Appends to your project's CLAUDE.md
```

### Claude Code

Add to your project's `CLAUDE.md`:

```markdown
## SafeCommands
Use `safe` for all CLI operations. Run `safe help` for available commands.
```

Then allowlist all groups in `.claude/settings.local.json`:

```json
{
  "permissions": {
    "allow": [
      "Bash(safe help:*)",
      "Bash(safe version:*)",
      "Bash(safe instructions:*)",
      "Bash(safe git:*)",
      "Bash(safe file:*)",
      "Bash(safe process:*)",
      "Bash(safe docker:*)",
      "Bash(safe npm:*)",
      "Bash(safe pnpm:*)",
      "Bash(safe bun:*)",
      "Bash(safe dotnet:*)",
      "Bash(safe db:*)",
      "Bash(safe env:*)",
      "Bash(safe generate:*)",
      "Bash(safe proxy:*)"
    ]
  }
}
```

Or run `safe instructions` which outputs a ready-to-use allowlist that stays in sync with the installed version.

### Other Agents

For any agent that supports command allowlisting, allowlist the `safe` command groups and include the output of `safe instructions` in the agent's context.

## Proxy System

The proxy system allows safe access to tools not in the main groups:

```bash
safe proxy gh pr list              # GitHub CLI (read-only operations)
safe proxy az account show         # Azure CLI (read-only)
safe proxy kubectl get pods        # Kubernetes (read-only)
safe proxy terraform plan          # Terraform (plan only, no apply/destroy)
safe proxy curl https://example.com  # HTTP GET only (no POST/PUT/DELETE)
safe proxy cargo build             # Rust cargo
safe proxy pip list                # Python pip
safe proxy make                    # Make targets
```

Each proxy tool has its own allowlist of permitted subcommands and flags.

## Configuration

### Built-in Allowlist (Immutable)

The core command allowlist is compiled into the binary. It cannot be modified by prompt injection, config file manipulation, or any external input.

### Extension Config (`~/.safecommands/config.json`)

Optional extension configuration for adding custom safe commands, additional npm script names, process names, or safe directories:

```json
{
  "version": 1,
  "allowedScripts": ["storybook", "e2e"],
  "allowedProcessNames": ["ruby"],
  "safeDirs": [".angular/cache"]
}
```

## For Developers

### Building from Source

```bash
git clone https://github.com/hansen-consultancy/SafeCommands.git
cd SafeCommands
dotnet build src/SafeCommands
dotnet run --project src/SafeCommands -- help
```

### Installing Locally

```bash
cd src/SafeCommands
dotnet pack
dotnet tool install --global --add-source ./nupkg HC.SafeCommands
```

### Architecture

- **Registry-based**: All commands defined in `CommandRegistry` with metadata (group, name, safety level, handler)
- **No shell interpretation**: Uses `ProcessStartInfo.ArgumentList` to prevent injection
- **Cross-platform**: Windows, macOS, Linux via `RuntimeInformation.IsOSPlatform()`
- **SpectreConsole**: Rich terminal output with color-coded safety levels

### Adding Commands

Each command group is a static class in `Commands/` that registers its commands:

```csharp
static class MyCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new("mygroup", "mycommand", "Description",
            "safe mygroup mycommand [args]", SafetyLevel.ReadOnly,
            (args, json) => { /* handler */ return 0; }));
    }
}
```

Then register it in `CommandRegistry.Initialize()`.

## License

This project is licensed under the MIT License.
