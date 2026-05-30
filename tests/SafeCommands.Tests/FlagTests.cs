using SafeCommands.Safety;

namespace SafeCommands.Tests;

public class FlagTests
{
    [Theory]
    [InlineData("--force=true", "--force")]  // strips =value
    [InlineData("--FORCE", "--force")]       // lowercases
    [InlineData("-f", "-f")]                 // short flag unchanged
    [InlineData(".", ".")]                   // bare dot unchanged
    [InlineData("Build", "build")]           // positional lowercased
    public void Base_NormalizesToken(string token, string expected)
    {
        Assert.Equal(expected, Flag.Base(token));
    }
}
