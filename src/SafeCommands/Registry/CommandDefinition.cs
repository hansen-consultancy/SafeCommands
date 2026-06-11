using SafeCommands.Infrastructure.Ports;
using SafeCommands.Safety;

namespace SafeCommands.Registry;

/// <summary>
/// Classification of a Command Definition's effect on state.
/// Part of SafeCommands' safety model (see UBIQUITOUS_LANGUAGE.md).
/// </summary>
enum SafetyLevel
{
    /// <summary>Pure read - no side effects.</summary>
    ReadOnly,

    /// <summary>Additive or recoverable write (e.g. git add, mkdir, npm test).</summary>
    SafeWrite,

    /// <summary>
    /// Write gated by per-command pre-validation — e.g. requires a clean working
    /// tree, a specific named target, or rejects destructive flags like --force.
    /// Rendered to users as "checked-write".
    /// </summary>
    CheckedWrite,
}

/// <summary>
/// Registered, immutable description of a single Command in the Built-in Allowlist.
/// Uniquely identified by (<see cref="Group"/>, <see cref="Name"/>).
/// </summary>
record CommandDefinition(
    string Group,
    string Name,
    string Description,
    string Usage,
    SafetyLevel Safety,
    Func<Ports, string[], int> Handler  // (ports, args) => exitCode
)
{
    /// <summary>
    /// Migration shim: auto-adapts legacy handlers with the
    /// <c>(string[] args, bool jsonMode) =&gt; int</c> signature so unmigrated command
    /// groups need no source changes during the staged refactor (Issue #2).
    /// </summary>
    public CommandDefinition(
        string group,
        string name,
        string description,
        string usage,
        SafetyLevel safety,
        Func<string[], bool, int> legacyHandler)
        : this(group, name, description, usage, safety,
               (p, args) => legacyHandler(args, p.Render.JsonMode))
    { }

    /// <summary>
    /// Safety contract evaluated at the dispatch site before the handler runs. Defaults to
    /// <see cref="Policy.Default"/> (no checks); commands set it via object-initializer syntax.
    /// </summary>
    public Policy Policy { get; init; } = Policy.Default;

    public string FullName => $"{Group} {Name}";

    public string SafetyLabel => Safety switch
    {
        SafetyLevel.ReadOnly => "read-only",
        SafetyLevel.SafeWrite => "safe-write",
        SafetyLevel.CheckedWrite => "checked-write",
        _ => "unknown"
    };
}
