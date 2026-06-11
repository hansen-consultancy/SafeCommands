# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- **(Behaviour change, scoped to `proxy` group)** Proxy validation is now enforced as a declared `Policy` at the dispatch seam (part of the [#2](https://github.com/hansen-consultancy/SafeCommands/issues/2) safety-policy migration). The per-subcommand flag allowlist — previously declared but never checked — is now enforced: e.g. `safe proxy gh api -X POST` and `safe proxy terraform plan -auto-approve` are blocked (the flag is not in that subcommand's allowlist). Subcommand matching is now token-boundary (a prefix like `status` no longer accepts `status-quo`). Blocked proxy commands now emit the structured `--json` Blocked envelope, like every other migrated group.
- **`safe proxy <tool>` unknown-tool message.** With the proxy dispatch bypass removed, an unrecognized direct form (`safe proxy foo bar`) now surfaces the generic `Unknown command: proxy foo` (which lists the available proxy commands) rather than the `not in the proxy allowlist` envelope; the explicit `safe proxy run foo` form still emits that envelope. Both forms refuse to execute the tool — this is a message-only difference, not a change in what is allowed to run.
- **`proxy` JSON output shape.** Proxy commands now emit the shared `{ exitCode, output, error }` result envelope used by every migrated group; the previous proxy-only `tool` field was dropped.

## [0.4.0] - 2026-04-30

### Added
- Internal ports-and-adapters seam (PR #1 of [#2](https://github.com/hansen-consultancy/SafeCommands/issues/2)):
  - `IExecutor` port abstracts external process invocation; real `ProcessExecutor` adapter wraps `ProcessRunner`.
  - `IRenderer` port abstracts user-facing output; real `ConsoleRenderer` adapter wraps `OutputFormatter` and Spectre.
  - `Ports` record threads both through every command handler.
  - `Run.Tool` / `Run.Bun` sugar collapses the previously-repeated `(args, json)` boilerplate into one-liners for passthrough commands.
  - `SafetyPolicy` (in namespace `SafeCommands.Safety`) holds composable rules; first rule shipped: `AllowOnlyScripts`.
- xUnit test project at `tests/SafeCommands.Tests/` with `FakeExecutor` / `FakeRenderer` fakes. Initial suite: 30 tests covering `BunCommands` handlers, `Policy` evaluation, and the JSON envelope contract.
- `SafeCommands.slnx` solution at the repo root.

### Changed
- **(Behaviour change, scoped to `bun` group)** Under `--json`, blocked commands now emit a structured envelope:
  ```json
  { "blocked": true, "command": "...", "reason": "...", "suggestion": "..." }
  ```
  Previously, blocked outputs always rendered as Spectre markup regardless of `--json`. This is the start of a per-group rollout: PR #1 lights up `bun` only. The other 11 groups still emit markup under `--json` until each is migrated in subsequent phases. Track progress in [#2](https://github.com/hansen-consultancy/SafeCommands/issues/2).
- `BunCommands` migrated to the new direct-port handler shape. No CLI surface change for `bun` users beyond the blocked-envelope contract above.
- `CommandDefinition.Handler` is now `Func<Ports, string[], int>`. A legacy constructor overload auto-adapts the existing `Func<string[], bool, int>` handler shape so unmigrated command groups required no source changes.
- **Error messages now route to stderr in human mode** (CLI convention). Previously, `OutputFormatter.WriteError` rendered Spectre markup to stdout, so piping `safe ... > file.log` would swallow error diagnostics. JSON-mode error output already went to stderr; this aligns the human path. Affects all groups (the dispatcher's `Unknown command: …` / `Unknown group: …` errors, and any handler calling `IRenderer.Error`).

### Internal
- `OutputFormatter.JsonOptions` is now `internal` (was `private`) so adapters share one source of truth for envelope shape.
- `InternalsVisibleTo("SafeCommands.Tests")` added to the main project.

## [0.3.1] - 2026-04-28

### Fixed
- `proxy` commands now preserve `--json` flag in forwarded args (was stripped before reaching the proxied tool's invocation context)
- `safe instructions` quick-reference table now includes the `generate` group

### Changed
- Internal rename: `SafetyLevel.TargetedWrite` → `CheckedWrite` to match the existing user-visible `checked-write` label. JSON output and help text unchanged.

### Documentation
- Added `UBIQUITOUS_LANGUAGE.md` glossary anchoring canonical domain terms (Agent vs Operator, Command Definition, Built-in Allowlist, Proxy Command, Safety Level/Rule/Guarantee)
- Updated `STRIDE.md` to cover the `generate` command group

## [0.3.0] - 2026-04-04

### Added
- `generate` command group (14 commands) for standard value generation without throwaway scripts
  - `uuid` — v4 (default), v3/v5 (namespaced MD5/SHA1), v7 (time-ordered)
  - `secret` — cryptographic random base64 or hex (configurable length)
  - `password` — random alphanumeric with optional special characters
  - `hash` — SHA256/SHA384/SHA512/MD5 hash of a string
  - `hash-file` — hash a file's contents (path-sandboxed to project directory)
  - `random-bytes` — cryptographic random bytes in hex or base64
  - `timestamp` — ISO 8601, Unix seconds, or Unix milliseconds
  - `nanoid` — short URL-safe IDs with custom length/alphabet
  - `base64-encode` / `base64-decode` — base64 string encoding
  - `url-encode` / `url-decode` — percent-encoding for URLs
  - `jwt-decode` — decode JWT header and payload (no verification)
  - `slug` — convert text to URL-safe slugs

## [0.2.0] - 2026-04-04

### Added
- STRIDE threat model (`STRIDE.md`) with 15 identified threats across 6 categories
- `db` command group (22 commands) for Prisma, Drizzle, EF Core, Laravel, Django migrations
- `pnpm` command group (10 commands) with safer defaults (lifecycle scripts disabled)
- `bun` command group (6 commands)
- `safe instructions` now generates a ready-to-use Claude Code allowlist config from the registry

### Fixed
- **E1 (STRIDE)**: All file operations now sandboxed to project directory via `ValidatePath()`
- **I1 (STRIDE)**: Environment variables switched from blocklist to allowlist (safe vars only by default, `--all` flag with expanded masking)
- Git `commit` now blocks `--no-verify` flag (agents must not bypass pre-commit hooks)
- Git `commit --amend` redirected to `commit-amend` with push-check safety
- `npm audit-fix` blocks `--force` (prevents breaking major version changes)
- Terraform `destroy`/`apply` explicitly excluded from proxy allowlist
- Claude Code allowlist config updated to use correct `Bash(safe <group>:*)` pattern per group

### Changed
- `npm install` and `bun install` reclassified from SafeWrite to TargetedWrite (supply chain risk)
- `dotnet tool-install` and `dotnet add-package` reclassified to TargetedWrite

## [0.1.0] - 2026-04-04

### Added
- Initial release with 146 commands across 11 groups
- **git** (27 commands): status, log, diff, show, branch, tag, remote, blame, rev-parse, ls-files, shortlog, stash, stash-list, stash-pop, stash-apply, add, add-tracked, commit, commit-amend, fetch, branch-create, pull, push (--force-with-lease ok), checkout (clean tree required), checkout-file, merge, cherry-pick
- **file** (14 commands): list, read, exists, info, find, tree, mkdir, copy, write, delete-tracked, delete-temp, delete-locks, delete-pattern, move
- **process** (5 commands): list, find, ports, kill-port, kill-name (dev tools only)
- **docker** (18 commands): ps, images, logs, inspect, stats, network-ls, volume-ls, compose-ps, compose-logs, build, compose-build, compose-up, compose-restart, compose-pull, stop, start, restart, compose-down (no -v)
- **npm** (12 commands): outdated, list, audit, view, install (with supply chain warning), ci, run (script allowlist), test, build, audit-fix (no --force), cache-clean, dedupe
- **pnpm** (10 commands): outdated, list, audit, why, install (safe - no lifecycle scripts by default), run, test, build, store-prune, dedupe
- **bun** (6 commands): outdated, pm-ls, install (with warning), run, test, build
- **dotnet** (18 commands): list-package, list-reference, tool-list, info, sln-list, build, test, restore, run, clean, publish, format, watch, tool-install, add-package, add-reference, new, pack
- **db** (22 commands): Prisma (status, studio, generate, format, validate, migrate-dev, migrate-deploy, db-pull, db-seed), Drizzle (check, status, generate, migrate), EF Core (migrations-list, migrations-add, database-update, migrations-script), Laravel (artisan-migrate-status, artisan-migrate), Django (showmigrations, migrate, makemigrations)
- **env** (5 commands): info, path, check, which, vars (secrets masked)
- **proxy** (9 commands): run, curl (GET only), gh, az, kubectl, terraform (no destroy/apply), pip, cargo, make
- `--json` flag on all commands for machine-readable output
- `safe instructions` command with `--install` flag for CLAUDE.md integration
- Safety levels: read-only, safe-write, checked-write
- SpectreConsole rich help with FigletText header and color-coded safety
- Cross-platform support (Windows, macOS, Linux)
- GitHub Actions workflow for NuGet trusted publishing via OIDC
