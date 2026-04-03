using SafeCommands.Infrastructure;
using SafeCommands.Registry;

namespace SafeCommands.Commands;

static class GitCommands
{
    // Flags allowed for read-only git commands
    private static readonly HashSet<string> LogAllowedFlags = ["-n", "--oneline", "--graph", "--format", "--pretty", "--author", "--since", "--until", "--all", "--stat", "--no-merges", "--first-parent", "--reverse", "--abbrev-commit", "--date"];
    private static readonly HashSet<string> DiffAllowedFlags = ["--staged", "--cached", "--name-only", "--name-status", "--stat", "--shortstat", "--numstat", "--diff-filter", "--no-color", "--color=never", "--unified", "-U"];
    private static readonly HashSet<string> PushBlockedFlags = ["--force", "-f", "--delete"];
    private static readonly HashSet<string> AddBlockedArgs = ["-A", "--all", "."];

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only commands
            new("git", "status", "Show working tree status", "safe git status", SafetyLevel.ReadOnly, RunStatus),
            new("git", "log", "Show commit history", "safe git log [-n <count>] [--oneline] [--graph]", SafetyLevel.ReadOnly, RunLog),
            new("git", "diff", "Show changes", "safe git diff [--staged] [--name-only] [file...]", SafetyLevel.ReadOnly, RunDiff),
            new("git", "show", "Show commit or object details", "safe git show <ref>", SafetyLevel.ReadOnly, RunShow),
            new("git", "branch", "List branches", "safe git branch", SafetyLevel.ReadOnly, RunBranch),
            new("git", "tag", "List tags", "safe git tag", SafetyLevel.ReadOnly, RunTag),
            new("git", "remote", "List or show remotes", "safe git remote [show <name>]", SafetyLevel.ReadOnly, RunRemote),
            new("git", "blame", "Show file blame annotations", "safe git blame <file>", SafetyLevel.ReadOnly, RunBlame),
            new("git", "rev-parse", "Resolve git references", "safe git rev-parse <ref>", SafetyLevel.ReadOnly, RunRevParse),
            new("git", "ls-files", "List tracked files", "safe git ls-files [--modified] [--others]", SafetyLevel.ReadOnly, RunLsFiles),
            new("git", "shortlog", "Summarize git log output", "safe git shortlog [-s] [-n]", SafetyLevel.ReadOnly, RunShortlog),

            // Safe writes
            new("git", "stash", "Stash current changes", "safe git stash", SafetyLevel.SafeWrite, RunStash),
            new("git", "stash-list", "List stashes", "safe git stash-list", SafetyLevel.ReadOnly, RunStashList),
            new("git", "stash-pop", "Apply and remove top stash", "safe git stash-pop", SafetyLevel.SafeWrite, RunStashPop),
            new("git", "stash-apply", "Apply stash without removing", "safe git stash-apply [<ref>]", SafetyLevel.SafeWrite, RunStashApply),
            new("git", "add", "Stage specific files", "safe git add <file...>", SafetyLevel.SafeWrite, RunAdd),
            new("git", "add-tracked", "Stage all tracked modified files", "safe git add-tracked", SafetyLevel.SafeWrite, RunAddTracked),
            new("git", "commit", "Commit staged changes", "safe git commit -m <message>", SafetyLevel.SafeWrite, RunCommit),
            new("git", "commit-amend", "Amend last commit (only if not pushed)", "safe git commit-amend [-m <message>]", SafetyLevel.TargetedWrite, RunCommitAmend),
            new("git", "fetch", "Fetch from remote", "safe git fetch [<remote>]", SafetyLevel.SafeWrite, RunFetch),
            new("git", "branch-create", "Create a new branch", "safe git branch-create <name>", SafetyLevel.SafeWrite, RunBranchCreate),

            // Checked writes
            new("git", "pull", "Pull changes (requires clean tree)", "safe git pull [<remote>] [<branch>]", SafetyLevel.TargetedWrite, RunPull),
            new("git", "push", "Push to remote (--force-with-lease ok, --force blocked)", "safe git push [<remote>] [<branch>] [--force-with-lease]", SafetyLevel.TargetedWrite, RunPush),
            new("git", "checkout", "Switch branch (requires clean tree)", "safe git checkout <branch>", SafetyLevel.TargetedWrite, RunCheckout),
            new("git", "checkout-file", "Restore a specific file from HEAD", "safe git checkout-file <file>", SafetyLevel.TargetedWrite, RunCheckoutFile),
            new("git", "merge", "Merge branch (requires clean tree)", "safe git merge <branch>", SafetyLevel.TargetedWrite, RunMerge),
            new("git", "cherry-pick", "Cherry-pick a single commit", "safe git cherry-pick <hash>", SafetyLevel.TargetedWrite, RunCherryPick),
        ]);
    }

    private static bool IsWorkingTreeClean()
    {
        var (code, output, _) = ProcessRunner.Run("git", ["status", "--porcelain"]);
        return code == 0 && string.IsNullOrWhiteSpace(output);
    }

    private static bool IsGitRepo()
    {
        var (code, _, _) = ProcessRunner.Run("git", ["rev-parse", "--git-dir"]);
        return code == 0;
    }

    private static int RequireGitRepo()
    {
        if (!IsGitRepo())
        {
            OutputFormatter.WriteError("Not a git repository");
            return 1;
        }
        return 0;
    }

    private static int RequireCleanTree(string operation)
    {
        if (!IsWorkingTreeClean())
        {
            OutputFormatter.WriteBlocked(operation, "Working tree has uncommitted changes",
                "Commit or stash your changes first: safe git stash");
            return 1;
        }
        return 0;
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
        if (RequireGitRepo() != 0) return 1;
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

    private static int RunLog(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        var filtered = FilterFlags(args, LogAllowedFlags, allowPositional: true);
        return RunGit(["log", ..filtered], json);
    }

    private static int RunDiff(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        var filtered = FilterFlags(args, DiffAllowedFlags, allowPositional: true);
        return RunGit(["diff", ..filtered], json);
    }

    private static int RunShow(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git show <ref>"); return 1; }
        return RunGit(["show", args[0]], json);
    }

    private static int RunBranch(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
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

    private static int RunTag(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["tag", "--list", ..args], json);
    }

    private static int RunRemote(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length > 0 && args[0] == "show")
            return RunGit(["remote", ..args], json);
        return RunGit(["remote", "-v"], json);
    }

    private static int RunBlame(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git blame <file>"); return 1; }
        return RunGit(["blame", args[0]], json);
    }

    private static int RunRevParse(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git rev-parse <ref>"); return 1; }
        return RunGit(["rev-parse", ..args], json);
    }

    private static int RunLsFiles(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["ls-files", ..args], json);
    }

    private static int RunShortlog(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["shortlog", ..args], json);
    }

    // === Safe writes ===

    private static int RunStash(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["stash", "push", ..args], json);
    }

    private static int RunStashList(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["stash", "list"], json);
    }

    private static int RunStashPop(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["stash", "pop"], json);
    }

    private static int RunStashApply(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        var stashRef = args.Length > 0 ? args[0] : "stash@{0}";
        return RunGit(["stash", "apply", stashRef], json);
    }

    private static int RunAdd(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe git add <file...> (use 'safe git add-tracked' for all tracked files)");
            return 1;
        }

        foreach (var arg in args)
        {
            if (AddBlockedArgs.Contains(arg))
            {
                OutputFormatter.WriteBlocked($"git add {arg}",
                    "Adding all files is not allowed - it may stage secrets or unwanted files",
                    "safe git add <specific-file> or safe git add-tracked");
                return 1;
            }
        }

        return RunGit(["add", ..args], json);
    }

    private static int RunAddTracked(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["add", "-u"], json);
    }

    private static int RunCommit(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;

        // Require -m flag with message
        var msgIndex = Array.IndexOf(args, "-m");
        if (msgIndex < 0 || msgIndex >= args.Length - 1)
        {
            OutputFormatter.WriteError("Usage: safe git commit -m \"<message>\"");
            return 1;
        }

        // Block --amend through regular commit - use commit-amend instead
        if (args.Contains("--amend"))
        {
            OutputFormatter.WriteBlocked("git commit --amend",
                "Use 'safe git commit-amend' for amending commits (includes safety checks)");
            return 1;
        }

        return RunGit(["commit", ..args], json);
    }

    private static int RunCommitAmend(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;

        // Check if HEAD has been pushed to any remote tracking branch
        var (_, headHash, _) = ProcessRunner.Run("git", ["rev-parse", "HEAD"]);
        var (_, branch, _) = ProcessRunner.Run("git", ["rev-parse", "--abbrev-ref", "HEAD"]);
        var (remoteCode, remoteHash, _) = ProcessRunner.Run("git", ["rev-parse", $"origin/{branch.Trim()}"]);

        if (remoteCode == 0 && headHash.Trim() == remoteHash.Trim())
        {
            OutputFormatter.WriteBlocked("git commit --amend",
                "HEAD commit has already been pushed to remote - amending would require force push",
                "Create a new commit instead: safe git commit -m \"<message>\"");
            return 1;
        }

        return RunGit(["commit", "--amend", ..args], json);
    }

    private static int RunFetch(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        return RunGit(["fetch", ..args], json);
    }

    private static int RunBranchCreate(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git branch-create <name>"); return 1; }
        return RunGit(["branch", args[0]], json);
    }

    // === Checked writes ===

    private static int RunPull(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        var check = RequireCleanTree("git pull");
        if (check != 0) return check;
        return RunGit(["pull", ..args], json);
    }

    private static int RunPush(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;

        foreach (var arg in args)
        {
            if (PushBlockedFlags.Contains(arg))
            {
                OutputFormatter.WriteBlocked($"git push {arg}",
                    "Force push and delete are not allowed",
                    "safe git push (without --force)");
                return 1;
            }
        }

        return RunGit(["push", ..args], json);
    }

    private static int RunCheckout(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe git checkout <branch>");
            return 1;
        }

        // Block "checkout ." or "checkout -- ." which discards all changes
        if (args[0] == "." || (args.Length >= 2 && args[0] == "--" && args[1] == "."))
        {
            OutputFormatter.WriteBlocked("git checkout .",
                "Discarding all changes is not allowed",
                "safe git checkout-file <specific-file> to restore individual files");
            return 1;
        }

        var check = RequireCleanTree("git checkout");
        if (check != 0) return check;
        return RunGit(["checkout", ..args], json);
    }

    private static int RunCheckoutFile(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe git checkout-file <file>");
            return 1;
        }

        if (args[0] == "." || args[0] == "*")
        {
            OutputFormatter.WriteBlocked("git checkout-file .",
                "Discarding all changes is not allowed",
                "Specify individual files: safe git checkout-file <file>");
            return 1;
        }

        return RunGit(["checkout", "--", args[0]], json);
    }

    private static int RunMerge(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git merge <branch>"); return 1; }
        var check = RequireCleanTree("git merge");
        if (check != 0) return check;
        return RunGit(["merge", args[0]], json);
    }

    private static int RunCherryPick(string[] args, bool json)
    {
        if (RequireGitRepo() != 0) return 1;
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe git cherry-pick <hash>"); return 1; }
        return RunGit(["cherry-pick", args[0]], json);
    }

    // === Helpers ===

    private static string[] FilterFlags(string[] args, HashSet<string> allowedFlags, bool allowPositional)
    {
        var result = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-'))
            {
                // Check if the flag itself is allowed, or the flag base (for --flag=value)
                var flagBase = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
                if (allowedFlags.Contains(flagBase) || allowedFlags.Contains(arg))
                {
                    result.Add(arg);
                    // If flag expects a value and it's not --flag=value form, include next arg
                    if (!arg.Contains('=') && NeedsFlagValue(flagBase) && i + 1 < args.Length)
                    {
                        result.Add(args[++i]);
                    }
                }
                // Skip unknown flags silently
            }
            else if (allowPositional)
            {
                result.Add(arg);
            }
        }
        return result.ToArray();
    }

    private static bool NeedsFlagValue(string flag)
        => flag is "-n" or "--format" or "--pretty" or "--author" or "--since" or "--until" or "--date"
            or "--diff-filter" or "--unified" or "-U";
}
