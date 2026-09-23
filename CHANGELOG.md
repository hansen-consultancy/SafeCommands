# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0] - 2026-09-23

A major version for one reason: the tool now targets .NET 10, so installing or updating it requires the .NET 10 runtime (.NET 8 reaches end of support in November 2026). Alongside that, squash-merged branches can be cleaned up without raw `git branch -D`, and a safety review closed three ways around the existing blocklists — including a pre-commit-hook bypass.

### Breaking
- **Requires the .NET 10 runtime.** Machines with only .NET 8 must install .NET 10 before `dotnet tool update --global HC.SafeCommands`.
- **Commands that are now refused** (each one could lose work or skip a safety check, so no legitimate use is expected): `safe git checkout -B`, `-f`, `--force` and `--discard-changes`; bundled or abbreviated spellings of any blocked flag (e.g. `commit -an`, `commit --no-verif`, `push --forc`); and the uppercase twin of an allowlisted short flag unless it is listed too.

### Added
- **`safe git branch-delete <name> [--into <target>]`**. Deletes a local branch only when nothing on it would be lost, so squash-merged PR branches (which plain `git branch -d` refuses as "not fully merged") can be cleaned up without the agent falling back to raw `git branch -D`. Tries `git branch -d` first; otherwise checks against `--into` (default `origin/HEAD`) and force-deletes only when (1) `git merge-tree --write-tree` shows merging the branch would leave the target's tree unchanged, or (2) the branch's net diff, squashed into a probe commit, has a patch-equivalent commit on the target (`git cherry`) — which covers the target editing the same lines again after the squash. The target must be a branch or tag other than the one being deleted (so `--into <self>`/`HEAD`/a hash can't "prove" it against itself). Fails closed on conflicts, probe errors, an unknown target or a branch tip that moves mid-check. Caller-supplied `-D`/`--force` are dropped by policy. `--into=<target>` is accepted; an `--into` with no value is a usage error rather than a silent fall-back to `origin/HEAD`. Requires git 2.38+ (`merge-tree --write-tree`).

### Changed
- **Now targets .NET 10 (LTS)**; .NET 8 reaches end of support in November 2026. Installing or updating the tool now requires the .NET 10 runtime. The explicit `System.Text.Json` 8.0.6 reference is dropped — it ships in-box with .NET 10. CI and publish workflows build with the 10.0 SDK.
- Test suite grew from 665 to **706**.

### Security
- **Blocklists catch bundled and abbreviated flags.** Blocked flags were matched as whole tokens, so `safe git commit -an -m x` (bundled `-a -n`) and `--no-verif` (git accepts unambiguous long-option prefixes) skipped pre-commit hooks, and `push --forc`/`-uf` got past the force-push block. `BlockFlags` now expands all-letter short-flag bundles and treats a prefix of a blocked long flag as that flag (STRIDE T2, T3). Only widens what blocks; `--force-with-lease` still works.
- **`safe git checkout` blocks `-B`, `-f`, `--force` and `--discard-changes`.** `-B` on an existing branch reset it and dropped its commits; `-f` discarded uncommitted changes, and `-f -b <new>` did so past the clean-tree check (STRIDE T7). `-b` (create) is unaffected.
- **Allowlist flags now match case-sensitively** ([#27](https://github.com/hansen-consultancy/SafeCommands/issues/27), STRIDE E6 → fully mitigated). Case folding is right for blocklists (`--Force` still blocks) but failed open for allowlists: an allowed `-r` (`gh pr create --reviewer`) also admitted `-R` (`--repo`). Subcommand allowlists and clean-tree exemptions now compare the exact flag; blocklists are unchanged. A registry-wide test asserts no allowlisted flag's case twin gets through. The gh create allowlists regain their short forms (`-t`, `-r`, `-f`, `-B`). Behaviour change: `safe git checkout -B` no longer rides `-b`'s dirty-tree exemption.

### Documentation
- `STRIDE.md` v10–v12: **T6** (unmerged work lost via `branch-delete`, mitigated), **E6** fully mitigated (score 6 → 3), **T2** hardened against bundled/abbreviated flags (score 6 → 3), **T3** annotated with the hook bypass it previously missed, and **T7** (forced/resetting checkout, fully mitigated). 162 commands across 12 groups.

## [1.1.0] - 2026-09-21

Two additions to what the gateway can do, both opt-in or additive: local completion history for the commands an agent ran, and PR/issue creation through the `gh` proxy. No command that worked in 1.0.0 behaves differently.

### Added
- **Opt-in command audit history** ([#1](https://github.com/hansen-consultancy/SafeCommands/issues/1), partial R1 mitigation). With `{"audit": true}` in `~/.safecommands/config.json`, each completed outer invocation appends one compact JSON line to `~/.safecommands/audit.log`: schema version, UTC start, canonical command (or a fixed meta/unknown category), argument *count*, working directory, OS username, PID, elapsed ms, exit code, and outcome. Disabled by default; a missing, malformed or unreadable config disables it without creating files, and configuration can never extend the allowlist or change a policy. Argument values, option strings, output, exception text and policy reasons are never written. Retention is fixed at three 10 MiB files with cross-process locking; audit failures never change a command's stdout, return value or exceptions. Best-effort completion history, explicitly **not** a tamper-resistant ledger — the current user can alter or remove it.
- **`safe proxy gh pr create` and `safe proxy gh issue create`** ([#28](https://github.com/hansen-consultancy/SafeCommands/pull/28)). Creation was already reachable through `gh api`'s POST-via-fields path, so this adds the ergonomic front door rather than new capability. Allowed flags: `--title`, `--body`/`-b`, `--base`, `--head`/`-H`, `--draft`/`-d`, `--fill`, `--assignee`/`-a`, `--label`/`-l`, `--reviewer` (`pr create`); title/body/assignee/label (`issue create`). Deliberately omitted: `--repo` (pins the write to the current repository), `--body-file` and `--template` (no arbitrary file read into a possibly-public body), `--editor` (interactive). `pr merge`, `pr close` and `issue close` remain blocked — they act on work that isn't the agent's. The `gh` proxy command is reclassified `ReadOnly` → `SafeWrite`; that label was already stale, since `gh api -f` reached creation.

### Changed
- Dependency bumps: Microsoft.NET.Test.Sdk 18.8.1 → 18.10.1, xunit.runner.visualstudio 3.1.5 → 4.0.0 (dev-only; still supports xunit v2, so xunit stays on 2.9.3). System.Text.Json deliberately stays on 8.0.6 — the latest 8.0.x — rather than 10.x, keeping the net8.0 servicing line.
- Test suite grew from 582 to **665**.

### Documentation
- `STRIDE.md` v9: **I8** (exfiltration via `gh` write verbs — fully mitigated by the flag omissions above) and **E6** (allowlist bypass via case-folded short flag — partially mitigated, [#27](https://github.com/hansen-consultancy/SafeCommands/issues/27)). E6 was surfaced by a regression test during #28: `Flag.Base` lowercases before matching, so an allowlisted short flag also admits its uppercase twin — `pr create -R` passed because `-r` (`--reviewer`) was allowlisted. Mitigated per-entry for now by listing dangerous-twin flags long-form only, pinned by tests; the structural fix (case-sensitive allowlist matching, case-insensitive blocklist matching) is tracked in #27.
- `STRIDE.md` v8 (shipped with the audit work): R1 downgraded to partially mitigated, plus **I7** (local audit metadata disclosure) and **D4** (audit storage availability), with the per-user storage boundary and retention/privacy controls documented.

## [1.0.0] - 2026-07-23

The stability milestone. Every one of the 161 commands across 12 groups now reaches the outside world through a single set of ports, safety is data evaluated centrally before any handler runs, and 582 tests pin those safety claims in memory — no processes, no filesystem. CI guards every push and gates every publish. The CLI surface has only grown (never shrunk) since 0.5.0, so this release is a "now stable" declaration rather than a breaking change: what `safe` accepts and rejects today is what we intend to keep supporting.

### Added
- **Continuous integration.** `ci.yml` builds and runs the full suite on every push and pull request to `main`. The publish workflow now runs `dotnet test` before packing, so a tagged release with a failing test can never reach NuGet.

### Changed
- **`safe git checkout -b <branch>` is now allowed with a dirty working tree.** The clean-tree guard exists to stop a plain branch *switch* from silently abandoning uncommitted work, but `checkout -b` *creates* a branch and carries those changes onto it (git refuses on conflict rather than discarding) — so the guard only obstructed the canonical "start a feature branch" move. `RequireCleanTree` gained an `exemptFlags` set; checkout exempts `-b` (case-folds to also cover `-B`). Plain `checkout <existing>` still requires a clean tree, and `checkout .` is still blocked.
- **(Behaviour change, scoped to `proxy gh api`)** The `gh api` flag allowlist now permits field (`-f`/`-F`), output (`-q`/`--jq`), and pagination flags for read and POST-via-fields create. `-X`/`--method` and `-H`/`--header` remain excluded, so DELETE/PUT/PATCH cannot be expressed through the gateway — neither directly nor via a method-override header ([#13](https://github.com/hansen-consultancy/SafeCommands/issues/13), STRIDE T5). Previously 0.5.0's newly-enforced allowlist left `gh api` usable only as a bare GET.
- **Path and flag arguments are matched case-insensitively.** Per-handler arg parsing was unified onto the shared `Sugar/Args` helper; `Safety` flag/path matching was flipped to case-insensitive in lockstep so a handler and its policy always read the same token (a flag like `--IN` cannot honor a path the policy didn't check). Net-safer — matches a superset of before, containment still enforced.

### Internal
- **Ports-and-adapters migration complete ([#2](https://github.com/hansen-consultancy/SafeCommands/issues/2) closed).** All twelve command groups (git, file, db, env, docker, dotnet, npm, pnpm, proxy, process, meta, generate) now reach the OS only through ports — `IExecutor`, `IRenderer`, `IRepoProbe`, `IWorkspace`, `IProcessHost` — with real adapters and in-memory fakes. `env` and `dotnet`, the last two on the legacy passthrough path in 0.5.0, migrated across.
- **`Program.cs` outer shell extracted into a testable `Cli`**, so routing and `--json` handling are exercised by unit tests rather than only via subprocess.
- **Dead code and transitional shims removed:** `OutputFormatter` (superseded by `IRenderer`/`ConsoleRenderer`), the legacy `Func<string[], bool, int>` handler-shim constructor, and `ProcessRunner.RunPassthrough`.
- Inline Usage guards consolidated into a declarative `MinArgs`; the `generate` group deepened.
- Test suite grew from 225 to **582**.
- Dependency bumps: Spectre.Console 0.50.0 → 0.57.2, System.Text.Json 8.0.5 → 8.0.6 (kept on the net8.0 servicing line, deliberately not 10.x), and the test stack (xunit 2.9.3, Microsoft.NET.Test.Sdk 18.8.1, xunit.runner.visualstudio 3.1.5, coverlet.collector 10.0.1).

### Documentation
- `STRIDE.md` v7: ASVS control-column backfilled across every threat, new E5 (`kill-port` kills the listening port-holder without the dev-tool name allowlist — accepted with recommendation), architecture diagram refreshed to the Cli + Ports/Adapters shape, command count corrected to 161. `CLAUDE.md` now documents the bidirectional, same-PR STRIDE update process.

## [0.5.0] - 2026-06-12

A foundational release. SafeCommands' core value — *validate before running* — now has a single owning abstraction. Safety validation moves from ~5 hand-rolled idioms scattered across handlers onto declarative `Policy` chains evaluated **once, centrally, before the handler runs**. This completes the deep Safety-Policy migration ([#2](https://github.com/hansen-consultancy/SafeCommands/issues/2); RFC in `specs/RFC-safety-policy.md`), closes all three latent validation defects, and brings the structured `--json` Blocked envelope to every migrated group.

### Added
- **Deep `Safety.Policy` abstraction.** A command's safety contract is an ordered `Rule` chain attached to its `CommandDefinition` as data. Ten fluent builders (`BlockFlags`, `BlockSubstrings`, `AllowOnlyFirstArg`, `AllowOnlyFlags`, `AllowSubcommands`, `RequirePathWithinProject`, `RequireGitRepo`, `RequireCleanTree`, `RequireHeadNotPushed`, `Custom`) over `Allow`/`Block`/`Rewrite` verdicts. `Flag.Base` normalizes `--flag=value` once before any flag rule runs.
- **Central enforcement at dispatch.** `CommandDispatcher` evaluates the policy before invoking the handler; a blocked command renders a uniform envelope (including `--json`) and **structurally cannot spawn the tool** — handlers can no longer forget to validate.
- **Two domain ports for full in-memory testability:** `IRepoProbe` (git state, cached to one spawn per question per run) and `IWorkspace` (path resolution + the project-root boundary), with `GitRepoProbe`/`FileSystemWorkspace` adapters and `FakeRepoProbe`/`FakeWorkspace` test doubles.
- **Path sandboxing is now an owned, tested rule** (`RequirePathWithinProject`, STRIDE E1), replacing 14 inline `ValidatePath` call sites and a divergent copy in `generate hash-file`.
- Test suite grew from 30 to **225** tests — the bulk exercising the safety rules at the boundary (force-push, `--accept-data-loss`, kill-name, compose `-v`, path traversal, `/proj` vs `/projEvil`, clean-tree) with zero processes or filesystem access.
- Design records: `specs/RFC-safety-policy.md` and `specs/ARCHITECTURE_DEEPENING.md`.

### Changed
- **(Behaviour change — structured `--json` Blocked envelope across all migrated groups.)** Blocked commands now emit `{ "blocked": true, "command", "reason", "suggestion" }` under `--json` for git, file, db, docker, npm, pnpm, process, generate, and proxy (joining bun from 0.4.0). Previously only bun was correct; the others emitted Spectre markup regardless of `--json` (defect #3). `dotnet` and `env` remain on the legacy path — pure passthrough, no blocked output to convert.
- **(Behaviour change, scoped to `proxy` group)** The per-subcommand flag allowlist — previously declared but never checked (defect #2) — is now enforced: `safe proxy gh api -X POST` and `safe proxy terraform plan -auto-approve` are blocked. Subcommand matching is now token-boundary (a prefix like `status` no longer accepts `status-quo`).
- **`safe proxy <tool>` unknown-tool message.** With the proxy dispatch bypass removed, the direct form `safe proxy foo bar` now surfaces the generic `Unknown command: proxy foo` (which lists the available proxy commands); the explicit `safe proxy run foo` form still emits the `not in the proxy allowlist` envelope. Both refuse to execute the tool — a message-only difference.
- **`proxy` JSON output shape.** Proxy commands now emit the shared `{ exitCode, output, error }` result envelope used by every migrated group; the previous proxy-only `tool` field was dropped.

### Fixed
- **`--flag=value` validation bypass (defect #1).** Exact-token flag blocklists didn't normalize `--force=true`, `--accept-data-loss=…`, etc., so they slipped past on `git push`, db migrations, and `npm audit-fix`. `Flag.Base` normalization now catches them.
- **`delete-pattern --in TestResults` failed closed (N2).** The safe-delete-dir check lowercased only the candidate segment, leaving the canonical mixed-case `TestResults` allowlist entry permanently unmatchable. Now matched case-insensitively. (Pre-existing since the unreleased 3a slice.)

### Internal
- Collapsed the 3×-duplicated run-script allowlist (npm/pnpm/bun) into a single `PackageScripts.Allowed`.
- `CommandRegistry.Initialize()` is now idempotent and thread-safe (harmless under production's single startup call; the new parallel test collections raced on the shared registry).

### Documentation
- `STRIDE.md`: E1/I2 path-containment and E3 proxy-validation mitigations updated to the centrally-evaluated, boundary-tested rules, with new review-history rows.

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
