using SafeCommands.Infrastructure;
using SafeCommands.Registry;
using SafeCommands.Safety;

namespace SafeCommands.Commands;

static class DockerCommands
{
    private static readonly HashSet<string> ComposeDownBlocked = ["-v", "--volumes", "--rmi"];
    private static readonly HashSet<string> BuildAllowed = ["-t", "--tag", "-f", "--file", "--target", "--build-arg", "--no-cache", "--pull", "--progress", "--platform"];
    private static readonly HashSet<string> ComposeUpAllowed = ["-d", "--detach", "--build", "--no-deps", "--force-recreate", "--remove-orphans", "--wait"];
    private static readonly HashSet<string> DockerBuildValueFlags = ["-t", "--tag", "-f", "--file", "--target", "--build-arg", "--platform", "--progress"];

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
            new("docker", "build", "Build image", "safe docker build [-t <tag>] [-f <dockerfile>]", SafetyLevel.SafeWrite, RunBuild)
                { Policy = Policy.Default.AllowOnlyFlags(BuildAllowed, DockerBuildValueFlags, keepPositionals: true) },
            new("docker", "compose-build", "Build compose services", "safe docker compose-build [<service>]", SafetyLevel.SafeWrite, RunComposeBuild),
            new("docker", "compose-up", "Start compose services", "safe docker compose-up [-d] [<service>]", SafetyLevel.SafeWrite, RunComposeUp)
                { Policy = Policy.Default.AllowOnlyFlags(ComposeUpAllowed, [], keepPositionals: true) },
            new("docker", "compose-restart", "Restart compose service", "safe docker compose-restart [<service>]", SafetyLevel.SafeWrite, RunComposeRestart),
            new("docker", "compose-pull", "Pull compose images", "safe docker compose-pull [<service>]", SafetyLevel.SafeWrite, RunComposePull),

            // Targeted writes
            new("docker", "stop", "Stop a running container", "safe docker stop <container>", SafetyLevel.CheckedWrite, RunStop),
            new("docker", "start", "Start a stopped container", "safe docker start <container>", SafetyLevel.CheckedWrite, RunStart),
            new("docker", "restart", "Restart a container", "safe docker restart <container>", SafetyLevel.CheckedWrite, RunRestart),
            new("docker", "compose-down", "Stop compose services (no -v)", "safe docker compose-down", SafetyLevel.CheckedWrite, RunComposeDown)
                { Policy = Policy.Default.BlockFlags(ComposeDownBlocked, "Removing volumes/images during compose down is not allowed", "safe docker compose-down (without -v)") },
        ]);
    }

    private static int RunDocker(string[] args, bool json)
    {
        var (code, output, error) = ProcessRunner.Run("docker", args);
        if (json)
            OutputFormatter.WriteJson(new { exitCode = code, output, error });
        else
        {
            OutputFormatter.WritePassthrough(output);
            OutputFormatter.WritePassthroughError(error);
        }
        return code;
    }

    private static int RunDockerCompose(string[] args, bool json)
        => RunDocker(["compose", ..args], json);

    // Read-only
    private static int RunPs(string[] args, bool json) => RunDocker(["ps", ..args], json);
    private static int RunImages(string[] args, bool json) => RunDocker(["images", ..args], json);
    private static int RunStats(string[] args, bool json) => RunDocker(["stats", "--no-stream", ..args], json);
    private static int RunNetworkLs(string[] args, bool json) => RunDocker(["network", "ls", ..args], json);
    private static int RunVolumeLs(string[] args, bool json) => RunDocker(["volume", "ls", ..args], json);

    private static int RunLogs(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe docker logs <container>"); return 1; }
        // Remove -f/--follow if present (would block forever in captured mode)
        var filtered = args.Where(a => a is not "-f" and not "--follow").ToArray();
        return RunDocker(["logs", ..filtered], json);
    }

    private static int RunInspect(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe docker inspect <container>"); return 1; }
        return RunDocker(["inspect", args[0]], json);
    }

    private static int RunComposePs(string[] args, bool json) => RunDockerCompose(["ps", ..args], json);
    private static int RunComposeLogs(string[] args, bool json)
    {
        var filtered = args.Where(a => a is not "-f" and not "--follow").ToArray();
        return RunDockerCompose(["logs", ..filtered], json);
    }

    // Safe writes
    private static int RunBuild(string[] args, bool json) => RunDocker(["build", ..args, "."], json);

    private static int RunComposeBuild(string[] args, bool json) => RunDockerCompose(["build", ..args], json);

    private static int RunComposeUp(string[] args, bool json) => RunDockerCompose(["up", ..args], json);

    private static int RunComposePull(string[] args, bool json) => RunDockerCompose(["pull", ..args], json);
    private static int RunComposeRestart(string[] args, bool json) => RunDockerCompose(["restart", ..args], json);

    // Targeted writes
    private static int RunStop(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe docker stop <container>"); return 1; }
        return RunDocker(["stop", args[0]], json);
    }

    private static int RunStart(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe docker start <container>"); return 1; }
        return RunDocker(["start", args[0]], json);
    }

    private static int RunRestart(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe docker restart <container>"); return 1; }
        return RunDocker(["restart", args[0]], json);
    }

    private static int RunComposeDown(string[] args, bool json) => RunDockerCompose(["down", ..args], json);
}
