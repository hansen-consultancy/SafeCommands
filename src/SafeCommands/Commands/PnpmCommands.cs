using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class PnpmCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
            new("pnpm", "outdated", "Check outdated dependencies", "safe pnpm outdated", SafetyLevel.ReadOnly, RunOutdated),
            new("pnpm", "list", "List installed packages", "safe pnpm list [--depth <n>]", SafetyLevel.ReadOnly, RunList),
            new("pnpm", "audit", "Run security audit", "safe pnpm audit", SafetyLevel.ReadOnly, RunAudit),
            new("pnpm", "why", "Show why a package is installed", "safe pnpm why <package>", SafetyLevel.ReadOnly, RunWhy),

            // Safe writes - pnpm doesn't run lifecycle scripts by default (safer than npm)
            new("pnpm", "install", "Install dependencies (lifecycle scripts disabled by default)", "safe pnpm install", SafetyLevel.SafeWrite, RunInstall),
            new("pnpm", "run", "Run package script (allowed list)", "safe pnpm run <script>", SafetyLevel.SafeWrite, RunScript),
            new("pnpm", "test", "Run tests", "safe pnpm test", SafetyLevel.SafeWrite, RunTest),
            new("pnpm", "build", "Build project", "safe pnpm build", SafetyLevel.SafeWrite, RunBuild),
            new("pnpm", "store-prune", "Prune unreferenced packages from store", "safe pnpm store-prune", SafetyLevel.SafeWrite, RunStorePrune),
            new("pnpm", "dedupe", "Deduplicate dependencies", "safe pnpm dedupe", SafetyLevel.SafeWrite, RunDedupe),
        ]);
    }

    internal static int RunOutdated(Ports p, string[] args) => Run.Tool(p, "pnpm", ["outdated", .. args]);
    internal static int RunList(Ports p, string[] args)     => Run.Tool(p, "pnpm", ["list", .. args]);
    internal static int RunAudit(Ports p, string[] args)    => Run.Tool(p, "pnpm", ["audit", .. args]);

    internal static int RunWhy(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe pnpm why <package>"); return 1; }
        return Run.Tool(p, "pnpm", ["why", .. args]);
    }

    internal static int RunInstall(Ports p, string[] args) => Run.Tool(p, "pnpm", ["install", .. args]);

    internal static int RunScript(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe pnpm run <script>");
            return 1;
        }
        // Policy evaluates against the script name (args[0]), not the "run" prefix we prepend.
        var policy = Policy.Default.AllowOnlyScripts(NodeScripts.AllowedScripts);
        if (policy.Evaluate(args) is PolicyResult.Block b)
        {
            p.Render.Blocked($"pnpm run {string.Join(' ', args)}".TrimEnd(), b.Reason, b.Suggestion);
            return 1;
        }
        return Run.Tool(p, "pnpm", ["run", .. args]);
    }

    internal static int RunTest(Ports p, string[] args)       => Run.Tool(p, "pnpm", ["test", .. args]);
    internal static int RunBuild(Ports p, string[] args)      => Run.Tool(p, "pnpm", ["run", "build", .. args]);
    internal static int RunStorePrune(Ports p, string[] args) => Run.Tool(p, "pnpm", ["store", "prune"]);
    internal static int RunDedupe(Ports p, string[] args)     => Run.Tool(p, "pnpm", ["dedupe"]);
}
