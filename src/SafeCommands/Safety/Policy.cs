namespace SafeCommands.Safety;

/// <summary>
/// Pure-function safety policy: a chain of <see cref="Rule"/>s that validate command args.
/// <see cref="Evaluate"/> short-circuits on the first <see cref="PolicyResult.Block"/>.
/// </summary>
sealed record Policy(IReadOnlyList<Rule> Rules)
{
    public static Policy Default { get; } = new([]);

    public Policy AllowOnlyScripts(IReadOnlyCollection<string> allowed)
        => this with { Rules = [.. Rules, new AllowOnlyScriptsRule(allowed)] };

    /// <summary>
    /// Block if any arg matches one of <paramref name="flags"/> exactly (case-insensitive).
    /// Use for hard-deny lists like <c>--force</c>, <c>-f</c>, <c>--volumes</c>.
    /// </summary>
    public Policy DenyFlags(params string[] flags)
        => this with { Rules = [.. Rules, new DenyFlagsRule(flags)] };

    /// <summary>
    /// Block if any arg's lowercased text contains one of <paramref name="needles"/>.
    /// Looser than <see cref="DenyFlags"/>; reach for it only when a single token (e.g.
    /// <c>migrate:fresh</c>) embeds the deny term. Prefer exact <see cref="DenyFlags"/>
    /// where possible to avoid surprising matches like <c>--reset-only</c>.
    /// </summary>
    public Policy DenyArgsContaining(params string[] needles)
        => this with { Rules = [.. Rules, new DenyArgsContainingRule(needles)] };

    public PolicyResult Evaluate(string[] args)
    {
        foreach (var rule in Rules)
        {
            var result = rule.Evaluate(args);
            if (result is PolicyResult.Block) return result;
        }
        return new PolicyResult.Allow();
    }
}

abstract record Rule
{
    public abstract PolicyResult Evaluate(string[] args);
}

abstract record PolicyResult
{
    public sealed record Allow : PolicyResult;
    public sealed record Block(string Reason, string Suggestion) : PolicyResult;
}

sealed record AllowOnlyScriptsRule(IReadOnlyCollection<string> Allowed) : Rule
{
    public override PolicyResult Evaluate(string[] args)
    {
        if (args.Length == 0) return new PolicyResult.Allow();
        var script = args[0].ToLowerInvariant();
        if (Allowed.Contains(script)) return new PolicyResult.Allow();
        return new PolicyResult.Block(
            $"Script '{script}' is not in the allowed list",
            $"Allowed: {string.Join(", ", Allowed.Take(15))}{(Allowed.Count > 15 ? "..." : "")}");
    }
}

sealed record DenyFlagsRule(IReadOnlyCollection<string> Flags) : Rule
{
    private readonly HashSet<string> _deny = Flags.Select(f => f.ToLowerInvariant()).ToHashSet();

    public override PolicyResult Evaluate(string[] args)
    {
        foreach (var arg in args)
        {
            if (_deny.Contains(arg.ToLowerInvariant()))
            {
                return new PolicyResult.Block(
                    $"Flag '{arg}' is not allowed",
                    $"Remove '{arg}' and retry");
            }
        }
        return new PolicyResult.Allow();
    }
}

sealed record DenyArgsContainingRule(IReadOnlyCollection<string> Needles) : Rule
{
    private readonly string[] _loweredNeedles = Needles.Select(n => n.ToLowerInvariant()).ToArray();

    public override PolicyResult Evaluate(string[] args)
    {
        foreach (var arg in args)
        {
            var lower = arg.ToLowerInvariant();
            foreach (var needle in _loweredNeedles)
            {
                if (lower.Contains(needle))
                {
                    return new PolicyResult.Block(
                        $"Argument '{arg}' contains the disallowed term '{needle}'",
                        $"Remove '{arg}' and retry");
                }
            }
        }
        return new PolicyResult.Allow();
    }
}
