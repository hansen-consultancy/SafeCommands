# RFC: A deep Safety Policy that owns "is this command safe?"

> Status: design / not yet implemented. Captured 2026-05-30 from a deepening exploration
> (`/improve-codebase-architecture`, Candidate 1 in `ARCHITECTURE_DEEPENING.md`). Promotable
> to a GitHub issue when ready. This is the **chosen hybrid** of three explored interfaces:
> declarative policy-as-data enforced centrally at dispatch + domain-shaped ports for full
> in-memory testability + a lean fluent rule vocabulary.

## Problem

SafeCommands' entire value proposition — *validate before running* — has **no owning
abstraction**. `Safety/Policy.cs` is the intended home but holds exactly one rule
(`AllowOnlyScriptsRule`), used by one group (bun, via `Run.Bun`). Every other safety decision
is hand-rolled inline across ~11 handlers as ≥5 structurally distinct idioms:

| Idiom | Where |
|---|---|
| Blocklist exact flags (`foreach` + `HashSet.Contains`) | `GitCommands.cs:327-336`, `DockerCommands.cs:124-133` |
| Same, lowercased | `DbCommands.cs:77-79` |
| Substring scan (`arg.Contains("fresh"/"reset"/…)`) | `DbCommands.cs:204` |
| Exact positional token (`args.Contains("zero"/"--amend")`) | `DbCommands.cs:232`, `GitCommands.cs:270` |
| Positive flag-allowlist that **rewrites** args (drops unknowns) | `GitCommands.cs:402-433` **and a divergent copy** `DockerCommands.cs:137-159` |
| Subcommand-prefix allowlist | `ProxyCommands.cs:196-213` |
| First-arg/name allowlist | `ProcessCommands.cs:211`, `NpmCommands.cs:98-117` (+ 3× duplicated script set) |
| Path containment, fused with rendering | `FileCommands.cs:36-55` **and a divergent copy** `GenerateCommands.cs:566-579` |
| Environment preconditions (must run git) | `GitCommands.cs:53-84,282-295` |

**Integration risk in the seams:** the concept is one thing (the product) but physically
scattered, so a fix or a new attack pattern must be applied N times in N idioms. This has
already produced **three latent defects**:

1. **`--force=true` bypass** — exact-token blocklists don't normalize `--flag=value`
   (`DbCommands.cs:79`, `GitCommands.cs:329`), so `--force=true` slips past. `FilterFlags`
   *does* split on `=` — inconsistent.
2. **Dead proxy `AllowedFlags`** — each `AllowedProxy` declares a per-subcommand flag
   allowlist (`ProxyCommands.cs:16-120`) that `RunProxyFor` never reads (`:196-213`).
3. **`--json` blocked-envelope fork** — `ConsoleRenderer.Blocked` emits structured JSON, but
   every handler still calling `OutputFormatter.WriteBlocked` emits Spectre markup with no
   JSON branch. `safe git push --force --json` silently violates the `--json` contract; only
   bun is correct.

**Why it's untestable today:** the dangerous rules live inside legacy `(string[], bool)`
handlers that call static `ProcessRunner`/`OutputFormatter`. `PolicyTests` covers 1 of ~8
rules; force-push, `--accept-data-loss`, kill-name, compose `-v`, path traversal, clean-tree
are all unverified, and several can only be exercised by spawning real git or touching the
real filesystem.

## Proposed Interface

A single deep `Policy` value, built from a small fluent vocabulary, **attached as data to each
`CommandDefinition`** and **enforced once at the dispatch site** before the handler runs.
Cross-boundary dependencies (git probes, filesystem/CWD) sit behind two narrow,
domain-shaped ports so the *entire* verdict is a pure function of `(args, port-state)`.

### Core types (`SafeCommands.Safety`)

```csharp
/// Verdict of a single rule. A rule may allow, reject, or REWRITE the arg vector
/// (the FilterFlags case — dropping unknown flags is itself the safety guarantee).
abstract record PolicyResult
{
    public sealed record Allow : PolicyResult;
    public sealed record Block(string Reason, string Suggestion = "") : PolicyResult;
    public sealed record Rewrite(string[] Args) : PolicyResult;
}

/// Everything a rule may read. Pure rules touch only `args`; probe/path rules read the ports.
/// One context hosts both, so the Rule contract never forces a probe dependency on a rule
/// that doesn't use one.
readonly record struct SafetyContext(string CommandLabel, IRepoProbe Repo, IWorkspace Workspace);

abstract record Rule
{
    public abstract PolicyResult Evaluate(string[] args, in SafetyContext ctx);
}

/// An ordered chain of rules — the safety contract for one command. Built declaratively at
/// registration time; replaces the inline foreach-loops AND the thin Policy of today.
sealed record Policy(IReadOnlyList<Rule> Rules)
{
    public static Policy Default { get; } = new([]);   // no checks; run verbatim

    // Fluent builders — each appends one rule. Names ARE the documentation.
    public Policy BlockFlags(IReadOnlyCollection<string> flags, string reason, string suggestion);       // A
    public Policy BlockSubstrings(IReadOnlyCollection<string> needles, string reason, string suggestion); // B
    public Policy AllowOnlyFirstArg(IReadOnlyCollection<string> allowed, string noun);                    // C/D
    public Policy AllowOnlyFlags(IReadOnlyCollection<string> allowedFlags,
                                 IReadOnlyCollection<string> valueFlags, bool keepPositionals = true);    // E (Rewrite)
    public Policy AllowSubcommands(IReadOnlyList<Subcommand> subcommands);                                // F
    public Policy RequirePathWithinProject(int argIndex = 0);                                             // G (IWorkspace)
    public Policy RequireGitRepo();                                                                       // H (IRepoProbe)
    public Policy RequireCleanTree();                                                                     // H
    public Policy RequireHeadNotPushed();                                                                 // H
    public Policy Custom(Rule rule);                                                                      // escape hatch

    /// The ONE entry point. Folds the chain: Allow → continue; Rewrite → continue with new
    /// args; Block → stop. Returns the final (possibly rewritten) safe args, OR the single Block.
    public PolicyDecision Evaluate(string[] args, in SafetyContext ctx);
}

/// The two-state answer the dispatcher/handler acts on — never the raw PolicyResult.
readonly record struct PolicyDecision(string[]? SafeArgs, PolicyResult.Block? Block)
{
    public bool IsBlocked => Block is not null;
}

/// One allowed proxy subcommand: a prefix plus the flags permitted under it. Flags ENFORCED.
readonly record struct Subcommand(string Prefix, IReadOnlyCollection<string> AllowedFlags);

/// Central flag normalization, applied once before any flag rule runs.
/// "--force=true" → "--force"; "-f" → "-f"; positional unchanged. Lowercased.
static class Flag { public static string Base(string token); }
```

### The two domain ports (`SafeCommands.Infrastructure.Ports`)

Narrow and *intention-revealing* — they expose safety facts, not mechanisms. This is the only
place the safety core touches the outside world.

```csharp
/// Read-only window onto VCS state, for the precondition rules [H]. Caches within one CLI
/// invocation so a 3-rule chain that all ask IsCleanTree spawns `git status` at most once
/// (today RequireGitRepo re-spawns `git rev-parse` ~25× per run).
interface IRepoProbe
{
    bool IsGitRepo   { get; }   // git rev-parse --git-dir
    bool IsCleanTree { get; }   // git status --porcelain
    bool IsHeadPushed { get; }  // git rev-parse HEAD == origin/<branch>
}

/// Read-only window onto the workspace filesystem, for path containment [G]. Resolution and
/// the project-root boundary are decided HERE, so the security decision is a pure comparison
/// of strings the port hands back — the rule never calls Path.GetFullPath or GetCurrentDirectory.
interface IWorkspace
{
    string ProjectRoot { get; }                  // absolute, canonical
    string Resolve(string path);                 // relative → canonical absolute, against root
    bool   IsWithinProject(string canonicalPath); // root or descendant
}
```

`Ports` grows from `(IExecutor, IRenderer)` to `(IExecutor, IRenderer, IRepoProbe, IWorkspace)`.
The handler signature `Func<Ports, string[], int>` is **unchanged**, so issue #2's staged
handler migration is unaffected.

### Enforcement at the single dispatch site (`Program.cs`)

`CommandDefinition` gains a `Policy Policy` field (default `Policy.Default`), declared beside
`SafetyLevel` at registration. The dispatcher evaluates it before the handler:

```csharp
var ctx = new SafetyContext($"{group} {command} {string.Join(' ', commandArgs)}".TrimEnd(),
                            ports.Repo, ports.Workspace);
var decision = cmd.Policy.Evaluate(commandArgs, ctx);
if (decision.IsBlocked)
{
    ports.Render.Blocked(ctx.CommandLabel, decision.Block!.Reason, decision.Block.Suggestion);
    return 1;                                 // exit code, no-spawn, envelope: all automatic & central
}
return cmd.Handler(ports, decision.SafeArgs!); // SafeArgs == commandArgs unless a Rewrite rule fired
```

This five-line switch replaces every per-handler `if (...) { WriteBlocked(...); return 1; }`,
makes the Blocked envelope (incl. `--json`) uniform, and makes "blocked never spawns the
tool" a **structural** property of dispatch — handlers physically cannot forget to validate.

### Usage — before/after

**(a) git push, block `--force` [A].** Today: a field + 13-line handler (`GitCommands.cs:11,323-339`)
that misses `--force=true`. After:

```csharp
new("git", "push", "Push to remote (--force blocked)", "safe git push [...]",
    SafetyLevel.CheckedWrite, RunPush,
    Policy.Default
        .RequireGitRepo()
        .BlockFlags(["--force", "-f", "--delete", "--no-verify"],
            reason: "Force push and delete are not allowed",
            suggestion: "safe git push (without --force)"));

internal static int RunPush(Ports p, string[] args) => Run.Tool(p, "git", ["push", .. args]);
```

`--force=true` is now caught (central `Flag.Base` normalization).

**(b) npm "allow only scripts" [C]** (collapses the 3× duplicated allowlist; `process kill-name`
[D] uses the same rule with `noun: "Process"`):

```csharp
new("npm", "run", "Run package script", "safe npm run <script>", SafetyLevel.SafeWrite, RunScript,
    Policy.Default.AllowOnlyFirstArg(AllowedScripts, noun: "Script"));

internal static int RunScript(Ports p, string[] args)
{
    if (args.Length == 0) { p.Render.Error("Usage: safe npm run <script>"); return 1; } // usage stays in handler
    return Run.Tool(p, "npm", ["run", .. args]);
}
```

**(c) file read, path containment [G]** (one rule, replacing `ValidatePath` + the
`GenerateCommands.RunHashFile` copy; decision no longer touches `System.IO` or reads ambient CWD):

```csharp
new("file", "read", "Read file content", "safe file read <path>", SafetyLevel.ReadOnly, RunRead,
    Policy.Default.RequirePathWithinProject(argIndex: 0));
```

**(d) git checkout, require clean tree [H], the probe case:**

```csharp
new("git", "checkout", "Switch branch (requires clean tree)", "safe git checkout <branch>",
    SafetyLevel.CheckedWrite, RunCheckout,
    Policy.Default
        .RequireGitRepo()
        .BlockFlags(["."], "Discarding all changes is not allowed",
                    "safe git checkout-file <specific-file> to restore individual files")
        .RequireCleanTree());   // reads ctx.Repo.IsCleanTree — no ProcessRunner in the handler
```

**(e) proxy [F], the residual ~30%.** `AllowSubcommands` finally enforces the per-subcommand
`AllowedFlags`. The `CommandExists` check and curl method-block compose from `BlockFlags` +
a small bespoke check (proxy is genuinely two-dimensional; it doesn't get a one-liner, and
that honesty is fine).

## Dependency Strategy

**Category: ports & adapters** (issue #2 category 3 — "remote but owned" generalized to the
local OS boundary). The safety verdict is a pure function of `(args, IRepoProbe, IWorkspace)`.

- **`IRepoProbe` [H]** — production `GitRepoProbe(IExecutor)` wraps the existing executor and
  caches each answer (one git spawn per question per run). Tests use `FakeRepoProbe`
  (`{ IsGitRepo, IsCleanTree, IsHeadPushed }` settable booleans).
- **`IWorkspace` [G]** — production `FileSystemWorkspace` is the only place
  `Directory.GetCurrentDirectory()` / `Path.GetFullPath` are read; it owns the
  trailing-separator boundary trick that defends `/proj` vs `/projEvil`. Tests use
  `FakeWorkspace(root, within: …)` — no real disk.
- **Rendering** stays behind the existing `IRenderer.Blocked` (called centrally at dispatch).
- **The probe never reaches a pure rule's constructor** — it arrives via `SafetyContext`, so
  `BlockFlags` et al. simply ignore it. No `IExecutor` in the pure rule object graph.

This decouples the safety consolidation from issue #2's handler-signature migration: a
`Policy` is data evaluated at dispatch, so rules can be consolidated and tested **without
first porting any handler** to the `Ports` signature.

## Testing Strategy

- **New boundary tests (the payoff):** a table-driven suite over `Policy.Evaluate(args, ctx)`
  with `FakeRepoProbe`/`FakeWorkspace` and zero processes/filesystem. Must cover:
  - force-push incl. **`--force=true`**; db `--accept-data-loss`/`--force-reset`; commit
    `--no-verify`/`--amend`; compose `-v`; git add `-A`/`.`
  - substring blocks (artisan `fresh`/`reset`/`wipe`; django `zero`) incl. false-positive
    guard (a branch literally named `reset`)
  - first-arg allowlists (npm/pnpm/bun scripts; kill-names) incl. the truncation/ellipsis
    contract already pinned by `PolicyTests`
  - `AllowOnlyFlags` **rewrite** drops unknown flags and keeps declared value-flags
  - proxy subcommand match **and** per-subcommand flag enforcement (the formerly dead path)
  - path containment: `../../etc/passwd`, absolute-outside-root, `/proj` vs `/projEvil`, the
    nested-safe-dirs case from commit `f63c5a3` (currently no regression test)
  - clean-tree dirty vs clean; not-a-repo; head-already-pushed
- **New dispatch tests:** with `FakeRenderer`/`FakeExecutor`, assert blocked input ⇒
  `Render.Blocked` called, `Handler`/executor **never invoked**, exit 1 — and the same under
  `--json` emits the structured envelope (closing the fork). This is bun's "blocked never
  spawns" assertion, generalized to every group.
- **Old tests:** migrate `PolicyTests` (`AllowOnlyScriptsRule` → `AllowOnlyFirstArg`). Keep
  `RendererEnvelopeTests` (still the renderer's contract). Keep `BunCommandsTests` but its
  policy-block assertion becomes redundant once enforcement is central — simplify, don't
  delete prematurely.
- **Test environment needs:** two new fakes, `FakeRepoProbe` and `FakeWorkspace`, siblings of
  the existing `FakeExecutor`/`FakeRenderer`. No real git repo, no temp directories required.

## Implementation Recommendations

Durable guidance, not coupled to current file paths:

- **The Safety module OWNS:** the catalogue of safety rule kinds; `--flag=value`
  normalization (once); the consistent `Blocked` envelope (granularity + suggestion
  truncation); the chain fold/short-circuit; the allow/block/rewrite verdict.
- **It HIDES:** how each rule matches; how environment facts are obtained (behind
  `IRepoProbe`/`IWorkspace`); how the executor and filesystem are reached.
- **It EXPOSES:** a small fluent `Policy` vocabulary + one `Evaluate` returning a two-state
  `PolicyDecision`; two narrow domain ports.
- **Boundary with usage:** usage validation (missing required arg → `Error`) stays in
  handlers. The Policy owns *safety* only (block/allow/rewrite). Keep them distinct.
- **Migration path (each step independently shippable, independent of issue #2):**
  1. Add `IRepoProbe`/`IWorkspace` + adapters + fakes; grow `Ports`.
  2. Add `Policy`/`Rule`/`PolicyResult`/`Flag.Base`/`PolicyDecision`; add the `Policy` field to
     `CommandDefinition` (default `Policy.Default`) and the dispatch-site enforcement —
     **no behavior change** while every command still defaults to `Policy.Default`.
  3. Move each handler's inline validation into a declared `Policy`, deleting the inline checks
     and the now-unreachable `OutputFormatter.WriteBlocked` calls.
  4. Collapse the 3× script allowlist, the 2× `FilterFlags`, and the 2× path-containment into
     shared rules; enforce the proxy `AllowedFlags`.
- **STRIDE.md:** update after implementation — E1 (file write outside project dir) becomes an
  owned, tested rule; the proxy flag-enforcement closes a validation gap. (See the
  "When to update STRIDE.md" checklist in `CLAUDE.md`.)
- **Possible later extension (not now, per Simplicity First):** a `Warn` verdict that proceeds
  but emits a warning would absorb the npm/bun install supply-chain warnings; defer until a
  second caller needs it. Skip rule combinators (`And`/`Or`/`Not`) and `RequireConfirmation` —
  no current caller, speculative.
