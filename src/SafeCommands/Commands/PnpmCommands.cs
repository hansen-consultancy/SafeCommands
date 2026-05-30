using SafeCommands.Infrastructure;
using SafeCommands.Registry;
using SafeCommands.Safety;

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
            new("pnpm", "run", "Run package script (allowed list)", "safe pnpm run <script>", SafetyLevel.SafeWrite, RunScript)
                { Policy = Policy.Default.AllowOnlyFirstArg(PackageScripts.Allowed, "Script") },
            new("pnpm", "test", "Run tests", "safe pnpm test", SafetyLevel.SafeWrite, RunTest),
            new("pnpm", "build", "Build project", "safe pnpm build", SafetyLevel.SafeWrite, RunBuild),
            new("pnpm", "store-prune", "Prune unreferenced packages from store", "safe pnpm store-prune", SafetyLevel.SafeWrite, RunStorePrune),
            new("pnpm", "dedupe", "Deduplicate dependencies", "safe pnpm dedupe", SafetyLevel.SafeWrite, RunDedupe),
        ]);
    }

    private static int RunPnpm(string[] args, bool json)
    {
        var (code, output, error) = ProcessRunner.Run("pnpm", args);
        if (json)
            OutputFormatter.WriteJson(new { exitCode = code, output, error });
        else
        {
            OutputFormatter.WritePassthrough(output);
            OutputFormatter.WritePassthroughError(error);
        }
        return code;
    }

    private static int RunOutdated(string[] args, bool json) => RunPnpm(["outdated", ..args], json);
    private static int RunList(string[] args, bool json) => RunPnpm(["list", ..args], json);
    private static int RunAudit(string[] args, bool json) => RunPnpm(["audit", ..args], json);
    private static int RunWhy(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe pnpm why <package>"); return 1; }
        return RunPnpm(["why", ..args], json);
    }

    private static int RunInstall(string[] args, bool json) => RunPnpm(["install", ..args], json);

    private static int RunScript(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe pnpm run <script>");
            return 1;
        }
        return RunPnpm(["run", ..args], json);
    }

    private static int RunTest(string[] args, bool json) => RunPnpm(["test", ..args], json);
    private static int RunBuild(string[] args, bool json) => RunPnpm(["run", "build", ..args], json);
    private static int RunStorePrune(string[] args, bool json) => RunPnpm(["store", "prune"], json);
    private static int RunDedupe(string[] args, bool json) => RunPnpm(["dedupe"], json);
}
