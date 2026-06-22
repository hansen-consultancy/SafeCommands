using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

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
            new("docker", "logs", "View container logs", "safe docker logs <container> [--tail <n>]", SafetyLevel.ReadOnly, RunLogs)
                { MinArgs = 1 },
            new("docker", "inspect", "Inspect container", "safe docker inspect <container>", SafetyLevel.ReadOnly, RunInspect)
                { MinArgs = 1 },
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
            new("docker", "stop", "Stop a running container", "safe docker stop <container>", SafetyLevel.CheckedWrite, RunStop)
                { MinArgs = 1 },
            new("docker", "start", "Start a stopped container", "safe docker start <container>", SafetyLevel.CheckedWrite, RunStart)
                { MinArgs = 1 },
            new("docker", "restart", "Restart a container", "safe docker restart <container>", SafetyLevel.CheckedWrite, RunRestart)
                { MinArgs = 1 },
            new("docker", "compose-down", "Stop compose services (no -v)", "safe docker compose-down", SafetyLevel.CheckedWrite, RunComposeDown)
                { Policy = Policy.Default.BlockFlags(ComposeDownBlocked, "Removing volumes/images during compose down is not allowed", "safe docker compose-down (without -v)") },
        ]);
    }

    private static int RunDocker(Ports p, string[] args) => Run.Tool(p, "docker", args);
    private static int RunDockerCompose(Ports p, string[] args) => RunDocker(p, ["compose", ..args]);

    // Read-only
    internal static int RunPs(Ports p, string[] args) => RunDocker(p, ["ps", ..args]);
    internal static int RunImages(Ports p, string[] args) => RunDocker(p, ["images", ..args]);
    internal static int RunStats(Ports p, string[] args) => RunDocker(p, ["stats", "--no-stream", ..args]);
    internal static int RunNetworkLs(Ports p, string[] args) => RunDocker(p, ["network", "ls", ..args]);
    internal static int RunVolumeLs(Ports p, string[] args) => RunDocker(p, ["volume", "ls", ..args]);

    internal static int RunLogs(Ports p, string[] args)
    {
        // Remove -f/--follow if present (would block forever in captured mode)
        var filtered = Args.Without(args, "-f", "--follow");
        return RunDocker(p, ["logs", ..filtered]);
    }

    internal static int RunInspect(Ports p, string[] args) => RunDocker(p, ["inspect", args[0]]);

    internal static int RunComposePs(Ports p, string[] args) => RunDockerCompose(p, ["ps", ..args]);

    internal static int RunComposeLogs(Ports p, string[] args)
    {
        var filtered = Args.Without(args, "-f", "--follow");
        return RunDockerCompose(p, ["logs", ..filtered]);
    }

    // Safe writes
    internal static int RunBuild(Ports p, string[] args) => RunDocker(p, ["build", ..args, "."]);
    internal static int RunComposeBuild(Ports p, string[] args) => RunDockerCompose(p, ["build", ..args]);
    internal static int RunComposeUp(Ports p, string[] args) => RunDockerCompose(p, ["up", ..args]);
    internal static int RunComposePull(Ports p, string[] args) => RunDockerCompose(p, ["pull", ..args]);
    internal static int RunComposeRestart(Ports p, string[] args) => RunDockerCompose(p, ["restart", ..args]);

    // Targeted writes
    internal static int RunStop(Ports p, string[] args) => RunDocker(p, ["stop", args[0]]);

    internal static int RunStart(Ports p, string[] args) => RunDocker(p, ["start", args[0]]);

    internal static int RunRestart(Ports p, string[] args) => RunDocker(p, ["restart", args[0]]);

    internal static int RunComposeDown(Ports p, string[] args) => RunDockerCompose(p, ["down", ..args]);
}
