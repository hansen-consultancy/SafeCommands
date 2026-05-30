using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IRepoProbe"/> adapter. Each property lazily computes once via the executor
/// and caches the answer for the lifetime of the instance.
/// </summary>
sealed class GitRepoProbe(IExecutor exec) : IRepoProbe
{
    private bool? _isGitRepo;
    private bool? _isCleanTree;
    private bool? _isHeadPushed;

    public bool IsGitRepo => _isGitRepo ??= exec.Run("git", ["rev-parse", "--git-dir"]).ExitCode == 0;

    public bool IsCleanTree => _isCleanTree ??= ComputeCleanTree();

    public bool IsHeadPushed => _isHeadPushed ??= ComputeHeadPushed();

    private bool ComputeCleanTree()
    {
        var r = exec.Run("git", ["status", "--porcelain"]);
        return r.ExitCode == 0 && string.IsNullOrWhiteSpace(r.StdOut);
    }

    private bool ComputeHeadPushed()
    {
        var headHash = exec.Run("git", ["rev-parse", "HEAD"]).StdOut;
        var branch = exec.Run("git", ["rev-parse", "--abbrev-ref", "HEAD"]).StdOut;
        var remote = exec.Run("git", ["rev-parse", $"origin/{branch.Trim()}"]);
        return remote.ExitCode == 0 && headHash.Trim() == remote.StdOut.Trim();
    }
}
