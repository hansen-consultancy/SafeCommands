using SafeCommands.Infrastructure.Ports;
using SafeCommands.Safety;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Exercises each concrete rule through the Policy fluent surface (never by constructing the
/// rule records directly). One context helper threads fakes for the probe/path rules.
/// </summary>
public class RuleTests
{
    private static SafetyContext Ctx(IRepoProbe? repo = null, IWorkspace? ws = null)
        => new("label", repo ?? new FakeRepoProbe(), ws ?? new FakeWorkspace());

    // ---------------------------------------------------------------- BlockFlags

    [Theory]
    [InlineData("--force")]
    [InlineData("-f")]
    [InlineData("--delete")]
    public void BlockFlags_BlocksListedFlag(string flag)
    {
        var policy = Policy.Default.BlockFlags(["--force", "-f", "--delete"], "no", "fix");
        Assert.True(policy.Evaluate([flag], Ctx()).IsBlocked);
    }

    [Fact]
    public void BlockFlags_BlocksForceEqualsTrue()
    {
        // Headline defect fix: "--force=true" normalizes to "--force" via Flag.Base and must block.
        var policy = Policy.Default.BlockFlags(["--force"], "no", "fix");
        Assert.True(policy.Evaluate(["--force=true"], Ctx()).IsBlocked);
    }

    [Fact]
    public void BlockFlags_BlocksExactDotToken()
    {
        var policy = Policy.Default.BlockFlags(["."], "no", "fix");
        Assert.True(policy.Evaluate(["."], Ctx()).IsBlocked);
    }

    [Fact]
    public void BlockFlags_AllowsCleanVector()
    {
        var policy = Policy.Default.BlockFlags(["--force"], "no", "fix");
        var decision = policy.Evaluate(["status", "--short"], Ctx());
        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public void BlockFlags_ReasonAndSuggestionComeFromBuilder()
    {
        var policy = Policy.Default.BlockFlags(["--force"], "the reason", "the suggestion");
        var block = policy.Evaluate(["--force"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal("the reason", block.Reason);
        Assert.Equal("the suggestion", block.Suggestion);
    }

    // ------------------------------------------------------------ BlockSubstrings

    [Theory]
    [InlineData("fresh")]
    [InlineData("reset")]
    [InlineData("wipe")]
    [InlineData("RESET")]            // case-insensitive
    [InlineData("--hard-reset")]     // substring inside a larger token
    public void BlockSubstrings_BlocksContainingNeedle(string arg)
    {
        var policy = Policy.Default.BlockSubstrings(["fresh", "reset", "wipe"], "no", "fix");
        Assert.True(policy.Evaluate([arg], Ctx()).IsBlocked);
    }

    [Fact]
    public void BlockSubstrings_AllowsCleanVector()
    {
        var policy = Policy.Default.BlockSubstrings(["fresh", "reset", "wipe"], "no", "fix");
        Assert.False(policy.Evaluate(["status", "main"], Ctx()).IsBlocked);
    }

    // ----------------------------------------------------- AllowOnlyFlags (Rewrite)

    private static string[] Rewrite(Policy p, params string[] args)
    {
        var decision = p.Evaluate(args, Ctx());
        Assert.False(decision.IsBlocked);
        return decision.SafeArgs!;
    }

    [Fact]
    public void AllowOnlyFlags_DropsUnknownFlags()
    {
        var p = Policy.Default.AllowOnlyFlags(["--graph"], [], keepPositionals: true);
        Assert.Equal(new[] { "--graph" }, Rewrite(p, "--graph", "--force"));
    }

    [Fact]
    public void AllowOnlyFlags_KeepsAllowedFlags()
    {
        var p = Policy.Default.AllowOnlyFlags(["--graph", "--oneline"], [], keepPositionals: true);
        Assert.Equal(new[] { "--graph", "--oneline" }, Rewrite(p, "--graph", "--oneline"));
    }

    [Fact]
    public void AllowOnlyFlags_ValueFlag_KeepsFollowingValue()
    {
        var p = Policy.Default.AllowOnlyFlags(["--format"], ["--format"], keepPositionals: true);
        Assert.Equal(new[] { "--format", "oneline" }, Rewrite(p, "--format", "oneline"));
    }

    [Fact]
    public void AllowOnlyFlags_EqualsForm_KeptAsSingleToken()
    {
        var p = Policy.Default.AllowOnlyFlags(["--format"], ["--format"], keepPositionals: true);
        Assert.Equal(new[] { "--format=oneline" }, Rewrite(p, "--format=oneline"));
    }

    [Fact]
    public void AllowOnlyFlags_KeepPositionalsFalse_DropsPositionals()
    {
        var p = Policy.Default.AllowOnlyFlags(["--graph"], [], keepPositionals: false);
        Assert.Equal(new[] { "--graph" }, Rewrite(p, "main", "--graph", "HEAD"));
    }

    [Fact]
    public void AllowOnlyFlags_KeepPositionalsTrue_KeepsPositionals()
    {
        var p = Policy.Default.AllowOnlyFlags(["--graph"], [], keepPositionals: true);
        Assert.Equal(new[] { "main", "--graph", "HEAD" }, Rewrite(p, "main", "--graph", "HEAD"));
    }

    // ---------------------------------------------------------- AllowSubcommands

    private static readonly IReadOnlyList<Subcommand> ProxySubs =
    [
        new("status", []),
        new("pr list", ["--state", "--limit"]),
    ];

    [Fact]
    public void AllowSubcommands_SingleTokenPrefix_Allows()
    {
        var policy = Policy.Default.AllowSubcommands(ProxySubs);
        Assert.False(policy.Evaluate(["status"], Ctx()).IsBlocked);
    }

    [Fact]
    public void AllowSubcommands_MultiTokenPrefix_Allows()
    {
        var policy = Policy.Default.AllowSubcommands(ProxySubs);
        Assert.False(policy.Evaluate(["pr", "list"], Ctx()).IsBlocked);
    }

    [Fact]
    public void AllowSubcommands_FlagNotInMatchedSubcommand_IsBlocked()
    {
        // Formerly-dead proxy enforcement: under "pr list" only --state/--limit are allowed.
        var policy = Policy.Default.AllowSubcommands(ProxySubs);
        var block = policy.Evaluate(["pr", "list", "--force"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Contains("not allowed for this subcommand", block.Reason);
    }

    [Fact]
    public void AllowSubcommands_AllowedFlagUnderSubcommand_Passes()
    {
        var policy = Policy.Default.AllowSubcommands(ProxySubs);
        Assert.False(policy.Evaluate(["pr", "list", "--state", "open"], Ctx()).IsBlocked);
    }

    [Fact]
    public void AllowSubcommands_UnmatchedVector_IsBlocked()
    {
        var policy = Policy.Default.AllowSubcommands(ProxySubs);
        var block = policy.Evaluate(["nuke", "everything"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal("Subcommand is not allowed", block.Reason);
    }

    [Fact]
    public void AllowSubcommands_SingleTokenPrefix_DoesNotOverMatchLongerToken()
    {
        // Token-boundary match: prefix "status" must NOT accept ["status-quo"].
        var policy = Policy.Default.AllowSubcommands(ProxySubs);
        var block = policy.Evaluate(["status-quo"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal("Subcommand is not allowed", block.Reason);
    }

    [Fact]
    public void AllowSubcommands_MultiTokenPrefix_DoesNotOverMatchLongerToken()
    {
        // Prefix "pr list" must NOT accept ["pr", "listicle"].
        var policy = Policy.Default.AllowSubcommands(ProxySubs);
        var block = policy.Evaluate(["pr", "listicle"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal("Subcommand is not allowed", block.Reason);
    }

    [Fact]
    public void AllowSubcommands_EmptyPrefix_MatchesAnyVector()
    {
        // An empty prefix is the catch-all; only its flag allowlist then applies.
        var policy = Policy.Default.AllowSubcommands([new("", ["--verbose"])]);
        Assert.False(policy.Evaluate(["anything", "--verbose"], Ctx()).IsBlocked);
        Assert.True(policy.Evaluate(["anything", "--force"], Ctx()).IsBlocked);
    }

    // ----------------------------------------------- RequirePathWithinProject

    [Fact]
    public void RequirePathWithinProject_InsideRoot_Allows()
    {
        var ws = new FakeWorkspace { ProjectRoot = "/proj" };
        var policy = Policy.Default.RequirePathWithinProject();
        Assert.False(policy.Evaluate(["/proj/src/x.cs"], Ctx(ws: ws)).IsBlocked);
    }

    [Fact]
    public void RequirePathWithinProject_OutsideRoot_Blocks()
    {
        var ws = new FakeWorkspace { ProjectRoot = "/proj" };
        var policy = Policy.Default.RequirePathWithinProject();
        var block = policy.Evaluate(["/etc/passwd"], Ctx(ws: ws)).Block;
        Assert.NotNull(block);
        Assert.Contains("is outside the project directory", block.Reason);
    }

    [Fact]
    public void RequirePathWithinProject_ArgIndexBeyondArgs_Allows()
    {
        var ws = new FakeWorkspace { ProjectRoot = "/proj" };
        var policy = Policy.Default.RequirePathWithinProject(argIndex: 5);
        Assert.False(policy.Evaluate(["/etc/passwd"], Ctx(ws: ws)).IsBlocked);
    }

    // ---------------------------------------------- Require* git-state rules

    [Fact]
    public void RequireGitRepo_DefaultState_Allows()
    {
        var policy = Policy.Default.RequireGitRepo();
        Assert.False(policy.Evaluate([], Ctx(repo: new FakeRepoProbe())).IsBlocked);
    }

    [Fact]
    public void RequireGitRepo_NotARepo_Blocks()
    {
        var policy = Policy.Default.RequireGitRepo();
        var block = policy.Evaluate([], Ctx(repo: new FakeRepoProbe { IsGitRepo = false })).Block;
        Assert.NotNull(block);
        Assert.Equal("Not a git repository", block.Reason);
        Assert.Null(block.Suggestion);
    }

    [Fact]
    public void RequireCleanTree_DefaultState_Allows()
    {
        var policy = Policy.Default.RequireCleanTree();
        Assert.False(policy.Evaluate([], Ctx(repo: new FakeRepoProbe())).IsBlocked);
    }

    [Fact]
    public void RequireCleanTree_DirtyTree_Blocks()
    {
        var policy = Policy.Default.RequireCleanTree();
        var block = policy.Evaluate([], Ctx(repo: new FakeRepoProbe { IsCleanTree = false })).Block;
        Assert.NotNull(block);
        Assert.Equal("Working tree has uncommitted changes", block.Reason);
        Assert.Contains("stash", block.Suggestion);
    }

    [Fact]
    public void RequireHeadNotPushed_DefaultState_Allows()
    {
        var policy = Policy.Default.RequireHeadNotPushed();
        Assert.False(policy.Evaluate([], Ctx(repo: new FakeRepoProbe())).IsBlocked);
    }

    [Fact]
    public void RequireHeadNotPushed_HeadPushed_Blocks()
    {
        var policy = Policy.Default.RequireHeadNotPushed();
        var block = policy.Evaluate([], Ctx(repo: new FakeRepoProbe { IsHeadPushed = true })).Block;
        Assert.NotNull(block);
        Assert.Contains("already been pushed", block.Reason);
        Assert.Contains("force push", block.Reason);
        Assert.Contains("Create a new commit", block.Suggestion);
    }

    // ------------------------------------------------------------- Policy fold

    [Fact]
    public void Fold_FirstBlockWins()
    {
        // Two blocking rules; the FIRST block must be the verdict — the second never runs.
        var policy = Policy.Default
            .BlockFlags(["--force"], "first reason", "first fix")
            .BlockFlags(["--force"], "second reason", "second fix");
        var block = policy.Evaluate(["--force"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal("first reason", block.Reason);
    }

    [Fact]
    public void Fold_RewriteThreadsToNextRule()
    {
        // AllowOnlyFlags drops --force; the following BlockFlags sees the REWRITTEN vector
        // (no --force) and therefore does NOT block — proving the rewrite is threaded forward.
        var policy = Policy.Default
            .AllowOnlyFlags(["--graph"], [], keepPositionals: true)
            .BlockFlags(["--force"], "no force", "drop it");
        var decision = policy.Evaluate(["--graph", "--force"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "--graph" }, decision.SafeArgs);
    }

    [Fact]
    public void Fold_FullPass_ReturnsRewrittenSafeArgs()
    {
        var policy = Policy.Default
            .AllowOnlyFlags(["--graph"], [], keepPositionals: true);
        var decision = policy.Evaluate(["main", "--graph", "--force"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "main", "--graph" }, decision.SafeArgs);
    }
}
