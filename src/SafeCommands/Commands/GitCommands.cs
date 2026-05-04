using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class GitCommands
{
    private static readonly HashSet<string> LogAllowedFlags = ["-n", "--oneline", "--graph", "--format", "--pretty", "--author", "--since", "--until", "--all", "--stat", "--no-merges", "--first-parent", "--reverse", "--abbrev-commit", "--date"];
    private static readonly HashSet<string> DiffAllowedFlags = ["--staged", "--cached", "--name-only", "--name-status", "--stat", "--shortstat", "--numstat", "--diff-filter", "--no-color", "--color=never", "--unified", "-U"];
    private static readonly HashSet<string> AddBlockedArgs = ["-A", "--all", "."];

    private static readonly Policy PushPolicy = Policy.Default.DenyFlags("--force", "-f", "--delete", "--no-verify");
    private static readonly Policy CommitPolicy = Policy.Default.DenyFlags("--no-verify", "-n");

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
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
            new("git", "commit-amend", "Amend last commit (only if not pushed)", "safe git commit-amend [-m <message>]", SafetyLevel.CheckedWrite, RunCommitAmend),
            new("git", "fetch", "Fetch from remote", "safe git fetch [<remote>]", SafetyLevel.SafeWrite, RunFetch),
            new("git", "branch-create", "Create a new branch", "safe git branch-create <name>", SafetyLevel.SafeWrite, RunBranchCreate),

            // Checked writes
            new("git", "pull", "Pull changes (requires clean tree)", "safe git pull [<remote>] [<branch>]", SafetyLevel.CheckedWrite, RunPull),
            new("git", "push", "Push to remote (--force-with-lease ok, --force blocked)", "safe git push [<remote>] [<branch>] [--force-with-lease]", SafetyLevel.CheckedWrite, RunPush),
            new("git", "checkout", "Switch branch (requires clean tree)", "safe git checkout <branch>", SafetyLevel.CheckedWrite, RunCheckout),
            new("git", "checkout-file", "Restore a specific file from HEAD", "safe git checkout-file <file>", SafetyLevel.CheckedWrite, RunCheckoutFile),
            new("git", "merge", "Merge branch (requires clean tree)", "safe git merge <branch>", SafetyLevel.CheckedWrite, RunMerge),
            new("git", "cherry-pick", "Cherry-pick a single commit", "safe git cherry-pick <hash>", SafetyLevel.CheckedWrite, RunCherryPick),
        ]);
    }

    /// <summary>Returns 1 with an error rendered if not in a git repo, else 0.</summary>
    private static int RequireGitRepo(Ports p)
    {
        if (!p.Git.IsRepo()) { p.Render.Error("Not a git repository"); return 1; }
        return 0;
    }

    /// <summary>Returns 1 with a structured Blocked envelope if the working tree is dirty, else 0.</summary>
    private static int RequireCleanTree(Ports p, string operation)
    {
        if (!p.Git.IsWorkingTreeClean())
        {
            p.Render.Blocked(operation,
                "Working tree has uncommitted changes",
                "Commit or stash your changes first: safe git stash");
            return 1;
        }
        return 0;
    }

    // === Read-only commands ===

    internal static int RunStatus(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (p.Render.JsonMode)
        {
            var r = p.Exec.Run("git", ["status", "--porcelain", "-b"]);
            var lines = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var branch = lines.Length > 0 ? lines[0].TrimStart('#', ' ') : "unknown";
            var files = lines.Skip(1).Select(l => new { status = l[..2].Trim(), file = l[3..] }).ToArray();
            p.Render.Json(new { branch, clean = files.Length == 0, files });
            return r.ExitCode;
        }
        return Run.Tool(p, "git", ["status", .. args]);
    }

    internal static int RunLog(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        var filtered = FilterFlags(args, LogAllowedFlags, allowPositional: true);
        return Run.Tool(p, "git", ["log", .. filtered]);
    }

    internal static int RunDiff(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        var filtered = FilterFlags(args, DiffAllowedFlags, allowPositional: true);
        return Run.Tool(p, "git", ["diff", .. filtered]);
    }

    internal static int RunShow(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0) { p.Render.Error("Usage: safe git show <ref>"); return 1; }
        return Run.Tool(p, "git", ["show", args[0]]);
    }

    internal static int RunBranch(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (p.Render.JsonMode)
        {
            var r = p.Exec.Run("git", ["branch", "--list", "--no-color"]);
            var branches = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => new { name = b.TrimStart('*', ' '), current = b.StartsWith('*') })
                .ToArray();
            p.Render.Json(new { branches });
            return r.ExitCode;
        }
        return Run.Tool(p, "git", ["branch", "--list", .. args]);
    }

    internal static int RunTag(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["tag", "--list", .. args]);
    }

    internal static int RunRemote(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length > 0 && args[0] == "show")
            return Run.Tool(p, "git", ["remote", .. args]);
        return Run.Tool(p, "git", ["remote", "-v"]);
    }

    internal static int RunBlame(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0) { p.Render.Error("Usage: safe git blame <file>"); return 1; }
        return Run.Tool(p, "git", ["blame", args[0]]);
    }

    internal static int RunRevParse(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0) { p.Render.Error("Usage: safe git rev-parse <ref>"); return 1; }
        return Run.Tool(p, "git", ["rev-parse", .. args]);
    }

    internal static int RunLsFiles(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["ls-files", .. args]);
    }

    internal static int RunShortlog(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["shortlog", .. args]);
    }

    // === Safe writes ===

    internal static int RunStash(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["stash", "push", .. args]);
    }

    internal static int RunStashList(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["stash", "list"]);
    }

    internal static int RunStashPop(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["stash", "pop"]);
    }

    internal static int RunStashApply(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        var stashRef = args.Length > 0 ? args[0] : "stash@{0}";
        return Run.Tool(p, "git", ["stash", "apply", stashRef]);
    }

    internal static int RunAdd(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe git add <file...> (use 'safe git add-tracked' for all tracked files)");
            return 1;
        }

        foreach (var arg in args)
        {
            if (AddBlockedArgs.Contains(arg))
            {
                p.Render.Blocked($"git add {arg}",
                    "Adding all files is not allowed - it may stage secrets or unwanted files",
                    "safe git add <specific-file> or safe git add-tracked");
                return 1;
            }
        }

        return Run.Tool(p, "git", ["add", .. args]);
    }

    internal static int RunAddTracked(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["add", "-u"]);
    }

    internal static int RunCommit(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;

        // Block --no-verify / -n - agents must not bypass pre-commit hooks
        if (CommitPolicy.Evaluate(args) is PolicyResult.Block)
        {
            p.Render.Blocked("git commit --no-verify",
                "Bypassing pre-commit hooks is not allowed - hooks exist for safety",
                "Fix the issue that the hook is catching, then commit normally");
            return 1;
        }

        // Require -m flag with message
        var msgIndex = Array.IndexOf(args, "-m");
        if (msgIndex < 0 || msgIndex >= args.Length - 1)
        {
            p.Render.Error("Usage: safe git commit -m \"<message>\"");
            return 1;
        }

        // Block --amend through regular commit - use commit-amend instead
        if (args.Contains("--amend"))
        {
            p.Render.Blocked("git commit --amend",
                "Use 'safe git commit-amend' for amending commits (includes safety checks)",
                "safe git commit-amend [-m <message>]");
            return 1;
        }

        return Run.Tool(p, "git", ["commit", .. args]);
    }

    internal static int RunCommitAmend(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;

        var head = p.Git.GetHeadStatus();
        if (head.IsPushed)
        {
            p.Render.Blocked("git commit --amend",
                $"HEAD is pushed to {head.Upstream}; amending would rewrite published history",
                "Create a new commit instead: safe git commit -m \"<message>\"");
            return 1;
        }

        return Run.Tool(p, "git", ["commit", "--amend", .. args]);
    }

    internal static int RunFetch(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        return Run.Tool(p, "git", ["fetch", .. args]);
    }

    internal static int RunBranchCreate(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0) { p.Render.Error("Usage: safe git branch-create <name>"); return 1; }
        return Run.Tool(p, "git", ["branch", args[0]]);
    }

    // === Checked writes ===

    internal static int RunPull(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (RequireCleanTree(p, "git pull") != 0) return 1;
        return Run.Tool(p, "git", ["pull", .. args]);
    }

    internal static int RunPush(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;

        if (PushPolicy.Evaluate(args) is PolicyResult.Block)
        {
            var offending = args.FirstOrDefault(a =>
                a is "--force" or "-f" or "--delete" or "--no-verify") ?? "";
            p.Render.Blocked($"git push {offending}".TrimEnd(),
                "Force push, branch deletion, and hook bypass are not allowed",
                "safe git push (use --force-with-lease if you need to overwrite a tracked branch)");
            return 1;
        }

        return Run.Tool(p, "git", ["push", .. args]);
    }

    internal static int RunCheckout(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe git checkout <branch>");
            return 1;
        }

        // Block "checkout ." or "checkout -- ." which discards all changes
        if (args[0] == "." || (args.Length >= 2 && args[0] == "--" && args[1] == "."))
        {
            p.Render.Blocked("git checkout .",
                "Discarding all changes is not allowed",
                "safe git checkout-file <specific-file> to restore individual files");
            return 1;
        }

        if (RequireCleanTree(p, "git checkout") != 0) return 1;
        return Run.Tool(p, "git", ["checkout", .. args]);
    }

    internal static int RunCheckoutFile(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe git checkout-file <file>");
            return 1;
        }

        if (args[0] == "." || args[0] == "*")
        {
            p.Render.Blocked("git checkout-file .",
                "Discarding all changes is not allowed",
                "Specify individual files: safe git checkout-file <file>");
            return 1;
        }

        return Run.Tool(p, "git", ["checkout", "--", args[0]]);
    }

    internal static int RunMerge(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0) { p.Render.Error("Usage: safe git merge <branch>"); return 1; }
        if (RequireCleanTree(p, "git merge") != 0) return 1;
        return Run.Tool(p, "git", ["merge", args[0]]);
    }

    internal static int RunCherryPick(Ports p, string[] args)
    {
        if (RequireGitRepo(p) != 0) return 1;
        if (args.Length == 0) { p.Render.Error("Usage: safe git cherry-pick <hash>"); return 1; }
        return Run.Tool(p, "git", ["cherry-pick", args[0]]);
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
                var flagBase = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
                if (allowedFlags.Contains(flagBase) || allowedFlags.Contains(arg))
                {
                    result.Add(arg);
                    if (!arg.Contains('=') && NeedsFlagValue(flagBase) && i + 1 < args.Length)
                        result.Add(args[++i]);
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
