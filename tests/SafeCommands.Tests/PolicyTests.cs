using SafeCommands.Infrastructure.Ports;
using SafeCommands.Safety;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Migrated from the OLD policy API (Evaluate(string[]) -> PolicyResult, AllowOnlyScripts) to
/// the new one: Evaluate(args, ctx) -> PolicyDecision, AllowOnlyFirstArg(allowed, noun).
/// Every behavioral assertion of the original suite is preserved.
/// </summary>
public class PolicyTests
{
    private static readonly string[] Allowed = ["build", "test", "lint"];

    private static SafetyContext Ctx(IRepoProbe? repo = null, IWorkspace? ws = null)
        => new("label", repo ?? new FakeRepoProbe(), ws ?? new FakeWorkspace());

    [Fact]
    public void Default_Allows_AllArgs()
    {
        var decision = Policy.Default.Evaluate(["anything", "--force"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Null(decision.Block);
    }

    [Fact]
    public void AllowOnlyFirstArg_AllowsKnownScript()
    {
        var policy = Policy.Default.AllowOnlyFirstArg(Allowed, "Script");
        Assert.False(policy.Evaluate(["build"], Ctx()).IsBlocked);
    }

    [Fact]
    public void AllowOnlyFirstArg_IsCaseInsensitive()
    {
        var policy = Policy.Default.AllowOnlyFirstArg(Allowed, "Script");
        Assert.False(policy.Evaluate(["BUILD"], Ctx()).IsBlocked);
        Assert.False(policy.Evaluate(["Test"], Ctx()).IsBlocked);
    }

    [Fact]
    public void AllowOnlyFirstArg_BlocksUnknownScript()
    {
        var policy = Policy.Default.AllowOnlyFirstArg(Allowed, "Script");
        var block = policy.Evaluate(["nonsense"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Contains("nonsense", block.Reason);
        Assert.Contains("not in the allowed list", block.Reason);
    }

    [Fact]
    public void AllowOnlyFirstArg_SuggestionIncludesAllowedList()
    {
        var policy = Policy.Default.AllowOnlyFirstArg(Allowed, "Script");
        var block = policy.Evaluate(["nope"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Contains("build", block.Suggestion);
        Assert.Contains("test", block.Suggestion);
    }

    [Fact]
    public void AllowOnlyFirstArg_ShortList_DoesNotAppendTruncationEllipsis()
    {
        // Allowed has 3 entries — well under the 15-item display cap. The ellipsis would
        // mislead users into thinking more scripts exist than they do.
        var policy = Policy.Default.AllowOnlyFirstArg(Allowed, "Script");
        var block = policy.Evaluate(["nope"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.DoesNotContain("...", block.Suggestion);
    }

    [Fact]
    public void AllowOnlyFirstArg_LongList_AppendsTruncationEllipsis()
    {
        // 20 entries > 15-item display cap; the ellipsis signals "more exist".
        var many = Enumerable.Range(0, 20).Select(i => $"script{i}").ToArray();
        var policy = Policy.Default.AllowOnlyFirstArg(many, "Script");
        var block = policy.Evaluate(["nope"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.EndsWith("...", block.Suggestion);
    }

    [Fact]
    public void AllowOnlyFirstArg_EmptyArgs_Allows()
    {
        // Handler-level error checks (e.g. "Usage: ...") run before policy; policy treats
        // empty args as Allow and lets the underlying tool decide.
        var policy = Policy.Default.AllowOnlyFirstArg(Allowed, "Script");
        Assert.False(policy.Evaluate([], Ctx()).IsBlocked);
    }

    [Theory]
    [InlineData("Script")]
    [InlineData("Process")]
    public void AllowOnlyFirstArg_NounAppearsInReason(string noun)
    {
        var policy = Policy.Default.AllowOnlyFirstArg(Allowed, noun);
        var block = policy.Evaluate(["nope"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal($"{noun} 'nope' is not in the allowed list", block.Reason);
    }

    [Fact]
    public void Evaluate_ShortCircuitsOnFirstBlock()
    {
        // The first Block wins: chaining a second rule that would also block does not change
        // the reason. Here the first rule blocks "build" (allowed list is ["only"]); a second
        // rule with a different noun must NOT be the one that produced the verdict.
        var policy = Policy.Default
            .AllowOnlyFirstArg(["only"], "First")
            .AllowOnlyFirstArg(["nothing"], "Second");
        var block = policy.Evaluate(["build"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.StartsWith("First ", block.Reason);
    }
}
