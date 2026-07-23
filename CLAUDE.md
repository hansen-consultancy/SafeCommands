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
- Git safety: checkout/merge/pull require clean working tree (but `checkout -b` is exempt — creating a branch carries uncommitted changes onto it rather than discarding them), push blocks --force, add blocks -A/.
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
- When addressing security recommendations from the document
- **When a change mitigates or resolves an existing finding** — move it to Mitigated/Resolved (update the mitigation text, score/status, and risk-summary row)

**Updates are bidirectional and ride in the same PR.** Whether a change *introduces/surfaces* a threat or *mitigates/resolves* one, the matching `STRIDE.md` edit ships in the **same PR** as the code/config change — never as a follow-up. A fix that closes a tracked finding is not done until `STRIDE.md` (and the linked issue's status) reflects it. Treat a security-relevant diff with no STRIDE.md change as incomplete.

**How to update:**
1. Add new threats to the relevant STRIDE category (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege)
2. Assess likelihood (1-4) and impact (1-4), score = likelihood x impact
3. Document existing mitigations or add recommendations, citing the control (ASVS chapter / infra ref) in the Control column
4. Link GitHub issues for unresolved findings
5. Update the Review History table

**Tracking critical findings:**
- Critical/High risk findings should have a linked GitHub issue with `security` label
- Review STRIDE.md annually or after major releases

**High-priority findings:**
- R1: No audit trail (score 9) - add structured logging ([#1](https://github.com/hansen-consultancy/SafeCommands/issues/1))
- I1: Incomplete secret masking in env vars (score 9) - **mitigated** (allowlist by default, expanded masking with `--all`)
- E1: File write outside project directory (score 8) - **mitigated** (declared path-containment policy at the dispatch seam)
