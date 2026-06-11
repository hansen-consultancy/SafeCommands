using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests.Fakes;

/// <summary>
/// Controllable fake <see cref="IRepoProbe"/>. Defaults satisfy every Require* rule
/// (repo present, tree clean, HEAD not yet pushed), so a test opts into a blocking
/// condition by flipping exactly one property.
/// </summary>
sealed class FakeRepoProbe : IRepoProbe
{
    public bool IsGitRepo { get; set; } = true;
    public bool IsCleanTree { get; set; } = true;
    public bool IsHeadPushed { get; set; } = false;
}
