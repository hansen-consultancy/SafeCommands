namespace SafeCommands.Infrastructure.Ports;

/// <summary>
/// Read-only window onto VCS state, for the git precondition rules. Implementations cache
/// within one CLI invocation so a rule chain that asks the same question repeatedly spawns
/// git at most once.
/// </summary>
interface IRepoProbe
{
    /// <summary><c>git rev-parse --git-dir</c> exit 0.</summary>
    bool IsGitRepo { get; }

    /// <summary><c>git status --porcelain</c> exit 0 and no output.</summary>
    bool IsCleanTree { get; }

    /// <summary>HEAD hash equals origin/&lt;branch&gt; hash (commit is already on the remote).</summary>
    bool IsHeadPushed { get; }
}
