using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class NpmCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
            new("npm", "outdated", "Check outdated dependencies", "safe npm outdated", SafetyLevel.ReadOnly, RunOutdated),
            new("npm", "list", "List installed packages", "safe npm list [--depth <n>]", SafetyLevel.ReadOnly, RunList),
            new("npm", "audit", "Run security audit", "safe npm audit", SafetyLevel.ReadOnly, RunAudit),
            new("npm", "view", "View package info", "safe npm view <package>", SafetyLevel.ReadOnly, RunView),

            // Targeted writes - install runs postinstall scripts (supply chain risk)
            new("npm", "install", "Install dependencies (runs postinstall scripts!)", "safe npm install [<package>] [--ignore-scripts]", SafetyLevel.CheckedWrite, RunInstall),
            new("npm", "ci", "Clean install from lockfile (runs postinstall scripts!)", "safe npm ci [--ignore-scripts]", SafetyLevel.CheckedWrite, RunCi),
            new("npm", "run", "Run package script (allowed list)", "safe npm run <script>", SafetyLevel.SafeWrite, RunScript),
            new("npm", "test", "Run tests", "safe npm test", SafetyLevel.SafeWrite, RunTest),
            new("npm", "build", "Build project", "safe npm build", SafetyLevel.SafeWrite, RunBuild),
            new("npm", "audit-fix", "Fix audit issues (no --force)", "safe npm audit-fix", SafetyLevel.CheckedWrite, RunAuditFix),
            new("npm", "cache-clean", "Clean npm cache", "safe npm cache-clean", SafetyLevel.SafeWrite, RunCacheClean),
            new("npm", "dedupe", "Deduplicate dependencies", "safe npm dedupe", SafetyLevel.SafeWrite, RunDedupe),
        ]);
    }

    // Read-only
    internal static int RunOutdated(Ports p, string[] args)
        => Run.Tool(p, "npm", p.Render.JsonMode ? ["outdated", "--json"] : ["outdated"]);

    internal static int RunList(Ports p, string[] args)
    {
        var npmArgs = new List<string> { "list" };
        var depthIdx = Array.IndexOf(args, "--depth");
        if (depthIdx >= 0 && depthIdx + 1 < args.Length)
        {
            npmArgs.Add("--depth");
            npmArgs.Add(args[depthIdx + 1]);
        }
        if (p.Render.JsonMode) npmArgs.Add("--json");
        return Run.Tool(p, "npm", npmArgs.ToArray());
    }

    internal static int RunAudit(Ports p, string[] args)
        => Run.Tool(p, "npm", p.Render.JsonMode ? ["audit", "--json"] : ["audit"]);

    internal static int RunView(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe npm view <package>"); return 1; }
        return Run.Tool(p, "npm", p.Render.JsonMode ? ["view", args[0], "--json"] : ["view", args[0]]);
    }

    internal static int RunInstall(Ports p, string[] args)
    {
        if (!args.Contains("--ignore-scripts"))
            p.Render.Warning("npm install runs postinstall scripts. Add --ignore-scripts for safer installs.");
        return Run.Tool(p, "npm", ["install", .. args]);
    }

    internal static int RunCi(Ports p, string[] args)
    {
        if (!args.Contains("--ignore-scripts"))
            p.Render.Warning("npm ci runs postinstall scripts. Add --ignore-scripts for safer installs.");
        return Run.Tool(p, "npm", ["ci", .. args]);
    }

    internal static int RunScript(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe npm run <script>");
            p.Render.Warning($"Allowed scripts: {string.Join(", ", NodeScripts.AllowedScripts)}");
            return 1;
        }
        // Policy evaluates against the script name (args[0]), not the "run" prefix we prepend.
        var policy = Policy.Default.AllowOnlyScripts(NodeScripts.AllowedScripts);
        if (policy.Evaluate(args) is PolicyResult.Block b)
        {
            p.Render.Blocked($"npm run {string.Join(' ', args)}".TrimEnd(), b.Reason, b.Suggestion);
            return 1;
        }
        return Run.Tool(p, "npm", ["run", .. args]);
    }

    internal static int RunAuditFix(Ports p, string[] args)
    {
        if (Policy.Default.DenyFlags("--force").Evaluate(args) is PolicyResult.Block)
        {
            p.Render.Blocked("npm audit fix --force",
                "Force audit fix can install breaking major version changes",
                "safe npm audit-fix (without --force) for safe semver-compatible fixes");
            return 1;
        }
        return Run.Tool(p, "npm", ["audit", "fix", .. args]);
    }

    internal static int RunTest(Ports p, string[] args)       => Run.Tool(p, "npm", ["test", .. args]);
    internal static int RunBuild(Ports p, string[] args)      => Run.Tool(p, "npm", ["run", "build", .. args]);
    internal static int RunCacheClean(Ports p, string[] args) => Run.Tool(p, "npm", ["cache", "clean", "--force"]);
    internal static int RunDedupe(Ports p, string[] args)     => Run.Tool(p, "npm", ["dedupe"]);
}
