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
    public void AllowOnlyScripts_EmptyArgs_Allows()
    {
        // Handler-level error checks (e.g. "Usage: ...") run before policy; policy treats
        // empty args as Allow and lets the underlying tool decide.
        var policy = Policy.Default.AllowOnlyScripts(Allowed);
        Assert.IsType<PolicyResult.Allow>(policy.Evaluate([]));
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
