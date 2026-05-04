using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IGitRepo"/> adapter. Each method is one <see cref="IExecutor.Run"/>
/// call (or two for <see cref="HasPendingChanges"/> / <see cref="GetHeadStatus"/> where
/// the underlying git command needs both a staged and an unstaged probe). No policy or
/// rendering — those belong in handlers.
/// </summary>
sealed class GitRepoAdapter(IExecutor exec) : IGitRepo
{
    public bool IsRepo()
    {
        var r = exec.Run("git", ["rev-parse", "--git-dir"]);
        return r.ExitCode == 0;
    }

    public bool IsWorkingTreeClean()
    {
        var r = exec.Run("git", ["status", "--porcelain"]);
        return r.ExitCode == 0 && string.IsNullOrWhiteSpace(r.StdOut);
    }

    public bool IsFileTracked(string relativePath)
    {
        var r = exec.Run("git", ["ls-files", "--error-unmatch", relativePath]);
        return r.ExitCode == 0;
    }

    public bool HasPendingChanges(string relativePath)
    {
        var unstaged = exec.Run("git", ["diff", "--name-only", relativePath]);
        if (!string.IsNullOrWhiteSpace(unstaged.StdOut)) return true;
        var staged = exec.Run("git", ["diff", "--staged", "--name-only", relativePath]);
        return !string.IsNullOrWhiteSpace(staged.StdOut);
    }

    public HeadStatus GetHeadStatus()
    {
        var branch = exec.Run("git", ["rev-parse", "--abbrev-ref", "HEAD"]);
        if (branch.ExitCode != 0) return default;
        var branchName = branch.StdOut.Trim();
        if (string.IsNullOrEmpty(branchName) || branchName == "HEAD") return default;

        var headHash = exec.Run("git", ["rev-parse", "HEAD"]);
        var upstream = $"origin/{branchName}";
        var remoteHash = exec.Run("git", ["rev-parse", upstream]);

        var pushed = remoteHash.ExitCode == 0
            && !string.IsNullOrEmpty(headHash.StdOut.Trim())
            && headHash.StdOut.Trim() == remoteHash.StdOut.Trim();

        return new HeadStatus(branchName, pushed ? upstream : null, pushed);
    }
}
