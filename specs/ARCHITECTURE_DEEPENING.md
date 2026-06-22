# Architecture Deepening Candidates

> Status: exploration / backlog. Captured 2026-05-30 from a codebase architecture review
> (`/improve-codebase-architecture`). These are *module-deepening* opportunities in the
> sense of Ousterhout's *A Philosophy of Software Design*: a deep module has a small
> interface hiding a large implementation, is more testable, and lets you test at the
> boundary instead of inside. Nothing here is committed work yet.
>
> **Update 2026-06-11:** Candidate 1 (with Candidate 2 folded in) is **shipped to `main`** via
> the deep Safety-Policy stack #6→#10 — declared `Policy` chains enforced centrally at dispatch,
> `IRepoProbe`/`IWorkspace` ports, ~225 boundary tests. 10 of 12 groups now carry policies
> (`dotnet`/`env` excepted — pure passthrough, no rules to enforce). The "Context" section below
> describes the *pre-migration* state captured 2026-05-30 and is retained as the original
> snapshot; the per-idiom file:line evidence no longer matches `main`. The separately-attempted
> inline migration (PRs #4/#5, the old "6-PR plan") was superseded by this stack and closed.

## Context: the codebase is mid-migration

Issue **#2** is a 6-PR staged migration to a ports & adapters architecture. **PR 1 has
landed** and introduced the seam:

- `Infrastructure/Ports/Ports.cs` — `record Ports(IExecutor Exec, IRenderer Render)`, threaded through every handler
- `Infrastructure/Ports/IExecutor.cs` / `IRenderer.cs` — the ports
- `Infrastructure/Adapters/ProcessExecutor.cs` / `ConsoleRenderer.cs` — the real adapters
- `Sugar/Run.cs` — a thin handler facade (`Run.Tool`, `Run.Bun`)
- `Safety/Policy.cs` — a pure `Rule`/`PolicyResult` chain
- `Registry/CommandDefinition.cs` — a **legacy-handler shim** that adapts old
  `(string[] args, bool json) => int` handlers to the new `(Ports, string[]) => int` signature

Exactly **one of twelve** command groups (`bun`) has been migrated. The other eleven still
ride the legacy shim, call the static `ProcessRunner`/`OutputFormatter` directly, and never
touch `Ports.Exec`. Most friction below radiates from this half-finished state — but the
headline issue (scattered safety validation) predates it.

Tests today: `PolicyTests` (1 of ~8 rules), `RendererEnvelopeTests` (the new renderer),
`BunCommandsTests` (the one migrated handler). **11 of 12 groups have zero tests, and they
contain virtually all the real safety logic.**

---

## Candidate 1 — The safety `Policy` is hollow; "is this command safe?" is scattered ⭐ ✅ shipped (#6→#10)

**Cluster:** `Safety/Policy.cs` vs. inline validation in `GitCommands`, `DbCommands`,
`DockerCommands`, `ProcessCommands`, `NpmCommands`, `PnpmCommands`, `ProxyCommands`.

**Why coupled:** Every group co-owns the product's core concept — "decide whether a command
is safe to run" — yet each re-derives it inline against its own private `HashSet`.
`Policy.cs` is the intended owner but holds **one rule** (`AllowOnlyScriptsRule`), used by
**one group** (bun, via `Run.Bun`).

**Evidence of scattering — ≥5 structurally distinct validation idioms:**

1. Blocklist by exact flag, `foreach` + `HashSet.Contains` — `GitCommands.cs:327-336` (`PushBlockedFlags`), `DockerCommands.cs:124-133` (`ComposeDownBlocked`)
2. Same, but `.ToLowerInvariant()` per arg — `DbCommands.cs:77-79` (`DestructiveFlags`)
3. Substring scan `arg.Contains("fresh"/"reset"/"rollback"/"wipe")` — `DbCommands.cs:204` (broader; catches different inputs than #1)
4. Exact positional token `args.Contains("zero")` / `args.Contains("--amend")` — `DbCommands.cs:232`, `GitCommands.cs:270`
5. Positive allowlist that silently **drops** unknown flags — `GitCommands.cs:402-433` (`FilterFlags`) **and a near-identical but divergent copy** in `DockerCommands.cs:137-159`
6. Prefix-match subcommand allowlist — `ProxyCommands.cs:196-213`
7. Single-value allowlist — `ProcessCommands.cs:211` (`AllowedKillNames`)

The build/test/lint script allowlist is **copy-pasted verbatim 3×** (`NpmCommands.cs:8-15`,
`PnpmCommands.cs:8-15`, `BunCommands.cs:10-17`); only bun routes it through `Policy`.

**Latent bugs that exist *because* validation has no owner:**

- **Proxy `AllowedFlags` is dead data.** Each `AllowedProxy` declares a per-subcommand flag
  allowlist (`ProxyCommands.cs:16-120`), but `RunProxyFor` only checks `SubcommandPrefix`
  (`:196-213`) — the flag allowlist is never enforced.
- **`--flag=value` bypass.** The exact-token blocklists (#1, #2, #4) don't normalize
  `--force=true`, so it slips past; `FilterFlags` (#5) *does* handle `=`. Divergent behavior.
- **`--json` blocked-envelope fork.** `ConsoleRenderer.Blocked` emits structured JSON under
  `--json` (`ConsoleRenderer.cs:55-59`), but every handler still calling
  `OutputFormatter.WriteBlocked` emits Spectre **markup with no JSON branch**. So
  `safe git push --force --json` silently violates the `--json` contract; only bun is correct.
- **Inconsistent block semantics.** Reported command granularity (offending flag vs. whole
  arg vector), suggestion truncation (`Take(15)` + conditional ellipsis in `Policy.cs:45` vs.
  `Take(10)` in `ProcessCommands.cs:215` vs. unconditional literal `"..."` in
  `NpmCommands.cs:112`), and case-sensitivity all vary by handler.

**Dependency category:** **in-process** for arg-based rules — *with a real wrinkle:*
clean-tree / is-a-repo / amend-already-pushed checks (`GitCommands.cs:53-84,282-295`) must
**probe the environment** (shell out to git), so a general policy needs an injected executor
(**local-substitutable**, faked via `FakeExecutor`). Also note idiom #5 *rewrites* args
(drops unknowns) rather than returning a verdict — so the abstraction must decide whether it
owns arg **sanitization** or only **allow/block verdicts**.

**Test impact:** `PolicyTests` covers 1 of ~8 rules. Force-push, `--accept-data-loss`,
kill-name allowlist, compose `-v`, etc. have **zero** tests. Extracting them as pure
`args → verdict` rules makes them table-testable **without** first migrating the handlers —
which decouples this candidate from Candidate 3.

> **Chosen for design.** A full interface RFC for this candidate (the hybrid
> declarative-policy-as-data + domain-shaped ports design) lives in
> [`RFC-safety-policy.md`](./RFC-safety-policy.md).

---

## Candidate 2 — Path sandboxing (STRIDE E1) has no owning abstraction ✅ shipped (#8, as `RequirePathWithinProject`)

**Cluster:** `FileCommands.ValidatePath` (`:36-55`) + its 18 inline call sites + the
`SafeDeleteDirs` ancestor-walk in `delete-pattern` (`:594-619`) + `delete-temp`'s third
interpretation of "safe dir" (`:468-524`) + **`GenerateCommands.RunHashFile`'s divergent
copy** of the containment check (`:566-579`).

**Why coupled:** Two files and three code-shapes co-own "is this path safe to touch" — the
project's primary file-safety guarantee (STRIDE E1, file write outside project dir). The
copies **already diverge**: `ValidatePath` uses the positive form
`full == root || StartsWith(root)`; `RunHashFile` uses the De Morgan'd negative. There are
also two *different notions* of safe path — containment-in-root vs. inside-an-ephemeral-build-tree
— that `delete-pattern` must run in sequence, implemented with completely different code.

**Shallowness:** `ValidatePath` fuses three jobs (resolve / decide / `WriteBlocked`) and
returns a bare bool the caller must remember to short-circuit on; it can't be reused by a
caller wanting a different message, nor tested without producing output and reading ambient CWD.

**Dependency category:** **in-process** for the decision (path + root → verdict);
**true-external** only for the actual `File.Delete`/`Directory.Delete`. The decision is the
deepenable part.

**Test impact:** Highest-leverage security test surface in the repo — traversal
(`../../etc/passwd`), the `/proj` vs `/projEvil` sibling-prefix case the code explicitly
guards (`FileCommands.cs:41-43`), and the "nested safe dirs" fix from commit `f63c5a3`
(which has **no regression test**). All currently untestable.

> Candidate 2 is a security-flavored facet of Candidate 1 ("validation has no home"). A
> strong move is to design it as the first concrete path-rule under Candidate 1's abstraction.

---

## Candidate 3 — Finish the migration: collapse the two-world shim and two rendering paths

> **Update 2026-06-20 (re-assessed against `main`; first slice shipped).** The 2026-05-30 snapshot
> below was verified against current `main` — most of its counts were stale (the Safety-Policy stack
> #6→#10 and Candidate 4 #14 had moved the landscape). Corrected status:
>
> | Sub-finding | Doc snapshot | Verified on `main` |
> |---|---|---|
> | Legacy `(string[],bool)` shim | 11/12 groups | **9/12** (`git, file, process, docker, npm, pnpm, dotnet, db, env`); native = `generate, bun, proxy`. `meta` is a separate direct-dispatch legacy path, outside the registry |
> | `OutputFormatter.WriteBlocked` sites | ~28 across 13 files | **6, all in `FileCommands`**. `OutputFormatter.*` = 163 calls / 11 files. `ConsoleRenderer` still depends on `OutputFormatter.JsonOptions` (load-bearing) |
> | `--json` blocked-envelope fork | latent bug | **still live**, but only the 6 `FileCommands` app-level blocks |
> | `Run.cs` | `Tool`+`Bun`, `Bun` hardcodes `"bun"` | unchanged |
> | Copy-pasted `RunXxx` envelope wrappers | ~17 | 6 central helpers + 9 inlined copies in `db` |
> | Inline `"Usage:"` guards | ~51 | **54** across 12 files |
> | `ProcessExecutor` seam | no-op; 11/12 bypass `Ports.Exec` | still a no-op seam; **9 of 11** tool-spawning groups bypass it (bun, proxy route through; generate spawns nothing) |
> | `IRenderer.JsonMode` leak | leaks; handlers hand-build JSON | still leaks; C4 added `Render.Json(object)` but only `generate` uses it; ~49 `WriteJson` sites / 10 files |
> | `Program.cs` → testable `Dispatch` | extract for tests | **partly done**: `CommandDispatcher.Execute` exists + is table-tested. Outer shell (routing, `--json` strip + proxy arg-splice, `--help`, global try/catch) is still untested top-level statements; no `Dispatch(Ports,string[])` |
>
> **Slice 1 shipped (PR #15):** the four pure-passthrough groups **`docker`, `dotnet`, `npm`, `pnpm`**
> moved onto `(Ports,string[])` + `Sugar/Run.Tool`, deleting their per-group envelope helpers and routing
> them through `IExecutor` — so a `FakeExecutor` now absorbs their spawns and "allowed input never spawns
> a real tool" is finally assertable at the dispatch boundary (+43 tests). `npm list --json` was
> normalized to the standard `{exitCode,output,error}` envelope (it was the lone raw-passthrough outlier;
> `outdated`/`audit`/`view` already envelope-wrapped). Shim count drops **9 → 5**.
>
> **Slice 2 shipped (PR #16):** **`db`** migrated — the `RunNpx` facade + 9 inlined envelope copies
> (EF/artisan/django) collapsed onto `Run.Tool` across all four fronting tools (`npx`/`dotnet`/`php`/`python`).
> Its `BlockFlags`/`BlockSubstrings` migration-safety policies were untouched (enforced centrally at dispatch,
> independent of handler signature), and the destructive-flag block + `--name`/`<name>` Usage guards are now
> assertable at the dispatch boundary against a `FakeExecutor` (+19 tests). Shim count drops **5 → 4**.
> Pre-existing dead `PrismaBlockedCommands` set (declared, never read) left in place — out of scope.
>
> **Slice 3 shipped (PR #17):** **`env`** migrated — the first **dual-mode** group (every handler
> hand-built a typed payload and branched on `bool json`). Now routes through `IRenderer` via the
> sanctioned `if (JsonMode) Render.Json(payload) else Render.Info(...)` pattern — the first real use of
> `Render.Json` outside `generate` — dropping all `OutputFormatter` + raw `Console.WriteLine`. `env check`
> / `env which` spawns moved onto `IExecutor` (`where`/`which` probe + `--version`), so env is now fully
> `FakeExecutor`/`FakeRenderer`-testable incl. the secret-masking path (+19 tests, incl. mask-under-`--all`
> and exclude-without-`--all`). Shim count drops **4 → 3**. Two small documented behavior changes: the
> `env vars --all` warning moved to `Render.Warning` (auto-suppressed under `--json`; `"WARNING:"` → `"Warning:"`
> prefix), and `RunCheck` dropped `CommandExists`'s try/catch (a missing `where`/`which` binary — effectively
> impossible on a dev host — now surfaces via the global handler rather than silently reporting not-found).
>
> **Slice 4 shipped (this PR):** **`git`** migrated — the core safety surface. The central `RunGit`
> helper collapsed onto `Run.Tool`, and the two **dual-mode** handlers (`status`, `branch`) keep their
> custom-JSON parse but now spawn the probe via `IExecutor` and emit via `Render.Json` (reusing the env
> pattern). Every git spawn — passthrough *and* the `--porcelain`/`--list` probes — now routes through
> `IExecutor`, **closing git's SPAWN HAZARD**: an allowed `git status` is now absorbed by a `FakeExecutor`
> at the dispatch boundary (new `Dispatch_GitStatus_Allowed...` test). The declared Policy chains in
> `Register()` (force-push/amend/clean-tree/`RequireGitRepo`/`AllowOnlyFlags`) are untouched — enforced
> centrally at dispatch, independent of the handler signature. +44 tests pinning every argv shape
> (single-positional vs splat, `checkout-file`'s `--` insertion, stash subcommands, both JSON parsers).
> Shim count drops **3 → 2**. Behavior-preserving (no argv or policy change).
>
> **Slice 5 shipped (this PR):** **`file`** migrated — the largest group, and the one carrying path
> sandboxing (STRIDE E1/I2). Its 6 app-level `OutputFormatter.WriteBlocked` sites (overwrite protection
> on `copy`/`write`; the git-tracked + clean-tree gates on `delete-tracked`/`move`) moved to
> `Render.Blocked`, **closing the last `--json` blocked-envelope fork** — those blocks now emit the
> structured `{blocked,…}` envelope under `--json` instead of Spectre markup. All output dropped
> `OutputFormatter` + raw `Console`/`AnsiConsole`; `file read`'s byte-faithful content dump is preserved
> by a new additive `IRenderer.Raw(string)` primitive (verbatim stdout, no added newline — the faithful
> translation of the old `Console.Write(content)`, pinned at the adapter level since a `FakeRenderer`
> structurally can't exhibit a newline regression). The group's only spawns — the `delete-tracked` /
> `move` git probes — now route through `IExecutor`, **closing file's SPAWN HAZARD** (new dispatch-level
> test absorbs the `ls-files` probe in a `FakeExecutor`). The declared path-containment policies in
> `Register()` (`RequirePathWithinProject` / `RequireWithinSafeDeleteDir`) are byte-identical — E1/I2
> sandboxing is untouched, still enforced centrally at dispatch before the handler. +48 tests. Shim count
> drops **2 → 1** (only `process` remains on the legacy shim). STRIDE was reviewed and needs no entry: an
> execution-seam reroute (`ProcessRunner` → the existing `IExecutor`/`ProcessExecutor` seam) plus render
> consistency, with path validation unchanged — no new threat and no mitigation weakened (consistent with
> slices 1–4).
>
> **Slice 6 shipped (this PR — group migration COMPLETE):** **`process`** migrated, the last group on
> the legacy shim. Its tool spawns (`netstat`/`ss`/`lsof`, plus the `ss`-availability probe formerly via
> `ProcessRunner.CommandExists`) now route through `IExecutor`; its raw process control
> (`Process.GetProcesses`/`GetProcessesByName`/`GetProcessById`/`Kill`) moved behind a NEW **`IProcessHost`**
> port (`List`/`FindByName`/`Kill`, returning `ProcessInfo`/`KillOutcome` value types) with a `ProcessHost`
> adapter that consolidates the per-process "exited mid-enumeration" races in one place. **Closes process's
> SPAWN HAZARD** — a `FakeProcessHost` absorbs enumeration and kills, so an allowed `kill-name` is testable
> at the dispatch boundary without touching a real process (new dispatch allow/block pair proving STRIDE
> **E2**: an allowlisted name routes to the host; a disallowed name is blocked *before* it). The kill-name
> dev-tools allowlist policy in `Register()` is byte-identical. `IProcessHost` was added as the 5th `Ports`
> member (Program.cs + ~26 test sites updated). +26 tests incl. real-adapter `ProcessHostTests`. Adversarial
> review (5 dimensions, 17 agents): 0 shipping-logic/safety/policy regressions; 5 findings (1 test-quality
> should-fix + 4 nits) all addressed. Two small documented behavior changes, both consistent with prior
> slices: the `ss`-availability probe dropped `CommandExists`'s try/catch (a missing `which` on a Unix host —
> effectively impossible — now surfaces rather than silently falling back to `lsof`; identical to env slice
> 3's `RunCheck`), and the `kill-port`/`kill-name` "nothing matched" paths now emit a valid empty JSON object
> under `--json` (`{port,killed:[]}` / `{killed:[],count:0}`) instead of the original's bare non-JSON line.
> STRIDE reviewed: no entry needed (E2's kill allowlist, enforced at dispatch, is unchanged — this is an
> execution-seam introduction, consistent with slices 1–5). **Shim count 1 → 0.**
>
> **Remaining:** the group migration is **done — all 12 registry groups are on `(Ports,string[])` and the
> legacy handler shim has zero registry users.** What's left is cleanup plus two independent items, each its
> own future slice:
> - **Retire the shim plumbing.** ✅ **Slice 7 (this PR):** the `CommandDefinition` legacy-handler ctor
>   (`Func<string[],bool,int>` → `(Ports,string[])` adapter) is **deleted** — the registry no longer carries
>   the two-world handler shape; its removal compiles clean and the full suite passes, which proves it had
>   zero callers (the only `(string[],bool)` handlers left are in `MetaCommands`, dispatched directly from
>   `Program.cs` *outside* the registry). ✅ **Slice 10:** `meta` (help/version/instructions) migrated onto
>   `(Ports,string[])` + `IRenderer` — its JSON/error/success now route through `Render` (the rich Spectre
>   Figlet/Table human help stays direct, since `IRenderer` doesn't model tables) — and **`OutputFormatter`
>   is deleted**, its lone surviving member `JsonOptions` relocated into `ConsoleRenderer` (its only
>   consumer). `IRenderer.JsonMode` is **kept by decision** — see the outcome note below; it is a legitimate
>   mode signal (npm `--json` arg-selection, git porcelain-vs-passthrough), not merely a leak.
> - **Consolidate the inline `"Usage:"` guards.** ✅ **Slice 8 (this PR):** added
>   `CommandDefinition.MinArgs`, enforced once at the dispatch seam (after policy, before the handler)
>   emitting `Usage: {Usage}`. **37 commands** (git/docker/env/db/process/file/dotnet/bun + npm `view`/pnpm
>   `why`) dropped their inline `args.Length` count-guards and now trust their args; the removed
>   per-handler no-arg tests collapsed into one parameterized `DispatchTests` theory. Genuine non-count
>   contracts stay inline by design: the `run` commands (npm/pnpm/bun — require a valid script;
>   `AllowOnlyFirstArg` permits empty args, so the guard is real), `file write --content`, `file
>   delete-pattern --in`, `db prisma-migrate-dev --name`, `process kill-port`'s range check, and `git
>   add`/`git commit`'s richer messages. The `generate` group and `proxy run` are deferred — their input
>   guards are positional-extraction (`IsNullOrEmpty(Positionals(...))`) / subsystem-specific, not the
>   uniform `args.Length` boilerplate this targets.
> - **Extract `Program.cs`'s outer shell.** ✅ **Slice 9 (this PR):** the top-level statements moved into
>   a table-testable static `Cli` — `StripJson(string[])` (the proxy-aware `--json` splice, pure) and
>   `Route(Ports, string[], bool)` (meta switch, group/command lookup + friendly unknown-X errors,
>   bare-group help, per-command `--help`, guarded dispatch). `Program.cs` is now an 11-line shell
>   (`Initialize → StripJson → build ports → Route`). Behavior-preserving, with one incidental fix: `safe
>   --json` (only the flag) used to throw `IndexOutOfRange` (the length check ran before the `--json`
>   strip, outside the try/catch) and now returns help cleanly. +25 tests pinning the splice + every
>   routing branch.
>
> **Candidate 3 — outcome (closed 2026-06-22).** Slices 1–10 (PRs #15→#24) delivered the substance: all 12
> registry groups **+ `meta`** are on `(Ports,string[])`; the legacy handler-shim ctor and the entire
> `OutputFormatter` class are **deleted**; the `--json` blocked-envelope fork is gone; the ~37 inline
> `"Usage:"` count-guards are a declarative `MinArgs` enforced at dispatch; `Program.cs` is a thin,
> table-tested `Cli` shell; and two new ports landed (`IProcessHost`, `IRenderer.Raw`). Test count ~225 → 582.
>
> **Two items are deliberately NOT done — a design call, not a backlog gap:**
> - **`IRenderer.JsonMode` stays.** It is not merely a rendering leak: for `npm outdated/list/audit/view` it
>   selects the *args passed to npm* (`--json`), and for `git status`/`branch` it picks the *execution
>   strategy* (a `--porcelain`/`--no-color` probe + parse vs. a plain passthrough). Removing it would force a
>   behavior change (e.g. `git status`'s human output would stop being native git) or a god-object renderer
>   that must know every command's heterogeneous human form — Spectre tables, recursive trees, fixed-width
>   columns, raw content — several of which (e.g. `Raw`) are intentionally *not* suppressed under `--json`.
>   The "typed payload, renderer owns both renderings" sketch below doesn't survive the control-flow cases,
>   so `JsonMode` is kept as a legitimate mode signal rather than half-removed at the cost of 29 closures.
> - **`generate`/`proxy` usage guards stay inline.** `generate`'s guards test positional *presence*
>   (`IsNullOrEmpty(Positionals(...))`), which the total-token `MinArgs` can't express, and `proxy run` is the
>   subsystem re-dispatcher — neither is the uniform `args.Length` boilerplate `MinArgs` targets. Converting
>   them would add a second arg-spec knob for a handful of sites; not worth the surface area.

**Cluster:** `CommandDefinition`'s legacy shim (`:43-52`) + the **two live output stacks**
(static `OutputFormatter`, ~28 `WriteBlocked` sites across 13 files, vs. `ConsoleRenderer`,
1 file) + `Run.cs` (too shallow — only `Tool`+`Bun`, and `Bun` hardcodes `"bun"`) + the ~17
copy-pasted `RunXxx` envelope wrappers + the ~51 inline `"Usage:"` checks.

**Why coupled:** This *is* issue #2's remaining work (PRs 2–6). One concept ("run a tool,
render the `{exitCode,output,error}` envelope") is implemented twice, with the shim
translating `Render.JsonMode` back to a `bool` and discarding `Ports.Exec` for 11/12 groups.
`ConsoleRenderer` re-implements `OutputFormatter`'s wording line-for-line and depends back on
its `JsonOptions`, so "legacy" is load-bearing, not dead. `ProcessExecutor` is, by its own
docstring, a "behavioural no-op... purely to introduce a seam".

**Dependency category:** **in-process** (the process boundary is already behind `IExecutor`).

**Test impact:** The **enabler**. Migrating a group onto `Ports` makes it
`FakeExecutor`/`FakeRenderer`-testable exactly like bun (the "blocked never spawns the tool"
assertion), retires the `--json` blocked-envelope fork, and makes the one
`RendererEnvelopeTests` authoritative for the whole CLI. It also makes Candidates 1 & 2
testable *at the handler boundary* (though their rules can be tested earlier).

**Sub-findings worth noting during this work:** `IRenderer` leaks `JsonMode` and instructs
callers to branch on it, so dual-mode handlers (e.g. `git status`, `EnvCommands`) hand-build
JSON — a deeper renderer could take a typed payload and own both renderings. `Program.cs`
concentrates routing + `--json` stripping (incl. a fragile proxy-aware arg-splice) + `--help`
+ the global try/catch as untested top-level statements; extracting `Dispatch(Ports, string[])`
would make it table-testable.

---

## Candidate 4 — `GenerateCommands` junk-drawer: pure transforms trapped in CLI plumbing ✅ shipped

> **Update 2026-06-20:** Shipped. The 14 transforms are extracted into pure, dependency-free
> modules under `Commands/Generate/` (`Uuid`, `Hashing`, `Codec`, `Jwt`, `Slug`, `Timestamps`,
> `RandomValues`) — clock and randomness are passed in as parameters, so all the deterministic logic
> is table-tested (RFC 4122 UUID vectors, NIST hash digests, the jwt.io sample). The `generate`
> handlers moved onto the `(Ports, string[])` signature and render through `IRenderer` (dropping the
> group off the legacy shim, 11→10). The scattered `Array.IndexOf`/`args.Contains` parsing across
> `generate`/`git`/`file`/`db`/`process`/`npm`/`env`/`docker`/`bun`/`meta` now has a single owner,
> `Sugar/Args.cs` (`HasFlag`/`Value`/`IntValue`/`ValuesAfter`/`Positionals`/`Without`), matching flag
> names case-insensitively. To keep that uniform case-handling safe, `Safety/PathArg.FlagValue` was
> flipped to case-insensitive in lockstep so a handler's `--in` lookup can never diverge from the
> policy's containment check (regression-tested). ~85 new tests. The hash-algorithm `switch` is no
> longer duplicated. The original analysis below is retained as the pre-ship snapshot.

**Cluster:** `GenerateCommands`' 14 self-contained transforms + its private
`HasFlag/GetOption/GetIntOption` (`:639-658`) + git's `FilterFlags` + inline `Array.IndexOf`
parsing in file/db/process + the hash-algorithm `switch` duplicated between `hash` (`:286-303`)
and `hash-file` (`:590-607`).

**Why coupled:** Arg-parsing is a cross-cutting concern reinvented ≥3 ways with no owner; the
transforms (UUID/jwt/slug/base64…) are bundled only for CLI ergonomics and are entangled with
the static output sink and `DateTimeOffset.UtcNow`.

**Dependency category:** **in-process** (pure logic; `timestamp` needs an injectable clock).

**Test impact:** ~100% pure deterministic logic — the cheapest thing in the repo to test —
yet **0% tested** because output and clock aren't injectable. Extract pure `string → string`
generators → trivially table-testable. Lowest architectural stakes, highest
testability-per-effort.

---

## Recommendation & ordering

1. **Candidate 1 (Safety Policy)** is the strongest target: it owns the product's core value,
   is mostly in-process (highly deepenable), and uniquely lets you test the safety rules *now*
   without waiting on the ports migration. Its one genuinely interesting design tension —
   pure-arg rules vs. rules that must probe the environment, and verdict-only vs.
   arg-sanitizing — is worth designing twice.
2. **Candidate 3** is the enabler everything else leans on, but it's closer to executing
   issue #2's existing plan than novel interface design.
3. **Candidate 2** is the sharpest security win and a clean, self-contained design — best
   folded in as the first concrete path-rule under Candidate 1's abstraction.
4. **Candidate 4** is the safe, cheap testability win for last.

**Chosen for interface design first: Candidate 1 (Safety Policy).**
