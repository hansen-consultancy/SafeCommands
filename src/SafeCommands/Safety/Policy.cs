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
