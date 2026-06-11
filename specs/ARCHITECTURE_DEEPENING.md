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

## Candidate 4 — `GenerateCommands` junk-drawer: pure transforms trapped in CLI plumbing

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
