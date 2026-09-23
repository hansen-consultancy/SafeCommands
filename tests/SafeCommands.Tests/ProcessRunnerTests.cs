using SafeCommands.Infrastructure;

namespace SafeCommands.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public void Run_MultiLineOutput_JoinsWithLfOnly()
    {
        // dotnet is guaranteed on PATH wherever the tests run, and --info prints many lines.
        var (code, output, _) = ProcessRunner.Run("dotnet", ["--info"]);
        Assert.Equal(0, code);
        Assert.Contains('\n', output);
        Assert.DoesNotContain('\r', output);
    }
}
