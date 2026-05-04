namespace SafeCommands.Infrastructure.Ports;

/// <summary>
/// Port for structured git state probes used by handlers in <c>GitCommands</c> and
/// <c>FileCommands</c>. Each method must be answerable by a single git invocation
/// (the GitRepoAdapter contract); composite questions belong in policy code, not here.
/// Tests use <c>FakeGitRepo</c> with a fluent builder.
/// </summary>
interface IGitRepo
{
    /// <summary>True if cwd is inside a git repository (<c>git rev-parse --git-dir</c>).</summary>
    bool IsRepo();

    /// <summary>True if <c>git status --porcelain</c> emits nothing.</summary>
    bool IsWorkingTreeClean();

    /// <summary>True if <paramref name="relativePath"/> is tracked by git.</summary>
    bool IsFileTracked(string relativePath);

    /// <summary>
    /// True if <paramref name="relativePath"/> has unstaged or staged modifications relative
    /// to HEAD. Combines what was previously two separate <c>git diff</c> invocations in
    /// <c>RunDeleteTracked</c>.
    /// </summary>
    bool HasPendingChanges(string relativePath);

    /// <summary>
    /// Status of HEAD relative to <c>origin/&lt;branch&gt;</c>. <see cref="HeadStatus.IsPushed"/>
    /// is true when HEAD's hash equals the upstream's hash. Used by <c>RunCommitAmend</c> to
    /// block amends on already-published commits.
    /// </summary>
    HeadStatus GetHeadStatus();
}

/// <summary>
/// Local branch name + upstream tracking branch + push state.
/// All fields null / false when not in a repo or HEAD is detached.
/// </summary>
readonly record struct HeadStatus(string? Branch, string? Upstream, bool IsPushed);
