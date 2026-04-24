# Ubiquitous Language

Terminology for the SafeCommands domain. Source: `specs/PRD.md`, `CLAUDE.md`, and the current command registry.

## Actors

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Agent** | An AI coding tool (Claude Code, Cursor, Copilot, etc.) that invokes `safe` as a subprocess. | AI, bot, assistant, client |
| **Operator** | The human whose machine the **Agent** runs on and whose work the **Safety Guarantees** protect. | User, developer, owner |
| **Underlying tool** | An external CLI that **SafeCommands** wraps or forwards to (`git`, `docker`, `gh`, …). | Real CLI, wrapped binary, backend |

## Product

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **SafeCommands** | The .NET CLI product that provides a pre-validated command surface for **Agents**. | `safe`-tool, the gateway |
| **`safe`** | The binary name of **SafeCommands** on disk, installed as a dotnet global tool. | safecmd, sc |
| **Command Gateway** | The role **SafeCommands** plays: a single allowlisted entry point through which an **Agent** reaches the shell. | Wrapper, proxy (this term has a narrower meaning — see **Proxy Command**) |

## Command surface

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Command Group** | A top-level namespace of related **Commands** (`git`, `file`, `docker`, `npm`, `dotnet`, `db`, `process`, `env`, `generate`, `proxy`, `meta`). | Category, module, namespace |
| **Command** | A single invocation pattern the **Agent** can run, e.g. `safe git status`. Uniquely identified by (**Command Group**, name). | Subcommand, action, verb |
| **Command Definition** | The registered, immutable description of a **Command** held by the **Command Registry** — name, args shape, **Safety Level**, **Safety Rules**. | Entry, spec |
| **Command Registry** | The immutable, built-in collection of all **Command Definitions**, compiled into the binary. | Registry, catalog |
| **Built-in Allowlist** | The **Command Registry** considered as the authoritative set of permitted operations. Cannot be mutated at runtime. | Whitelist |
| **Extension Config** | The operator-managed `~/.safecommands/config.json` that adds custom **Proxy Commands**, blocked commands, or extra safe dirs. Gated by **Trust Verification**. | User config, plugin config |
| **Proxy Command** | A **Command** that forwards a validated argument list to an **Underlying tool** not covered by a first-class **Command Group** (`safe proxy gh pr list`). | Passthrough, forward |

## Safety model

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Safety Level** | The classification of a **Command**'s effect: **Read-Only**, **Safe Write**, or **Targeted Write**. | Tier, risk class |
| **Read-Only** | A **Safety Level** where the **Command** only reads state. | Pure, query |
| **Safe Write** | A **Safety Level** where the **Command** mutates state but only in additive, recoverable ways. | Benign write, soft write |
| **Targeted Write** | A **Safety Level** where the **Command** writes in a bounded, named-target way (e.g. supply-chain installs) and ships with explicit warnings. | Scoped write |
| **Safety Rule** | A per-**Command** validation applied to arguments before execution (e.g. "working tree must be clean", "no `--force`"). | Check, guard, constraint |
| **Safety Guarantee** | A promise **SafeCommands** makes to the **Operator** about what the **Command Registry** as a whole will never do (no force push, no untracked deletion, etc.). | Invariant, property |
| **Blocked Operation** | An **Underlying tool** invocation explicitly rejected even when the tool is otherwise reachable (e.g. `git reset --hard`, `docker compose down -v`). | Denied command, forbidden op |
| **Blocked Flag** | A specific flag rejected by a **Safety Rule** (`--force`, `--force-reset`, `--accept-data-loss`, `-A`). | Banned flag |
| **Safe Directory** | A path listed as a permitted deletion target for bulk file ops (`bin/`, `obj/`, `node_modules/`, `.tmp/`, …). | Temp dir, allowed dir |
| **Clean Working Tree** | A git precondition, enforced by **Safety Rules** on checkout/merge/pull, meaning no staged or unstaged changes. | Clean tree, no dirty state |

## Execution

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **ProcessRunner** | The infrastructure component that launches **Underlying tools** via `Process.Start` with `ArgumentList`, without shell interpretation. | Executor, shell, runner |
| **Output Mode** | Whether a **Command** prints **Human Output** or **JSON Output** (selected with `--json`). | Format |
| **Human Output** | The default, prose-formatted output intended for terminal readers. | Text, pretty |
| **JSON Output** | The machine-readable structured output produced when `--json` is passed. | Structured output |

## Configuration & trust

| Term | Definition | Aliases to avoid |
| ---- | ---------- | ---------------- |
| **Trust Verification** | The SHA-256-hash-based approval flow that gates changes to **Extension Config** before they take effect. | Signature check, approval |
| **Trust Store** | The persisted record of approved **Extension Config** hashes at `~/.safecommands/trust.json`. | Keychain, signatures file |

## Relationships

- A **Command Group** contains one or more **Command Definitions**.
- A **Command Definition** has exactly one **Safety Level** and zero or more **Safety Rules**.
- The **Command Registry** is the union of all **Command Definitions** and is immutable at runtime.
- An **Extension Config** may add **Proxy Commands** and block **Commands**, but only after **Trust Verification**.
- A **Proxy Command** targets exactly one **Underlying tool** with a validated argument list.
- The **ProcessRunner** executes a **Command** only after every **Safety Rule** on its **Command Definition** passes.
- A **Safety Guarantee** is upheld jointly by the **Command Registry** (what exists) and **Safety Rules** (what's allowed per invocation).

## Example dialogue

> **Dev:** "The **Agent** tried `safe git push --force` and got rejected. Where does that block live?"
>
> **Domain expert:** "In the **Safety Rules** attached to the push **Command Definition**. `--force` is a **Blocked Flag**; `--force-with-lease` passes. The **Command Registry** itself still lists `git push` — we don't remove the **Command**, we constrain its arguments."
>
> **Dev:** "So if I want to let it run `terraform plan`, I add a **Proxy Command** to the **Extension Config**?"
>
> **Domain expert:** "Right. After you edit `~/.safecommands/config.json`, the **Operator** has to approve the new hash through **Trust Verification** before the **ProcessRunner** will forward anything to terraform."
>
> **Dev:** "And `terraform destroy`?"
>
> **Domain expert:** "Still a **Blocked Operation** — the **Safety Guarantee** against unrecoverable infra changes is enforced by the proxy allowlist itself, not per-call. You'd have to add it explicitly as a custom **Proxy Command**, which is exactly the scenario **Trust Verification** is there to surface to the **Operator**."

## Flagged ambiguities

- **"Safe"** is overloaded. It names the product (`safe`), appears in a **Safety Level** (**Safe Write**), in **Safety Rules**, in **Safe Directory**, and as a general adjective. Always qualify: write "a **Safe Write** command" or "a **Safety Rule** blocks…", not "a safe command."
- **"Command"** is used at three levels: (1) a shell invocation the **Agent** types, (2) a **Command Definition** in the **Command Registry**, (3) an **Underlying tool** subcommand. Prefer **Command Definition** when talking about the registered entry and **Underlying tool** when talking about what `safe` shells out to.
- **"Proxy"** is used both for the broad **Command Gateway** role ("safe proxies to git") and for the narrow `safe proxy …` **Command Group**. Reserve **Proxy Command** for the latter; use **Command Gateway** or **wraps** for the former.
- **"Allowlist"** has two scopes: the **Built-in Allowlist** (immutable, compiled in) and the effective allowlist after **Extension Config** merges. When it matters, say **Built-in Allowlist** vs **Effective Allowlist**.
- **"User"** was avoided deliberately in favour of **Operator** (the human) vs **Agent** (the AI) — "user" in AI tooling contexts is ambiguous enough that we spell out which actor we mean.
- **"Group"** alone is ambiguous with .NET grouping concepts in code; prefer **Command Group** in prose.
