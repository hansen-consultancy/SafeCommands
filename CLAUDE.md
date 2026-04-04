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
- Each command group (git, file, docker, generate, etc.) registers its commands in a static `Register()` method
- **ProcessRunner** handles cross-platform process execution via `Process.Start()` with `ArgumentList` (no shell interpretation)
- **OutputFormatter** provides human and JSON (`--json`) output modes
- Safety levels: `ReadOnly`, `SafeWrite`, `TargetedWrite` (with pre-validation)

## Key Design Decisions

- No Spectre.Console.Cli - manual dispatch for flexibility with 100+ commands
- Supply chain aware: `npm install`, `bun install`, `dotnet tool-install` marked as `TargetedWrite` with warnings about postinstall scripts. `pnpm install` is `SafeWrite` since pnpm disables lifecycle scripts by default.
- Git safety: checkout/merge/pull require clean working tree, push blocks --force, add blocks -A/.
- Proxy system validates full command against allowlist before forwarding

## Security Documentation

### STRIDE.md Threat Model

This repository includes a STRIDE threat model (`STRIDE.md`) for security analysis.

**When to update STRIDE.md:**
- Adding new command groups or commands
- Changing how ProcessRunner executes external tools
- Adding file system operations or changing path validation
- Modifying the proxy allowlist or flag filtering logic
- Adding configuration file loading or trust verification
- After security incidents or penetration test findings

**How to update:**
1. Add new threats to the relevant STRIDE category (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege)
2. Assess likelihood (1-4) and impact (1-4), score = likelihood x impact
3. Document existing mitigations or add recommendations
4. Link GitHub issues for unresolved findings
5. Update the Review History table

**High-priority findings:**
- R1: No audit trail (score 9) - add structured logging
- I1: Incomplete secret masking in env vars (score 9) - expand patterns
- E1: File write outside project directory (score 8) - add path sandboxing
