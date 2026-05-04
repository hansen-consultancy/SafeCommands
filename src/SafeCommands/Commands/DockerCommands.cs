using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class DockerCommands
{
    private static readonly HashSet<string> BuildAllowed = ["-t", "--tag", "-f", "--file", "--target", "--build-arg", "--no-cache", "--pull", "--progress", "--platform"];
    private static readonly HashSet<string> ComposeUpAllowed = ["-d", "--detach", "--build", "--no-deps", "--force-recreate", "--remove-orphans", "--wait"];

    private static readonly Policy ComposeDownPolicy = Policy.Default.DenyFlags("-v", "--volumes", "--rmi");

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
            new("docker", "ps", "List containers", "safe docker ps [--all]", SafetyLevel.ReadOnly, RunPs),
            new("docker", "images", "List images", "safe docker images", SafetyLevel.ReadOnly, RunImages),
            new("docker", "logs", "View container logs", "safe docker logs <container> [--tail <n>]", SafetyLevel.ReadOnly, RunLogs),
            new("docker", "inspect", "Inspect container", "safe docker inspect <container>", SafetyLevel.ReadOnly, RunInspect),
            new("docker", "compose-ps", "List compose services", "safe docker compose-ps", SafetyLevel.ReadOnly, RunComposePs),
            new("docker", "compose-logs", "View compose logs", "safe docker compose-logs [<service>]", SafetyLevel.ReadOnly, RunComposeLogs),
            new("docker", "stats", "Show container resource usage", "safe docker stats [--no-stream]", SafetyLevel.ReadOnly, RunStats),
            new("docker", "network-ls", "List networks", "safe docker network-ls", SafetyLevel.ReadOnly, RunNetworkLs),
            new("docker", "volume-ls", "List volumes", "safe docker volume-ls", SafetyLevel.ReadOnly, RunVolumeLs),

            // Safe writes
            new("docker", "build", "Build image", "safe docker build [-t <tag>] [-f <dockerfile>]", SafetyLevel.SafeWrite, RunBuild),
            new("docker", "compose-build", "Build compose services", "safe docker compose-build [<service>]", SafetyLevel.SafeWrite, RunComposeBuild),
            new("docker", "compose-up", "Start compose services", "safe docker compose-up [-d] [<service>]", SafetyLevel.SafeWrite, RunComposeUp),
            new("docker", "compose-restart", "Restart compose service", "safe docker compose-restart [<service>]", SafetyLevel.SafeWrite, RunComposeRestart),
            new("docker", "compose-pull", "Pull compose images", "safe docker compose-pull [<service>]", SafetyLevel.SafeWrite, RunComposePull),

            // Targeted writes
            new("docker", "stop", "Stop a running container", "safe docker stop <container>", SafetyLevel.CheckedWrite, RunStop),
            new("docker", "start", "Start a stopped container", "safe docker start <container>", SafetyLevel.CheckedWrite, RunStart),
            new("docker", "restart", "Restart a container", "safe docker restart <container>", SafetyLevel.CheckedWrite, RunRestart),
            new("docker", "compose-down", "Stop compose services (no -v)", "safe docker compose-down", SafetyLevel.CheckedWrite, RunComposeDown),
        ]);
    }

    // Read-only
    internal static int RunPs(Ports p, string[] args)        => Run.Tool(p, "docker", ["ps", .. args]);
    internal static int RunImages(Ports p, string[] args)    => Run.Tool(p, "docker", ["images", .. args]);
    internal static int RunStats(Ports p, string[] args)     => Run.Tool(p, "docker", ["stats", "--no-stream", .. args]);
    internal static int RunNetworkLs(Ports p, string[] args) => Run.Tool(p, "docker", ["network", "ls", .. args]);
    internal static int RunVolumeLs(Ports p, string[] args)  => Run.Tool(p, "docker", ["volume", "ls", .. args]);

    internal static int RunLogs(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe docker logs <container>"); return 1; }
        // Remove -f/--follow if present (would block forever in captured mode)
        var filtered = args.Where(a => a is not "-f" and not "--follow").ToArray();
        return Run.Tool(p, "docker", ["logs", .. filtered]);
    }

    internal static int RunInspect(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe docker inspect <container>"); return 1; }
        return Run.Tool(p, "docker", ["inspect", args[0]]);
    }

    internal static int RunComposePs(Ports p, string[] args) => Run.Tool(p, "docker", ["compose", "ps", .. args]);

    internal static int RunComposeLogs(Ports p, string[] args)
    {
        var filtered = args.Where(a => a is not "-f" and not "--follow").ToArray();
        return Run.Tool(p, "docker", ["compose", "logs", .. filtered]);
    }

    // Safe writes
    internal static int RunBuild(Ports p, string[] args)
    {
        var filtered = FilterFlags(args, BuildAllowed);
        return Run.Tool(p, "docker", ["build", .. filtered, "."]);
    }

    internal static int RunComposeBuild(Ports p, string[] args)   => Run.Tool(p, "docker", ["compose", "build", .. args]);

    internal static int RunComposeUp(Ports p, string[] args)
    {
        var filtered = FilterFlags(args, ComposeUpAllowed);
        return Run.Tool(p, "docker", ["compose", "up", .. filtered]);
    }

    internal static int RunComposePull(Ports p, string[] args)    => Run.Tool(p, "docker", ["compose", "pull", .. args]);
    internal static int RunComposeRestart(Ports p, string[] args) => Run.Tool(p, "docker", ["compose", "restart", .. args]);

    // Targeted writes
    internal static int RunStop(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe docker stop <container>"); return 1; }
        return Run.Tool(p, "docker", ["stop", args[0]]);
    }

    internal static int RunStart(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe docker start <container>"); return 1; }
        return Run.Tool(p, "docker", ["start", args[0]]);
    }

    internal static int RunRestart(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe docker restart <container>"); return 1; }
        return Run.Tool(p, "docker", ["restart", args[0]]);
    }

    internal static int RunComposeDown(Ports p, string[] args)
    {
        if (ComposeDownPolicy.Evaluate(args) is PolicyResult.Block)
        {
            p.Render.Blocked(
                $"docker compose down {string.Join(' ', args)}".TrimEnd(),
                "Removing volumes/images during compose down is not allowed",
                "safe docker compose-down (without -v)");
            return 1;
        }
        return Run.Tool(p, "docker", ["compose", "down", .. args]);
    }

    private static string[] FilterFlags(string[] args, HashSet<string> allowed)
    {
        var result = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-'))
            {
                var flagBase = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
                if (allowed.Contains(flagBase))
                {
                    result.Add(arg);
                    if (!arg.Contains('=') && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        result.Add(args[++i]);
                }
            }
            else
            {
                result.Add(arg); // positional args pass through
            }
        }
        return result.ToArray();
    }
}
