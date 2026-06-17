using SafeCommands.Infrastructure;
using SafeCommands.Registry;
using SafeCommands.Safety;

namespace SafeCommands.Commands;

static class GitCommands
{
    // Flags allowed for read-only git commands
    private static readonly HashSet<string> LogAllowedFlags = ["-n", "--oneline", "--graph", "--format", "--pretty", "--author", "--since", "--until", "--all", "--stat", "--no-merges", "--first-parent", "--reverse", "--abbrev-commit", "--date"];
    private static readonly HashSet<string> DiffAllowedFlags = ["--staged", "--cached", "--name-only", "--name-status", "--stat", "--shortstat", "--numstat", "--diff-filter", "--no-color", "--color=never", "--unified", "-U"];
    private static readonly HashSet<string> GitValueFlags = ["-n", "--format", "--pretty", "--author", "--since", "--until", "--date", "--diff-filter", "--unified", "-U"];
    private static readonly HashSet<string> PushBlockedFlags = ["--force", "-f", "--delete", "--no-verify"];
    private static readonly HashSet<string> AddBlockedArgs = ["-A", "--all", "."];

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only commands
            new("git", "status", "Show working tree status", "safe git status", SafetyLevel.ReadOnly, RunStatus)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "log", "Show commit history", "safe git log [-n <count>] [--oneline] [--graph]", SafetyLevel.ReadOnly, RunLog)
                { Policy = Policy.Default.RequireGitRepo().AllowOnlyFlags(LogAllowedFlags, GitValueFlags, keepPositionals: true) },
            new("git", "diff", "Show changes", "safe git diff [--staged] [--name-only] [file...]", SafetyLevel.ReadOnly, RunDiff)
                { Policy = Policy.Default.RequireGitRepo().AllowOnlyFlags(DiffAllowedFlags, GitValueFlags, keepPositionals: true) },
            new("git", "show", "Show commit or object details", "safe git show <ref>", SafetyLevel.ReadOnly, RunShow)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "branch", "List branches", "safe git branch", SafetyLevel.ReadOnly, RunBranch)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "tag", "List tags", "safe git tag", SafetyLevel.ReadOnly, RunTag)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "remote", "List or show remotes", "safe git remote [show <name>]", SafetyLevel.ReadOnly, RunRemote)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "blame", "Show file blame annotations", "safe git blame <file>", SafetyLevel.ReadOnly, RunBlame)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "rev-parse", "Resolve git references", "safe git rev-parse <ref>", SafetyLevel.ReadOnly, RunRevParse)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "ls-files", "List tracked files", "safe git ls-files [--modified] [--others]", SafetyLevel.ReadOnly, RunLsFiles)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "shortlog", "Summarize git log output", "safe git shortlog [-s] [-n]", SafetyLevel.ReadOnly, RunShortlog)
                { Policy = Policy.Default.RequireGitRepo() },

            // Safe writes
            new("git", "stash", "Stash current changes", "safe git stash", SafetyLevel.SafeWrite, RunStash)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "stash-list", "List stashes", "safe git stash-list", SafetyLevel.ReadOnly, RunStashList)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "stash-pop", "Apply and remove top stash", "safe git stash-pop", SafetyLevel.SafeWrite, RunStashPop)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "stash-apply", "Apply stash without removing", "safe git stash-apply [<ref>]", SafetyLevel.SafeWrite, RunStashApply)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "add", "Stage specific files", "safe git add <file...>", SafetyLevel.SafeWrite, RunAdd)
                { Policy = Policy.Default.RequireGitRepo().BlockFlags(AddBlockedArgs, "Adding all files is not allowed - it may stage secrets or unwanted files", "safe git add <specific-file> or safe git add-tracked") },
            new("git", "add-tracked", "Stage all tracked modified files", "safe git add-tracked", SafetyLevel.SafeWrite, RunAddTracked)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "commit", "Commit staged changes", "safe git commit -m <message>", SafetyLevel.SafeWrite, RunCommit)
                { Policy = Policy.Default.RequireGitRepo()
                    .BlockFlags(["--no-verify", "-n"], "Bypassing pre-commit hooks is not allowed - hooks exist for safety", "Fix the issue that the hook is catching, then commit normally")
                    .Custom(new BlockFlagsRule(["--amend"], "Use 'safe git commit-amend' for amending commits (includes safety checks)", null)) },
            new("git", "commit-amend", "Amend last commit (only if not pushed)", "safe git commit-amend [-m <message>]", SafetyLevel.CheckedWrite, RunCommitAmend)
                { Policy = Policy.Default.RequireGitRepo().RequireHeadNotPushed() },
            new("git", "fetch", "Fetch from remote", "safe git fetch [<remote>]", SafetyLevel.SafeWrite, RunFetch)
                { Policy = Policy.Default.RequireGitRepo() },
            new("git", "branch-create", "Create a new branch", "safe git branch-create <name>", SafetyLevel.SafeWrite, RunBranchCreate)
                { Policy = Policy.Default.RequireGitRepo() },

            // Checked writes
            new("git", "pull", "Pull changes (requires clean tree)", "safe git pull [<remote>] [<branch>]", SafetyLevel.CheckedWrite, RunPull)
                { Policy = Policy.Default.RequireGitRepo().RequireCleanTree() },
            new("git", "push", "Push to remote (--force-with-lease ok, --force blocked)", "safe git push [<remote>] [<branch>] [--force-with-lease]", SafetyLevel.CheckedWrite, RunPush)
                { Policy = Policy.Default.RequireGitRepo().BlockFlags(PushBlockedFlags, "Force push and delete are not allowed", "safe git push (without --force)") },
            new("git", "checkout", "Switch branch (requires clean tree; -b creates a new branch and is exempt)", "safe git checkout [-b] <branch>", SafetyLevel.CheckedWrite, RunCheckout)
                { Policy = Policy.Default.RequireGitRepo().BlockFlags(["."], "Discarding all changes is not allowed", "safe git checkout-file <specific-file> to restore individual files").RequireCleanTree(exemptFlags: ["-b"]) },
            new("git", "checkout-file", "Restore a specific file from HEAD", "safe git checkout-file <file>", SafetyLevel.CheckedWrite, RunCheckoutFile)
                { Policy = Policy.Default.RequireGitRepo().BlockFlags([".", "*"], "Discarding all changes is not allowed", "Specify individual files: safe git checkout-file <file>") },
            new("git", "merge", "Merge branch (requires clean tree)", "safe git merge <branch>", SafetyLevel.CheckedWrite, RunMerge)
                { Policy = Policy.Default.RequireGitRepo().RequireCleanTree() },
            new("git", "cherry-pick", "Cherry-pick a single commit", "safe git cherry-pick <hash>", SafetyLevel.CheckedWrite, RunCherryPick)
                { Policy = Policy.Default.RequireGitRepo() },
        ]);
    }

    private static int RunGit(string[] gitArgs, bool json)
    {
        var (code, output, error) = ProcessRunner.Run("git", gitArgs);
        if (json)
            OutputFormatter.WriteJson(new { exitCode = code, output, error });
        else
        {
            OutputFormatter.WritePassthrough(output);
            OutputFormatter.WritePassthroughError(error);
        }
        return code;
    }

    // === Read-only commands ===

    private static int RunStatus(string[] args, bool json)
    {
        if (json)
        {
            var (code, output, _) = ProcessRunner.Run("git", ["status", "--porcelain", "-b"]);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var branch = lines.Length > 0 ? lines[0].TrimStart('#', ' ') : "unknown";
            var files = lines.Skip(1).Select(l => new { status = l[..2].Trim(), file = l[3..] }).ToArray();
            OutputFormatter.WriteJson(new { branch, clean = files.Length == 0, files });
            return code;
        }
        return RunGit(["status", ..args], false);
    }

    private static int RunLog(string[] args, bool json) => RunGit(["log", ..args], json);

    private static int RunDiff(string[] args, bool json) => RunGit(["diff", ..args], json);

    private static int RunShow(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git show <ref>"); return 1; }
        return RunGit(["show", args[0]], json);
    }

    private static int RunBranch(string[] args, bool json)
    {
        if (json)
        {
            var (code, output, _) = ProcessRunner.Run("git", ["branch", "--list", "--no-color"]);
            var branches = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => new { name = b.TrimStart('*', ' '), current = b.StartsWith('*') })
                .ToArray();
            OutputFormatter.WriteJson(new { branches });
            return code;
        }
        return RunGit(["branch", "--list", ..args], false);
    }

    private static int RunTag(string[] args, bool json) => RunGit(["tag", "--list", ..args], json);

    private static int RunRemote(string[] args, bool json)
    {
        if (args.Length > 0 && args[0] == "show")
            return RunGit(["remote", ..args], json);
        return RunGit(["remote", "-v"], json);
    }

    private static int RunBlame(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git blame <file>"); return 1; }
        return RunGit(["blame", args[0]], json);
    }

    private static int RunRevParse(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git rev-parse <ref>"); return 1; }
        return RunGit(["rev-parse", ..args], json);
    }

    private static int RunLsFiles(string[] args, bool json) => RunGit(["ls-files", ..args], json);

    private static int RunShortlog(string[] args, bool json) => RunGit(["shortlog", ..args], json);

    // === Safe writes ===

    private static int RunStash(string[] args, bool json) => RunGit(["stash", "push", ..args], json);

    private static int RunStashList(string[] args, bool json) => RunGit(["stash", "list"], json);

    private static int RunStashPop(string[] args, bool json) => RunGit(["stash", "pop"], json);

    private static int RunStashApply(string[] args, bool json)
    {
        var stashRef = args.Length > 0 ? args[0] : "stash@{0}";
        return RunGit(["stash", "apply", stashRef], json);
    }

    private static int RunAdd(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe git add <file...> (use 'safe git add-tracked' for all tracked files)");
            return 1;
        }
        return RunGit(["add", ..args], json);
    }

    private static int RunAddTracked(string[] args, bool json) => RunGit(["add", "-u"], json);

    private static int RunCommit(string[] args, bool json)
    {
        // Require -m flag with message
        var msgIndex = Array.IndexOf(args, "-m");
        if (msgIndex < 0 || msgIndex >= args.Length - 1)
        {
            OutputFormatter.WriteError("Usage: safe git commit -m \"<message>\"");
            return 1;
        }
        return RunGit(["commit", ..args], json);
    }

    private static int RunCommitAmend(string[] args, bool json) => RunGit(["commit", "--amend", ..args], json);

    private static int RunFetch(string[] args, bool json) => RunGit(["fetch", ..args], json);

    private static int RunBranchCreate(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git branch-create <name>"); return 1; }
        return RunGit(["branch", args[0]], json);
    }

    // === Checked writes ===

    private static int RunPull(string[] args, bool json) => RunGit(["pull", ..args], json);

    private static int RunPush(string[] args, bool json) => RunGit(["push", ..args], json);

    private static int RunCheckout(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe git checkout [-b] <branch>");
            return 1;
        }
        return RunGit(["checkout", ..args], json);
    }

    private static int RunCheckoutFile(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe git checkout-file <file>");
            return 1;
        }
        return RunGit(["checkout", "--", args[0]], json);
    }

    private static int RunMerge(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git merge <branch>"); return 1; }
        return RunGit(["merge", args[0]], json);
    }

    private static int RunCherryPick(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git cherry-pick <hash>"); return 1; }
        return RunGit(["cherry-pick", args[0]], json);
    }
}
