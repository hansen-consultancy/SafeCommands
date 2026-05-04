using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IGitRepo"/> adapter. Each method is exactly one <see cref="IExecutor.Run"/>
/// call. <see cref="GetHeadStatus"/> uses <c>git status --porcelain -b</c> so branch, upstream,
/// and ahead/behind state come back together without hard-coding <c>origin/</c>. No policy or
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
        // Single porcelain probe covers both staged and unstaged changes for the path.
        // Callers must IsFileTracked-gate first so untracked '??' lines don't appear here.
        var r = exec.Run("git", ["status", "--porcelain", "--", relativePath]);
        return r.ExitCode == 0 && !string.IsNullOrWhiteSpace(r.StdOut);
    }

    public HeadStatus GetHeadStatus()
    {
        // First line of `status --porcelain -b` is one of:
        //   "## main...origin/main"                      (in sync; pushed)
        //   "## main...origin/main [ahead 2]"            (local commits not yet pushed)
        //   "## main...origin/main [ahead 1, behind 3]"  (diverged)
        //   "## main...upstream/main [behind 4]"         (HEAD is at a published commit)
        //   "## main"                                    (no upstream configured)
        //   "## HEAD (no branch)"                        (detached)
        //   "## No commits yet on main"                  (fresh repo)
        var r = exec.Run("git", ["status", "--porcelain", "-b"]);
        if (r.ExitCode != 0) return default;

        var nl = r.StdOut.IndexOf('\n');
        var first = (nl >= 0 ? r.StdOut[..nl] : r.StdOut).TrimEnd('\r');
        if (!first.StartsWith("## ")) return default;

        var info = first[3..];
        if (info.StartsWith("HEAD ") || info.StartsWith("No commits")) return default;

        // Split off the optional "[ahead N, behind M]" suffix.
        string tracking;
        bool ahead = false;
        var bracket = info.IndexOf(" [");
        if (bracket >= 0)
        {
            tracking = info[..bracket];
            ahead = info[bracket..].Contains("ahead");
        }
        else
        {
            tracking = info;
        }

        var sep = tracking.IndexOf("...", StringComparison.Ordinal);
        string branch;
        string? upstream;
        if (sep >= 0)
        {
            branch = tracking[..sep];
            upstream = tracking[(sep + 3)..];
        }
        else
        {
            branch = tracking;
            upstream = null;
        }

        // HEAD is "pushed" iff an upstream is configured and we have no local-only commits.
        // [behind N] alone still means HEAD sits on a published commit, so it counts as pushed.
        var pushed = upstream is not null && !ahead;
        return new HeadStatus(branch, pushed ? upstream : null, pushed);
    }
}
