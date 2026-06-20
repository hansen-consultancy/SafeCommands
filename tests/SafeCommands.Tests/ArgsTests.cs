using SafeCommands.Sugar;

namespace SafeCommands.Tests;

/// <summary>
/// The shared arg parser that replaced the per-handler Array.IndexOf / args.Contains idioms.
/// Flag names match case-insensitively; values come back verbatim.
/// </summary>
public class ArgsTests
{
    // ─────────────────────────────────────────────────────────────── HasFlag

    [Fact] public void HasFlag_Present() => Assert.True(Args.HasFlag(["a", "--verbose"], "--verbose"));
    [Fact] public void HasFlag_CaseInsensitive() => Assert.True(Args.HasFlag(["--VERBOSE"], "--verbose"));
    [Fact] public void HasFlag_Absent() => Assert.False(Args.HasFlag(["a", "b"], "--verbose"));
    [Fact] public void HasFlag_Empty() => Assert.False(Args.HasFlag([], "--verbose"));

    // ─────────────────────────────────────────────────────────────── Value

    [Fact] public void Value_ReturnsTokenAfterFlag() => Assert.Equal("5", Args.Value(["--lines", "5"], "--lines"));
    [Fact] public void Value_CaseInsensitiveFlag() => Assert.Equal("5", Args.Value(["--LINES", "5"], "--lines"));
    [Fact] public void Value_Absent_ReturnsNull() => Assert.Null(Args.Value(["a", "b"], "--lines"));
    [Fact] public void Value_FlagIsLastToken_ReturnsNull() => Assert.Null(Args.Value(["x", "--lines"], "--lines"));
    [Fact] public void Value_FirstOccurrenceWins() => Assert.Equal("1", Args.Value(["--n", "1", "--n", "2"], "--n"));
    [Fact] public void Value_PreservesValueCase() => Assert.Equal("HeLLo", Args.Value(["--name", "HeLLo"], "--name"));

    // ─────────────────────────────────────────────────────────────── IntValue

    [Fact] public void IntValue_Parses() => Assert.Equal(5, Args.IntValue(["--depth", "5"], "--depth", 3));
    [Fact] public void IntValue_Absent_Fallback() => Assert.Equal(3, Args.IntValue(["x"], "--depth", 3));
    [Fact] public void IntValue_Unparseable_Fallback() => Assert.Equal(3, Args.IntValue(["--depth", "abc"], "--depth", 3));
    [Fact] public void IntValue_FlagLast_Fallback() => Assert.Equal(3, Args.IntValue(["--depth"], "--depth", 3));
    [Fact] public void IntValue_Negative() => Assert.Equal(-1, Args.IntValue(["--lines", "-1"], "--lines", 99));

    // ─────────────────────────────────────────────────────────────── ValuesAfter

    [Fact] public void ValuesAfter_ReturnsRest() => Assert.Equal(new[] { "a", "b", "c" }, Args.ValuesAfter(["--content", "a", "b", "c"], "--content"));
    [Fact] public void ValuesAfter_Absent_Empty() => Assert.Empty(Args.ValuesAfter(["x"], "--content"));
    [Fact] public void ValuesAfter_FlagLast_Empty() => Assert.Empty(Args.ValuesAfter(["x", "--content"], "--content"));
    [Fact] public void ValuesAfter_CaseInsensitive() => Assert.Equal(new[] { "v" }, Args.ValuesAfter(["--CONTENT", "v"], "--content"));

    // ─────────────────────────────────────────────────────────────── Positionals

    [Fact]
    public void Positionals_SkipsFlagsAndValueFlagValues()
        => Assert.Equal(new[] { "a", "b", "c" },
            Args.Positionals(["a", "--algorithm", "sha256", "b", "-x", "c"], "--algorithm").ToArray());

    [Fact]
    public void Positionals_NoValueFlags_KeepsAllNonFlagTokens()
        => Assert.Equal(new[] { "hello", "world" }, Args.Positionals(["hello", "--upper", "world"]).ToArray());

    [Fact]
    public void Positionals_ValueFlagCaseInsensitive()
        => Assert.Equal(new[] { "x" }, Args.Positionals(["--ALGORITHM", "md5", "x"], "--algorithm").ToArray());

    [Fact]
    public void Positionals_Empty() => Assert.Empty(Args.Positionals([]));

    // ─────────────────────────────────────────────────────────────── Without

    [Fact] public void Without_RemovesFlag() => Assert.Equal(new[] { "a", "b" }, Args.Without(["a", "--all", "b"], "--all"));
    [Fact] public void Without_CaseInsensitive() => Assert.Equal(new[] { "a" }, Args.Without(["--ALL", "a"], "--all"));
    [Fact] public void Without_MultipleFlags() => Assert.Equal(new[] { "logs" }, Args.Without(["logs", "-f", "--follow"], "-f", "--follow"));
    [Fact] public void Without_PreservesOrderAndDuplicates() => Assert.Equal(new[] { "x", "x" }, Args.Without(["x", "--all", "x"], "--all"));
    [Fact] public void Without_NoMatch_ReturnsAll() => Assert.Equal(new[] { "a", "b" }, Args.Without(["a", "b"], "--all"));
}
