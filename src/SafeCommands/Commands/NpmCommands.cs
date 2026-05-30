using SafeCommands.Infrastructure;
using SafeCommands.Registry;
using SafeCommands.Safety;

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
            new("npm", "run", "Run package script (allowed list)", "safe npm run <script>", SafetyLevel.SafeWrite, RunScript)
                { Policy = Policy.Default.AllowOnlyFirstArg(PackageScripts.Allowed, "Script") },
            new("npm", "test", "Run tests", "safe npm test", SafetyLevel.SafeWrite, RunTest),
            new("npm", "build", "Build project", "safe npm build", SafetyLevel.SafeWrite, RunBuild),
            new("npm", "audit-fix", "Fix audit issues (no --force)", "safe npm audit-fix", SafetyLevel.CheckedWrite, RunAuditFix)
                { Policy = Policy.Default.BlockFlags(["--force"], "Force audit fix can install breaking major version changes", "safe npm audit-fix (without --force) for safe semver-compatible fixes") },
            new("npm", "cache-clean", "Clean npm cache", "safe npm cache-clean", SafetyLevel.SafeWrite, RunCacheClean),
            new("npm", "dedupe", "Deduplicate dependencies", "safe npm dedupe", SafetyLevel.SafeWrite, RunDedupe),
        ]);
    }

    private static int RunNpm(string[] args, bool json)
    {
        var (code, output, error) = ProcessRunner.Run("npm", args);
        if (json)
            OutputFormatter.WriteJson(new { exitCode = code, output, error });
        else
        {
            OutputFormatter.WritePassthrough(output);
            OutputFormatter.WritePassthroughError(error);
        }
        return code;
    }

    private static int RunOutdated(string[] args, bool json)
    {
        if (json) return RunNpm(["outdated", "--json"], true);
        return RunNpm(["outdated"], false);
    }

    private static int RunList(string[] args, bool json)
    {
        var npmArgs = new List<string> { "list" };
        var depthIdx = Array.IndexOf(args, "--depth");
        if (depthIdx >= 0 && depthIdx + 1 < args.Length)
        {
            npmArgs.Add("--depth");
            npmArgs.Add(args[depthIdx + 1]);
        }
        if (json) npmArgs.Add("--json");
        return RunNpm(npmArgs.ToArray(), false); // already json if requested
    }

    private static int RunAudit(string[] args, bool json)
    {
        if (json) return RunNpm(["audit", "--json"], true);
        return RunNpm(["audit"], false);
    }

    private static int RunView(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe npm view <package>"); return 1; }
        if (json) return RunNpm(["view", args[0], "--json"], true);
        return RunNpm(["view", args[0]], false);
    }

    private static int RunInstall(string[] args, bool json)
    {
        // Warn about postinstall scripts - supply chain attack vector
        if (!args.Contains("--ignore-scripts"))
            OutputFormatter.WriteWarning("npm install runs postinstall scripts. Add --ignore-scripts for safer installs.");
        return RunNpm(["install", ..args], json);
    }

    private static int RunCi(string[] args, bool json)
    {
        if (!args.Contains("--ignore-scripts"))
            OutputFormatter.WriteWarning("npm ci runs postinstall scripts. Add --ignore-scripts for safer installs.");
        return RunNpm(["ci", ..args], json);
    }

    private static int RunScript(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe npm run <script>");
            OutputFormatter.WriteWarning($"Allowed scripts: {string.Join(", ", PackageScripts.Allowed)}");
            return 1;
        }

        return RunNpm(["run", ..args], json);
    }

    private static int RunAuditFix(string[] args, bool json) => RunNpm(["audit", "fix", ..args], json);

    private static int RunTest(string[] args, bool json) => RunNpm(["test", ..args], json);
    private static int RunBuild(string[] args, bool json) => RunNpm(["run", "build", ..args], json);
    private static int RunCacheClean(string[] args, bool json) => RunNpm(["cache", "clean", "--force"], json);
    private static int RunDedupe(string[] args, bool json) => RunNpm(["dedupe"], json);
}
