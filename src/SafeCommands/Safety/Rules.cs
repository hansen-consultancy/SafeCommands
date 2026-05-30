namespace SafeCommands.Safety;

/// <summary>
/// Describes where a command's path argument lives in the arg vector. Extraction returns null
/// when the path is absent — the rule then Allows (the handler's "." default, or its own usage
/// error, takes over). This defines the "no path given" case out of existence.
/// </summary>
abstract record PathArg
{
    public abstract string? Extract(string[] args);

    /// <summary>
    /// The <see cref="Index"/>-th POSITIONAL token (0-based), skipping flags (tokens starting
    /// with '-') and the VALUES of any flag named in <see cref="ValueFlags"/> (matched
    /// case-insensitively, mirroring the handlers' GetOption). Returns null if there is no such
    /// positional.
    /// </summary>
    public sealed record Positional(int Index, IReadOnlyCollection<string> ValueFlags) : PathArg
    {
        public override string? Extract(string[] args)
        {
            int seen = -1;
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (ValueFlags.Any(f => string.Equals(f, a, StringComparison.OrdinalIgnoreCase))) { i++; continue; } // skip flag + its value
                if (a.StartsWith('-')) continue;                                                                      // skip boolean flag
                if (++seen == Index) return a;
            }
            return null;
        }
    }

    /// <summary>
    /// The token immediately following <see cref="Flag"/> (ordinal match, mirroring the handlers'
    /// Array.IndexOf). Returns null if the flag is absent or is the last token.
    /// </summary>
    public sealed record FlagValue(string Flag) : PathArg
    {
        public override string? Extract(string[] args)
        {
            var i = Array.IndexOf(args, Flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}

/// <summary>Blocks if any arg, normalized via <see cref="Flag.Base"/>, is in the flag set.</summary>
sealed record BlockFlagsRule(IReadOnlyCollection<string> Flags, string Reason, string? Suggestion) : Rule
{
    private readonly HashSet<string> _flags = Flags.Select(f => f.ToLowerInvariant()).ToHashSet();

    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
    {
        foreach (var arg in args)
        {
            if (_flags.Contains(Flag.Base(arg)))
                return new PolicyResult.Block(Reason, Suggestion);
        }
        return new PolicyResult.Allow();
    }
}

/// <summary>Blocks if any arg contains any needle (case-insensitive).</summary>
sealed record BlockSubstringsRule(IReadOnlyCollection<string> Needles, string Reason, string Suggestion) : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
    {
        // Substring matching can false-positive on legitimate tokens (e.g. a branch named
        // "reset"); choosing this over exact-token matching is the caller's decision.
        foreach (var arg in args)
        {
            foreach (var needle in Needles)
            {
                if (arg.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return new PolicyResult.Block(Reason, Suggestion);
            }
        }
        return new PolicyResult.Allow();
    }
}

/// <summary>Allows only when the first arg is in the (lowercased) allowed set.</summary>
sealed record AllowOnlyFirstArgRule(IReadOnlyCollection<string> Allowed, string Noun) : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
    {
        if (args.Length == 0) return new PolicyResult.Allow();
        var first = args[0].ToLowerInvariant();
        if (Allowed.Contains(first)) return new PolicyResult.Allow();
        return new PolicyResult.Block(
            $"{Noun} '{first}' is not in the allowed list",
            $"Allowed: {string.Join(", ", Allowed.Take(15))}{(Allowed.Count > 15 ? "..." : "")}");
    }
}

/// <summary>
/// Rewrites the arg vector to keep only allowed flags (and the value of declared value-flags),
/// dropping unknown flags silently. Mirrors the legacy GitCommands.FilterFlags.
/// </summary>
sealed record AllowOnlyFlagsRule(IReadOnlyCollection<string> AllowedFlags, IReadOnlyCollection<string> ValueFlags, bool KeepPositionals) : Rule
{
    // Case-SENSITIVE by design (unlike BlockFlags/AllowSubcommands, which lowercase): git's real
    // flags are case-sensitive and the legacy FilterFlags never normalized case. Do not "fix"
    // this into a lowercasing match — it would change which flags survive the rewrite.
    private readonly HashSet<string> _allowed = AllowedFlags.ToHashSet();
    private readonly HashSet<string> _valueFlags = ValueFlags.ToHashSet();

    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
    {
        var result = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-'))
            {
                var flagBase = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
                if (_allowed.Contains(flagBase) || _allowed.Contains(arg))
                {
                    result.Add(arg);
                    if (!arg.Contains('=') && _valueFlags.Contains(flagBase) && i + 1 < args.Length)
                        result.Add(args[++i]);
                }
                // Skip unknown flags silently.
            }
            else if (KeepPositionals)
            {
                result.Add(arg);
            }
        }
        return new PolicyResult.Rewrite(result.ToArray());
    }
}

/// <summary>
/// Allows only args matching one of the declared subcommand prefixes; under the matched
/// subcommand, every flag must have its <see cref="Flag.Base"/> in that subcommand's allowed set.
/// </summary>
sealed record AllowSubcommandsRule(IReadOnlyList<Subcommand> Subcommands) : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
    {
        foreach (var sub in Subcommands)
        {
            if (!PrefixMatches(args, sub.Prefix, out var prefixTokens)) continue;

            var allowedFlags = sub.AllowedFlags.Select(f => f.ToLowerInvariant()).ToHashSet();
            for (int i = prefixTokens; i < args.Length; i++)
            {
                if (args[i].StartsWith('-') && !allowedFlags.Contains(Flag.Base(args[i])))
                    return new PolicyResult.Block(
                        $"Flag '{args[i]}' is not allowed for this subcommand",
                        $"Allowed flags: {string.Join(", ", sub.AllowedFlags)}");
            }
            return new PolicyResult.Allow();
        }
        return new PolicyResult.Block(
            "Subcommand is not allowed",
            $"Allowed: {string.Join(", ", Subcommands.Where(s => !string.IsNullOrEmpty(s.Prefix)).Select(s => s.Prefix))}");
    }

    // Token-boundary match: the leading args must equal the prefix's whitespace-split tokens
    // (case-insensitive). A plain string StartsWith would over-match — prefix "status" would
    // accept ["status-quo"], and "pr list" would accept ["pr", "listicle"]. An empty prefix
    // splits to zero tokens and matches any vector (catch-all).
    private static bool PrefixMatches(string[] args, string prefix, out int prefixTokens)
    {
        var tokens = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        prefixTokens = tokens.Length;
        if (args.Length < tokens.Length) return false;
        for (int i = 0; i < tokens.Length; i++)
            if (!string.Equals(args[i], tokens[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }
}

/// <summary>Requires the path selected by <see cref="Target"/> to resolve inside the project root.</summary>
sealed record RequirePathWithinProjectRule(PathArg Target) : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
    {
        var path = Target.Extract(args);
        if (path is null) return new PolicyResult.Allow();
        // Resolve may throw on a malformed path (illegal chars / null byte). That propagates to
        // the dispatcher's global try/catch and fails closed (the file op never runs), surfacing
        // as a generic "Command failed" rather than a Blocked envelope. Acceptable: safety holds.
        var resolved = ctx.Workspace.Resolve(path);
        if (ctx.Workspace.IsWithinProject(resolved)) return new PolicyResult.Allow();
        return new PolicyResult.Block(
            $"Path '{resolved}' is outside the project directory",
            $"All file operations are sandboxed to: {ctx.Workspace.ProjectRoot}");
    }
}

/// <summary>
/// Requires the directory selected by <see cref="Target"/> to be — or to descend from — a
/// directory whose segment name is in <see cref="SafeDirs"/> (matched case-insensitively),
/// strictly below the project root. Mirrors delete-pattern's ancestor walk: tmp/.dotnet is
/// allowed because its ancestor "tmp" is a safe dir.
/// </summary>
sealed record RequireWithinSafeDeleteDirRule(PathArg Target, IReadOnlyCollection<string> SafeDirs) : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
    {
        var dir = Target.Extract(args);
        if (dir is null) return new PolicyResult.Allow();           // handler usage-errors on missing --in
        var resolved = ctx.Workspace.Resolve(dir);
        var root = ctx.Workspace.ProjectRoot;
        // Segments strictly below the project root, up to and including the target's own name.
        // (When chained after RequirePathWithinProject, resolved is guaranteed within project.)
        if (ctx.Workspace.IsWithinProject(resolved) && resolved.Length > root.Length)
        {
            var rel = resolved[root.Length..];
            // Split on BOTH separators: FakeWorkspace uses '/' even on Windows; the real
            // FileSystemWorkspace uses OS separators.
            foreach (var seg in rel.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
                if (SafeDirs.Contains(seg.ToLowerInvariant())) return new PolicyResult.Allow();
        }
        return new PolicyResult.Block(
            $"'{dir}' is not inside a safe delete directory",
            $"Target (or an ancestor) must be one of: {string.Join(", ", SafeDirs.Take(10))}...");
    }
}

/// <summary>Requires the command to run inside a git repository.</summary>
sealed record RequireGitRepoRule : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
        => ctx.Repo.IsGitRepo ? new PolicyResult.Allow() : new PolicyResult.Block("Not a git repository", null);
}

/// <summary>Requires a clean working tree.</summary>
sealed record RequireCleanTreeRule : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
        => ctx.Repo.IsCleanTree
            ? new PolicyResult.Allow()
            : new PolicyResult.Block("Working tree has uncommitted changes", "Commit or stash your changes first: safe git stash");
}

/// <summary>Blocks when HEAD has already been pushed (amending would require a force push).</summary>
sealed record RequireHeadNotPushedRule : Rule
{
    public override PolicyResult Evaluate(string[] args, in SafetyContext ctx)
        => ctx.Repo.IsHeadPushed
            ? new PolicyResult.Block(
                "HEAD commit has already been pushed to remote - amending would require force push",
                "Create a new commit instead: safe git commit -m \"<message>\"")
            : new PolicyResult.Allow();
}
