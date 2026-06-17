using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Safety;

/// <summary>
/// Verdict of a single <see cref="Rule"/>. A rule may allow, block, or REWRITE the arg vector
/// (the flag-allowlist case — dropping unknown flags is itself the safety guarantee).
/// </summary>
abstract record PolicyResult
{
    public sealed record Allow : PolicyResult;
    public sealed record Block(string Reason, string? Suggestion = null) : PolicyResult;
    public sealed record Rewrite(string[] Args) : PolicyResult;
}

/// <summary>
/// Everything a rule may read. Pure rules touch only <c>args</c>; probe/path rules read the
/// ports. One context hosts both, so the <see cref="Rule"/> contract never forces a probe
/// dependency on a rule that does not use one.
/// </summary>
readonly record struct SafetyContext(string CommandLabel, IRepoProbe Repo, IWorkspace Workspace);

/// <summary>A single safety check over an arg vector.</summary>
abstract record Rule
{
    public abstract PolicyResult Evaluate(string[] args, in SafetyContext ctx);
}

/// <summary>One allowed subcommand: a (possibly multi-token) prefix and the flags permitted under it.</summary>
readonly record struct Subcommand(string Prefix, IReadOnlyCollection<string> AllowedFlags);

/// <summary>The two-state answer the dispatcher acts on — never the raw <see cref="PolicyResult"/>.</summary>
readonly record struct PolicyDecision(string[]? SafeArgs, PolicyResult.Block? Block)
{
    public bool IsBlocked => Block is not null;
}

/// <summary>
/// An ordered chain of <see cref="Rule"/>s — the safety contract for one command. Built
/// declaratively via the fluent builders and attached to a CommandDefinition.
/// <see cref="Evaluate"/> folds the chain, short-circuiting on the first
/// <see cref="PolicyResult.Block"/> and threading any <see cref="PolicyResult.Rewrite"/>.
/// </summary>
sealed record Policy(IReadOnlyList<Rule> Rules)
{
    public static Policy Default { get; } = new([]);

    public Policy BlockFlags(IReadOnlyCollection<string> flags, string reason, string suggestion)
        => this with { Rules = [.. Rules, new BlockFlagsRule(flags, reason, suggestion)] };

    public Policy BlockSubstrings(IReadOnlyCollection<string> needles, string reason, string suggestion)
        => this with { Rules = [.. Rules, new BlockSubstringsRule(needles, reason, suggestion)] };

    public Policy AllowOnlyFirstArg(IReadOnlyCollection<string> allowed, string noun)
        => this with { Rules = [.. Rules, new AllowOnlyFirstArgRule(allowed, noun)] };

    public Policy AllowOnlyFlags(IReadOnlyCollection<string> allowedFlags, IReadOnlyCollection<string> valueFlags, bool keepPositionals = true)
        => this with { Rules = [.. Rules, new AllowOnlyFlagsRule(allowedFlags, valueFlags, keepPositionals)] };

    public Policy AllowSubcommands(IReadOnlyList<Subcommand> subcommands)
        => this with { Rules = [.. Rules, new AllowSubcommandsRule(subcommands)] };

    /// <summary>Convenience for the common case: the <paramref name="argIndex"/>-th POSITIONAL
    /// argument (flags and value-flag values are skipped — see <see cref="PathArg.Positional"/>),
    /// NOT the raw arg-vector index. Paths behind a flag use the <see cref="PathArg"/> overload.</summary>
    public Policy RequirePathWithinProject(int argIndex = 0)
        => RequirePathWithinProject(new PathArg.Positional(argIndex, []));

    public Policy RequirePathWithinProject(PathArg target)
        => this with { Rules = [.. Rules, new RequirePathWithinProjectRule(target)] };

    public Policy RequireWithinSafeDeleteDir(PathArg target, IReadOnlyCollection<string> safeDirs)
        => this with { Rules = [.. Rules, new RequireWithinSafeDeleteDirRule(target, safeDirs)] };

    public Policy RequireGitRepo()
        => this with { Rules = [.. Rules, new RequireGitRepoRule()] };

    public Policy RequireCleanTree(IReadOnlyCollection<string>? exemptFlags = null)
        => this with { Rules = [.. Rules, new RequireCleanTreeRule(exemptFlags ?? [])] };

    public Policy RequireHeadNotPushed()
        => this with { Rules = [.. Rules, new RequireHeadNotPushedRule()] };

    public Policy Custom(Rule rule)
        => this with { Rules = [.. Rules, rule] };

    public PolicyDecision Evaluate(string[] args, in SafetyContext ctx)
    {
        var current = args;
        foreach (var rule in Rules)
        {
            switch (rule.Evaluate(current, ctx))
            {
                case PolicyResult.Block b:
                    return new PolicyDecision(null, b);
                case PolicyResult.Rewrite r:
                    current = r.Args;
                    break;
                // Allow: continue with same args.
            }
        }
        return new PolicyDecision(current, null);
    }
}
