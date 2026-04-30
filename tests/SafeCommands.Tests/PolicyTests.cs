using SafeCommands.Safety;

namespace SafeCommands.Tests;

public class PolicyTests
{
    private static readonly string[] Allowed = ["build", "test", "lint"];

    [Fact]
    public void Default_Allows_AllArgs()
    {
        var result = Policy.Default.Evaluate(["anything", "--force"]);
        Assert.IsType<PolicyResult.Allow>(result);
    }

    [Fact]
    public void AllowOnlyScripts_AllowsKnownScript()
    {
        var policy = Policy.Default.AllowOnlyScripts(Allowed);
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate(["build"]));
    }

    [Fact]
    public void AllowOnlyScripts_IsCaseInsensitive()
    {
        var policy = Policy.Default.AllowOnlyScripts(Allowed);
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate(["BUILD"]));
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate(["Test"]));
    }

    [Fact]
    public void AllowOnlyScripts_BlocksUnknownScript()
    {
        var policy = Policy.Default.AllowOnlyScripts(Allowed);
        var result = policy.Evaluate(["nonsense"]);
        var block = Assert.IsType<PolicyResult.Block>(result);
        Assert.Contains("nonsense", block.Reason);
        Assert.Contains("not in the allowed list", block.Reason);
    }

    [Fact]
    public void AllowOnlyScripts_SuggestionIncludesAllowedList()
    {
        var policy = Policy.Default.AllowOnlyScripts(Allowed);
        var block = Assert.IsType<PolicyResult.Block>(policy.Evaluate(["nope"]));
        Assert.Contains("build", block.Suggestion);
        Assert.Contains("test", block.Suggestion);
    }

    [Fact]
    public void AllowOnlyScripts_ShortList_DoesNotAppendTruncationEllipsis()
    {
        // Allowed has 3 entries — well under the 15-item display cap. The ellipsis would
        // mislead users into thinking more scripts exist than they do.
        var policy = Policy.Default.AllowOnlyScripts(Allowed);
        var block = Assert.IsType<PolicyResult.Block>(policy.Evaluate(["nope"]));
        Assert.DoesNotContain("...", block.Suggestion);
    }

    [Fact]
    public void AllowOnlyScripts_LongList_AppendsTruncationEllipsis()
    {
        // 20 entries > 15-item display cap; the ellipsis signals "more exist".
        var many = Enumerable.Range(0, 20).Select(i => $"script{i}").ToArray();
        var policy = Policy.Default.AllowOnlyScripts(many);
        var block = Assert.IsType<PolicyResult.Block>(policy.Evaluate(["nope"]));
        Assert.EndsWith("...", block.Suggestion);
    }

    [Fact]
    public void AllowOnlyScripts_EmptyArgs_Allows()
    {
        // Handler-level error checks (e.g. "Usage: ...") run before policy; policy treats
        // empty args as Allow and lets the underlying tool decide.
        var policy = Policy.Default.AllowOnlyScripts(Allowed);
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate([]));
    }

    // ---- DenyFlags ----

    [Fact]
    public void DenyFlags_AllowsArgsWithoutMatchingFlag()
    {
        var policy = Policy.Default.DenyFlags("--force", "-f");
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate(["--name", "thing"]));
    }

    [Fact]
    public void DenyFlags_BlocksExactMatch()
    {
        var policy = Policy.Default.DenyFlags("--force", "-f");
        var block = Assert.IsType<PolicyResult.Block>(policy.Evaluate(["--name", "x", "--force"]));
        Assert.Contains("--force", block.Reason);
    }

    [Fact]
    public void DenyFlags_IsCaseInsensitive()
    {
        var policy = Policy.Default.DenyFlags("--force");
        Assert.IsType<PolicyResult.Block>(policy.Evaluate(["--FORCE"]));
    }

    [Fact]
    public void DenyFlags_DoesNotMatchSubstring()
    {
        // "--force" must not match "--force-with-lease" — exact-only semantics
        var policy = Policy.Default.DenyFlags("--force");
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate(["--force-with-lease"]));
    }

    [Fact]
    public void DenyFlags_EmptyArgs_Allows()
    {
        var policy = Policy.Default.DenyFlags("--force");
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate([]));
    }

    // ---- DenyArgsContaining ----

    [Fact]
    public void DenyArgsContaining_BlocksWhenSubstringMatches()
    {
        var policy = Policy.Default.DenyArgsContaining("fresh", "reset", "rollback", "wipe");
        var block = Assert.IsType<PolicyResult.Block>(policy.Evaluate(["migrate:fresh"]));
        Assert.Contains("fresh", block.Reason);
    }

    [Fact]
    public void DenyArgsContaining_IsCaseInsensitive()
    {
        var policy = Policy.Default.DenyArgsContaining("reset");
        Assert.IsType<PolicyResult.Block>(policy.Evaluate(["MIGRATE:RESET"]));
    }

    [Fact]
    public void DenyArgsContaining_AllowsCleanArgs()
    {
        var policy = Policy.Default.DenyArgsContaining("fresh", "reset");
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate(["--step", "1"]));
    }

    [Fact]
    public void Evaluate_ShortCircuitsOnFirstBlock()
    {
        // If we ever chain rules, the first Block should win. For now AllowOnlyScripts is
        // a single-rule policy; this test is a forward-looking guard.
        var policy = Policy.Default
            .AllowOnlyScripts(Allowed)
            .AllowOnlyScripts(["other"]);  // a second, conflicting rule
        var result = policy.Evaluate(["build"]);
        // First rule allows "build"; second rule blocks "build". Short-circuit logic only
        // halts on Block, not Allow, so the second rule should still fire and Block.
        Assert.IsType<PolicyResult.Block>(result);
    }
}
