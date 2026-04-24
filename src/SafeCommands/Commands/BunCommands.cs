using SafeCommands.Infrastructure;
using SafeCommands.Registry;

namespace SafeCommands.Commands;

static class BunCommands
{
    private static readonly HashSet<string> AllowedScripts =
    [
        "build", "dev", "start", "test", "lint", "format",
        "typecheck", "check", "compile", "watch", "serve", "preview",
        "generate", "codegen", "migrate", "seed", "prisma",
        "storybook", "e2e", "cypress", "playwright",
        "clean", "prebuild", "postbuild",
    ];

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
            new("bun", "outdated", "Check outdated dependencies", "safe bun outdated", SafetyLevel.ReadOnly, RunOutdated),
            new("bun", "pm-ls", "List installed packages", "safe bun pm-ls", SafetyLevel.ReadOnly, RunPmLs),

            // Targeted writes - bun install runs postinstall scripts
            new("bun", "install", "Install dependencies (runs lifecycle scripts!)", "safe bun install [--ignore-scripts]", SafetyLevel.CheckedWrite, RunInstall),

            // Safe writes
            new("bun", "run", "Run package script (allowed list)", "safe bun run <script>", SafetyLevel.SafeWrite, RunScript),
            new("bun", "test", "Run tests", "safe bun test", SafetyLevel.SafeWrite, RunTest),
            new("bun", "build", "Build/bundle project", "safe bun build <entrypoint>", SafetyLevel.SafeWrite, RunBuild),
        ]);
    }

    private static int RunBun(string[] args, bool json)
    {
        var (code, output, error) = ProcessRunner.Run("bun", args);
        if (json)
            OutputFormatter.WriteJson(new { exitCode = code, output, error });
        else
        {
            OutputFormatter.WritePassthrough(output);
            OutputFormatter.WritePassthroughError(error);
        }
        return code;
    }

    private static int RunOutdated(string[] args, bool json) => RunBun(["outdated", ..args], json);
    private static int RunPmLs(string[] args, bool json) => RunBun(["pm", "ls", ..args], json);

    private static int RunInstall(string[] args, bool json)
    {
        if (!args.Contains("--ignore-scripts"))
            OutputFormatter.WriteWarning("bun install runs lifecycle scripts. Add --ignore-scripts for safer installs.");
        return RunBun(["install", ..args], json);
    }

    private static int RunScript(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe bun run <script>");
            return 1;
        }

        var script = args[0].ToLowerInvariant();
        if (!AllowedScripts.Contains(script))
        {
            OutputFormatter.WriteBlocked($"bun run {script}",
                $"Script '{script}' is not in the allowed list",
                $"Allowed: {string.Join(", ", AllowedScripts.Take(15))}...");
            return 1;
        }

        return RunBun(["run", ..args], json);
    }

    private static int RunTest(string[] args, bool json) => RunBun(["test", ..args], json);
    private static int RunBuild(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe bun build <entrypoint>"); return 1; }
        return RunBun(["build", ..args], json);
    }
}
