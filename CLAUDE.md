# SafeCommands

A safe command gateway CLI for AI coding agents. Provides pre-validated CLI operations so agents can be allowlisted without per-command approval.

## Project Structure

```
specs/PRD.md              # Product requirements document
src/SafeCommands/         # Main .NET project
  Program.cs              # Entry point, CLI arg routing
  Registry/               # Command definitions and registry
  Commands/               # Command group implementations
  Infrastructure/         # ProcessRunner, OutputFormatter
```

## Build & Run

```bash
dotnet build src/SafeCommands
dotnet run --project src/SafeCommands -- <args>

# Example: dotnet run --project src/SafeCommands -- help git
```

## Install as global tool (for local dev)

```bash
cd src/SafeCommands
dotnet pack
dotnet tool install --global --add-source ./nupkg HC.SafeCommands
```

## Architecture

- **CommandRegistry** holds all command definitions (immutable, built-in)
- Each command group (git, file, docker, etc.) registers its commands in a static `Register()` method
- **ProcessRunner** handles cross-platform process execution via `Process.Start()` with `ArgumentList` (no shell interpretation)
- **OutputFormatter** provides human and JSON (`--json`) output modes
- Safety levels: `ReadOnly`, `SafeWrite`, `TargetedWrite` (with pre-validation)

## Key Design Decisions

- No Spectre.Console.Cli - manual dispatch for flexibility with 100+ commands
- Supply chain aware: `npm install`, `bun install`, `dotnet tool-install` marked as `TargetedWrite` with warnings about postinstall scripts. `pnpm install` is `SafeWrite` since pnpm disables lifecycle scripts by default.
- Git safety: checkout/merge/pull require clean working tree, push blocks --force, add blocks -A/.
- Proxy system validates full command against allowlist before forwarding
