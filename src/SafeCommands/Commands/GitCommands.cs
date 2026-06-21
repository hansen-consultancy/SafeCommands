using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

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

    private static int RunGit(Ports p, string[] gitArgs) => Run.Tool(p, "git", gitArgs);

    // === Read-only commands ===

    internal static int RunStatus(Ports p, string[] args)
    {
        if (p.Render.JsonMode)
        {
            var r = p.Exec.Run("git", ["status", "--porcelain", "-b"]);
            var lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var branch = lines.Length > 0 ? lines[0].TrimStart('#', ' ') : "unknown";
            var files = lines.Skip(1).Select(l => new { status = l[..2].Trim(), file = l[3..] }).ToArray();
            p.Render.Json(new { branch, clean = files.Length == 0, files });
            return r.ExitCode;
        }
        return RunGit(p, ["status", ..args]);
    }

    internal static int RunLog(Ports p, string[] args) => RunGit(p, ["log", ..args]);

    internal static int RunDiff(Ports p, string[] args) => RunGit(p, ["diff", ..args]);

    internal static int RunShow(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe git show <ref>"); return 1; }
        return RunGit(p, ["show", args[0]]);
    }

    internal static int RunBranch(Ports p, string[] args)
    {
        if (p.Render.JsonMode)
        {
            var r = p.Exec.Run("git", ["branch", "--list", "--no-color"]);
            var branches = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => new { name = b.TrimStart('*', ' '), current = b.StartsWith('*') })
                .ToArray();
            p.Render.Json(new { branches });
            return r.ExitCode;
        }
        return RunGit(p, ["branch", "--list", ..args]);
    }

    internal static int RunTag(Ports p, string[] args) => RunGit(p, ["tag", "--list", ..args]);

    internal static int RunRemote(Ports p, string[] args)
    {
        if (args.Length > 0 && args[0] == "show")
            return RunGit(p, ["remote", ..args]);
        return RunGit(p, ["remote", "-v"]);
    }

    internal static int RunBlame(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe git blame <file>"); return 1; }
        return RunGit(p, ["blame", args[0]]);
    }

    internal static int RunRevParse(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe git rev-parse <ref>"); return 1; }
        return RunGit(p, ["rev-parse", ..args]);
    }

    internal static int RunLsFiles(Ports p, string[] args) => RunGit(p, ["ls-files", ..args]);

    internal static int RunShortlog(Ports p, string[] args) => RunGit(p, ["shortlog", ..args]);

    // === Safe writes ===

    internal static int RunStash(Ports p, string[] args) => RunGit(p, ["stash", "push", ..args]);

    internal static int RunStashList(Ports p, string[] args) => RunGit(p, ["stash", "list"]);

    internal static int RunStashPop(Ports p, string[] args) => RunGit(p, ["stash", "pop"]);

    internal static int RunStashApply(Ports p, string[] args)
    {
        var stashRef = args.Length > 0 ? args[0] : "stash@{0}";
        return RunGit(p, ["stash", "apply", stashRef]);
    }

    internal static int RunAdd(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe git add <file...> (use 'safe git add-tracked' for all tracked files)");
            return 1;
        }
        return RunGit(p, ["add", ..args]);
    }

    internal static int RunAddTracked(Ports p, string[] args) => RunGit(p, ["add", "-u"]);

    internal static int RunCommit(Ports p, string[] args)
    {
        // Require -m flag with a following message token
        if (Args.Value(args, "-m") == null)
        {
            p.Render.Error("Usage: safe git commit -m \"<message>\"");
            return 1;
        }
        return RunGit(p, ["commit", ..args]);
    }

    internal static int RunCommitAmend(Ports p, string[] args) => RunGit(p, ["commit", "--amend", ..args]);

    internal static int RunFetch(Ports p, string[] args) => RunGit(p, ["fetch", ..args]);

    internal static int RunBranchCreate(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe git branch-create <name>"); return 1; }
        return RunGit(p, ["branch", args[0]]);
    }

    // === Checked writes ===

    internal static int RunPull(Ports p, string[] args) => RunGit(p, ["pull", ..args]);

    internal static int RunPush(Ports p, string[] args) => RunGit(p, ["push", ..args]);

    internal static int RunCheckout(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe git checkout [-b] <branch>");
            return 1;
        }
        return RunGit(p, ["checkout", ..args]);
    }

    internal static int RunCheckoutFile(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe git checkout-file <file>");
            return 1;
        }
        return RunGit(p, ["checkout", "--", args[0]]);
    }

    internal static int RunMerge(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe git merge <branch>"); return 1; }
        return RunGit(p, ["merge", args[0]]);
    }

    internal static int RunCherryPick(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe git cherry-pick <hash>"); return 1; }
        return RunGit(p, ["cherry-pick", args[0]]);
    }
}
