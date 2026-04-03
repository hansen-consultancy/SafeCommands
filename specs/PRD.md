# SafeCommands - Product Requirements Document

## Overview

**SafeCommands** is a .NET CLI tool that acts as a **safe command gateway** for AI coding agents. It provides a curated allowlist of safe CLI operations that agents can execute without requiring interactive user approval, eliminating API/AI overhead while preventing destructive actions.

**CLI Name**: `safe` (installed as dotnet global tool)
**Package**: `HC.SafeCommands`

## Problem Statement

AI coding agents (Claude Code, Cursor, GitHub Copilot, etc.) need to execute CLI commands as part of their workflow. However:

1. **Dangerous commands exist alongside safe ones** - `git status` is harmless, but `git checkout .` can destroy uncommitted work
2. **Blanket allowlisting is risky** - Allowing `git:*` permits both safe reads and destructive writes
3. **Per-command approval is slow** - Requiring human approval for every `git status` or `dotnet build` creates friction
4. **AI-based safety checks add overhead** - Using the AI model to evaluate command safety wastes tokens and adds latency

## Solution

SafeCommands provides a **pre-validated command surface** that agents can call freely. By allowlisting `safe:*` in an agent's configuration, all commands routed through SafeCommands are guaranteed safe by design.

### Design Principles

1. **Safe by default** - Every command in the built-in allowlist has been vetted for safety
2. **No destructive side effects** - Commands either read state, or write in recoverable ways
3. **AI-first** - Designed to be called by AI agents, with machine-readable output options
4. **Self-documenting** - `safe instructions` prints CLAUDE.md-ready content for agent self-setup
5. **Extensible but secure** - Custom commands via `~/.safecommands/config.json` with trust verification
6. **Cross-platform** - Works on Windows, macOS, and Linux

## Architecture

### Command Resolution

```
safe <group> <command> [args...]
  |
  v
CommandRegistry (built-in allowlist)
  |
  ├── Direct match → Execute via ProcessRunner
  ├── Proxy match  → Validate args → Forward to real CLI
  └── No match     → Reject with help
```

### Components

1. **CommandRegistry** - Immutable built-in command definitions
2. **SafetyValidator** - Validates arguments against safety rules per command
3. **ProcessRunner** - Cross-platform process execution
4. **ConfigManager** - Handles ~/.safecommands/ extension config with trust verification
5. **OutputFormatter** - Supports human-readable and machine-readable (JSON) output

### Project Structure

```
SafeCommands/
├── specs/
│   └── PRD.md
├── src/
│   └── SafeCommands/
│       ├── SafeCommands.csproj
│       ├── Program.cs
│       ├── Infrastructure/
│       │   ├── ProcessRunner.cs
│       │   └── OutputFormatter.cs
│       ├── Registry/
│       │   ├── CommandRegistry.cs
│       │   ├── CommandDefinition.cs
│       │   └── SafetyRule.cs
│       ├── Commands/
│       │   ├── GitCommands.cs
│       │   ├── FileCommands.cs
│       │   ├── ProcessCommands.cs
│       │   ├── DockerCommands.cs
│       │   ├── NpmCommands.cs
│       │   ├── DotnetCommands.cs
│       │   ├── EnvCommands.cs
│       │   ├── ProxyCommand.cs
│       │   └── MetaCommands.cs
│       └── Configuration/
│           ├── SafeConfig.cs
│           └── TrustManager.cs
├── CLAUDE.md
└── .gitignore
```

## Command Reference

### Git Commands (`safe git <command>`)

Commands that proxy to `git` with safety validation:

| Command | Description | Safety Notes |
|---------|-------------|--------------|
| `safe git status` | Show working tree status | Read-only |
| `safe git log [args]` | Show commit log | Read-only. Allows: `-n`, `--oneline`, `--graph`, `--format`, `--author`, `--since`, `--until`, `--all`, `--stat`, file paths |
| `safe git diff [args]` | Show changes | Read-only. Allows: `--staged`, `--cached`, `--name-only`, `--stat`, file paths, commit refs |
| `safe git show <ref>` | Show commit/object | Read-only |
| `safe git branch` | List branches | Read-only (no -D, no --force) |
| `safe git branch create <name>` | Create a new branch | Safe write - creates only |
| `safe git tag` | List tags | Read-only |
| `safe git remote` | List remotes | Read-only |
| `safe git remote show <name>` | Show remote details | Read-only |
| `safe git stash` | Stash current changes | Safe - preserves work |
| `safe git stash list` | List stashes | Read-only |
| `safe git stash pop` | Apply and remove top stash | Recoverable |
| `safe git stash apply [ref]` | Apply stash without removing | Safe - keeps stash |
| `safe git add <files...>` | Stage files | Safe - no `git add -A` or `.` allowed without `--tracked` flag |
| `safe git add-tracked` | Stage all tracked modified files | Safe - only already-tracked files |
| `safe git commit -m <message>` | Commit staged changes | Safe - requires message |
| `safe git fetch [remote]` | Fetch from remote | Read-only network op |
| `safe git pull [remote] [branch]` | Pull changes | Safe if working tree is clean (validated) |
| `safe git push [remote] [branch]` | Push to remote | **No --force allowed** |
| `safe git checkout <branch>` | Switch branch | **Only if working tree is clean** (validated) |
| `safe git checkout-file <file>` | Restore a single file from HEAD | Targeted discard, requires explicit file |
| `safe git merge <branch>` | Merge branch | **Only if working tree is clean** |
| `safe git cherry-pick <hash>` | Cherry-pick a commit | Single commit only |
| `safe git blame <file>` | Show file blame | Read-only |
| `safe git rev-parse <ref>` | Resolve git references | Read-only |
| `safe git ls-files [args]` | List tracked files | Read-only |

**Blocked git operations**: `reset --hard`, `clean -f`, `push --force`, `checkout .`, `branch -D`, `rebase` (interactive), `reflog expire`, `gc --prune`

### File Commands (`safe file <command>`)

Safe file system operations:

| Command | Description | Safety Notes |
|---------|-------------|--------------|
| `safe file list <path>` | List directory contents | Read-only |
| `safe file read <path>` | Read file content | Read-only |
| `safe file exists <path>` | Check if file/dir exists | Read-only |
| `safe file info <path>` | Show file metadata (size, dates) | Read-only |
| `safe file find <pattern> [--in dir]` | Find files by glob pattern | Read-only |
| `safe file delete-tracked <file>` | Delete git-tracked file with no unstaged changes | Recoverable via git |
| `safe file delete-temp` | Delete temp/cache files (bin, obj, .tmp, __pycache__) | Build artifacts only |
| `safe file delete-locks` | Delete lock files (.git/index.lock, etc.) | Common recovery action |
| `safe file delete-pattern <glob> --in <dir>` | Delete matching files in specified dir | Dir must be in safe list (temp, build output, etc.) |
| `safe file mkdir <path>` | Create directory | Safe - additive |
| `safe file copy <src> <dest>` | Copy file | Safe - additive (no overwrite by default) |
| `safe file move <src> <dest>` | Move/rename file | Only if src is git-tracked |
| `safe file write <path> --content <text>` | Write content to new file | **Only if file doesn't exist** (no overwrite) |

**Safe directory list** (for delete-pattern): `bin/`, `obj/`, `node_modules/`, `.tmp/`, `__pycache__/`, `.cache/`, `dist/`, `build/`, `out/`, `.next/`, `.nuget/`, `TestResults/`, `coverage/`, `.pytest_cache/`

### Process Commands (`safe process <command>`)

| Command | Description | Safety Notes |
|---------|-------------|--------------|
| `safe process list` | List running processes | Read-only |
| `safe process find <name>` | Find process by name | Read-only |
| `safe process ports` | Show listening ports | Read-only |
| `safe process kill-port <port>` | Kill process on specific port | Common dev need, targeted |
| `safe process kill-name <name>` | Kill by name | **Only from allowed list**: node, dotnet, python, java, webpack, vite, esbuild, tsc |

### Docker Commands (`safe docker <command>`)

| Command | Description | Safety Notes |
|---------|-------------|--------------|
| `safe docker ps` | List containers | Read-only |
| `safe docker images` | List images | Read-only |
| `safe docker logs <container>` | View container logs | Read-only. Allows: `--tail`, `--since`, `-f` |
| `safe docker inspect <container>` | Inspect container | Read-only |
| `safe docker stop <container>` | Stop a running container | Graceful stop |
| `safe docker start <container>` | Start a stopped container | Safe restart |
| `safe docker restart <container>` | Restart a container | Safe |
| `safe docker build [args]` | Build image | Safe - local only. Allows: `-t`, `-f`, `--target`, `--build-arg` |
| `safe docker compose up [-d]` | Start compose services | Allows: `-d`, `--build`, `--no-deps`, service names |
| `safe docker compose down` | Stop compose services | **No `-v` (protects volumes)** |
| `safe docker compose ps` | List compose services | Read-only |
| `safe docker compose logs [service]` | View compose logs | Read-only |
| `safe docker compose restart [service]` | Restart compose service | Safe |
| `safe docker compose build [service]` | Build compose services | Safe - local only |

**Blocked docker operations**: `rm -f`, `system prune`, `volume rm`, `network rm`, `compose down -v`

### npm/Node Commands (`safe npm <command>`)

| Command | Description | Safety Notes |
|---------|-------------|--------------|
| `safe npm install [package]` | Install dependencies | Safe - additive |
| `safe npm ci` | Clean install from lockfile | Safe - deterministic |
| `safe npm run <script>` | Run package script | **Only from allowed list**: build, dev, start, test, lint, format, typecheck, check, compile, watch, serve, preview |
| `safe npm test` | Run tests | Alias for `npm run test` |
| `safe npm build` | Build project | Alias for `npm run build` |
| `safe npm outdated` | Check outdated deps | Read-only |
| `safe npm list [--depth n]` | List installed packages | Read-only |
| `safe npm audit` | Security audit | Read-only |
| `safe npm cache clean` | Clean npm cache | Safe - cache only |

**Blocked npm operations**: `npm publish`, `npm unpublish`, `npm deprecate`, arbitrary script names

### .NET Commands (`safe dotnet <command>`)

| Command | Description | Safety Notes |
|---------|-------------|--------------|
| `safe dotnet build [project]` | Build project | Safe |
| `safe dotnet test [project]` | Run tests | Safe |
| `safe dotnet restore [project]` | Restore packages | Safe |
| `safe dotnet run [project]` | Run project | Safe for dev |
| `safe dotnet clean [project]` | Clean build output | Safe - build artifacts only |
| `safe dotnet publish [project]` | Publish project | Safe - local output |
| `safe dotnet format [project]` | Format code | Safe - style only |
| `safe dotnet watch [command]` | Watch mode | Safe wrapper |
| `safe dotnet tool list` | List installed tools | Read-only |
| `safe dotnet tool install <tool>` | Install a tool | Safe - additive |
| `safe dotnet new <template>` | Create from template | Safe - additive |
| `safe dotnet add package <pkg>` | Add NuGet package | Safe - additive |
| `safe dotnet add reference <proj>` | Add project reference | Safe - additive |
| `safe dotnet list package` | List packages | Read-only |

### Environment Commands (`safe env <command>`)

| Command | Description | Safety Notes |
|---------|-------------|--------------|
| `safe env info` | Show OS, runtime, shell info | Read-only |
| `safe env path` | Show PATH entries | Read-only |
| `safe env check <tool>` | Check if tool is available (with version) | Read-only |
| `safe env which <tool>` | Show tool location | Read-only |
| `safe env vars [filter]` | Show environment variables (filters secrets) | Read-only, sanitized |

### Proxy Commands (`safe proxy <tool> <args...>`)

The proxy system allows forwarding commands to any tool, validated against the full built-in allowlist. This is useful for tools not in the main command groups:

```
safe proxy curl -s https://example.com    # GET requests only
safe proxy az account show                # Read-only Azure CLI
safe proxy gh pr list                     # Read-only GitHub CLI
safe proxy kubectl get pods               # Read-only k8s
```

The proxy validates the full command (tool + args) against the allowlist before execution. Unknown commands are rejected.

### Meta Commands

| Command | Description |
|---------|-------------|
| `safe help` | Show all command groups and their commands |
| `safe help <group>` | Show commands in a specific group |
| `safe version` | Show SafeCommands version |
| `safe instructions` | Print CLAUDE.md integration instructions |
| `safe instructions --install` | Auto-append instructions to project CLAUDE.md |
| `safe config show` | Show current configuration |
| `safe config path` | Show config file location |
| `safe config init` | Create default config at ~/.safecommands/config.json |

## Configuration

### Built-in Allowlist (Immutable)

The core allowlist is compiled into the binary. This ensures it cannot be tampered with by prompt injection or file manipulation.

### Extension Config (`~/.safecommands/config.json`)

```json
{
  "version": 1,
  "customCommands": [
    {
      "name": "terraform plan",
      "group": "proxy",
      "command": "terraform",
      "args": ["plan"],
      "allowExtraArgs": true,
      "description": "Run terraform plan"
    }
  ],
  "blockedCommands": [
    "npm run deploy"
  ],
  "allowedScripts": [
    "storybook",
    "e2e"
  ],
  "allowedProcessNames": [
    "ruby",
    "cargo"
  ],
  "safeDirs": [
    ".angular/cache",
    ".parcel-cache"
  ]
}
```

### Trust Verification

Custom config changes require user approval via SHA-256 hash verification (same pattern as P:\Dev's trust system). The trust store is at `~/.safecommands/trust.json`.

## AI Integration

### CLAUDE.md Instructions (output of `safe instructions`)

```markdown
## SafeCommands

This project uses SafeCommands (`safe`) for safe CLI operations.
Allowlist `safe` in your tool permissions to avoid per-command approval.

### Usage
- `safe git status` - Check git status
- `safe git add <file>` - Stage files
- `safe git commit -m "msg"` - Commit changes
- `safe file delete-temp` - Clean build artifacts
- `safe dotnet build` - Build the project
- `safe dotnet test` - Run tests
- `safe help` - See all available commands

### Safety Guarantees
All commands through `safe` are pre-validated:
- No destructive git operations (no force push, no hard reset)
- No deletion of untracked/uncommitted files (except temp/build dirs)
- No force flags on any operation
- Process kills limited to dev tooling processes only
```

### Machine-Readable Output

All commands support `--json` flag for structured output:

```bash
safe git status --json
# {"branch": "main", "clean": true, "staged": [], "modified": ["file.txt"]}
```

## Non-Functional Requirements

1. **Performance**: Command execution overhead < 50ms beyond the underlying tool
2. **Zero dependencies at runtime**: No API calls, no network (except for proxied commands)
3. **Single binary**: Distributed as dotnet global tool
4. **Startup time**: < 200ms to first output
5. **Error messages**: Clear, actionable, include the blocked reason and safe alternative

## Future Considerations

- Shell completion scripts generation
- MCP server mode (expose commands as MCP tools)
- Audit log of commands executed
- Project-level config overrides (`.safecommands.json` in repo)
- VSCode extension for visual command palette
