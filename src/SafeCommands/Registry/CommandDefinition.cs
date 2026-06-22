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
    /// Safety contract evaluated at the dispatch site before the handler runs. Defaults to
    /// <see cref="Policy.Default"/> (no checks); commands set it via object-initializer syntax.
    /// </summary>
    public Policy Policy { get; init; } = Policy.Default;

    /// <summary>
    /// Minimum number of arguments the command requires. Enforced once at the dispatch site (after
    /// <see cref="Policy"/>, before the handler): too few args renders <c>Usage: {Usage}</c> and
    /// returns 1. Lets handlers assume <c>args.Length &gt;= MinArgs</c> instead of each repeating an
    /// inline "Usage:" guard. 0 (default) means no requirement. Genuinely command-specific contracts
    /// (a required <em>flag</em>, a value range) still live in the handler — this covers only the
    /// uniform positional-count check.
    /// </summary>
    public int MinArgs { get; init; }

    public string FullName => $"{Group} {Name}";

    public string SafetyLabel => Safety switch
    {
        SafetyLevel.ReadOnly => "read-only",
        SafetyLevel.SafeWrite => "safe-write",
        SafetyLevel.CheckedWrite => "checked-write",
        _ => "unknown"
    };
}
