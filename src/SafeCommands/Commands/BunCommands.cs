using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class BunCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
            new("bun", "outdated", "Check outdated dependencies", "safe bun outdated", SafetyLevel.ReadOnly, RunOutdated),
            new("bun", "pm-ls", "List installed packages", "safe bun pm-ls", SafetyLevel.ReadOnly, RunPmLs),

            // Targeted writes - bun install runs postinstall scripts
            new("bun", "install", "Install dependencies (runs lifecycle scripts!)", "safe bun install [--ignore-scripts]", SafetyLevel.CheckedWrite, RunInstall),

            // Safe writes
            new("bun", "run", "Run package script (allowed list)", "safe bun run <script>", SafetyLevel.SafeWrite, RunScript)
                { Policy = Policy.Default.AllowOnlyFirstArg(PackageScripts.Allowed, "Script") },
            new("bun", "test", "Run tests", "safe bun test", SafetyLevel.SafeWrite, RunTest),
            new("bun", "build", "Build/bundle project", "safe bun build <entrypoint>", SafetyLevel.SafeWrite, RunBuild),
        ]);
    }

    internal static int RunOutdated(Ports p, string[] args) => Run.Bun(p, "outdated", args);
    internal static int RunPmLs(Ports p, string[] args)     => Run.Bun(p, "pm", ["ls", .. args]);

    internal static int RunInstall(Ports p, string[] args)
    {
        if (!Args.HasFlag(args, "--ignore-scripts"))
            p.Render.Warning("bun install runs lifecycle scripts. Add --ignore-scripts for safer installs.");
        return Run.Bun(p, "install", args);
    }

    internal static int RunScript(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe bun run <script>");
            return 1;
        }
        return Run.Bun(p, "run", args);
    }

    internal static int RunTest(Ports p, string[] args) => Run.Bun(p, "test", args);

    internal static int RunBuild(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe bun build <entrypoint>");
            return 1;
        }
        return Run.Bun(p, "build", args);
    }
}
