using System.Runtime.InteropServices;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class ProcessCommands
{
    private static readonly HashSet<string> AllowedKillNames =
    [
        "node", "nodejs", "deno", "bun",
        "dotnet", "dotnet-watch",
        "python", "python3", "pip",
        "java", "javac", "gradle", "mvn",
        "ruby", "rails",
        "webpack", "vite", "esbuild", "tsc", "rollup", "parcel",
        "cargo",
        "php", "artisan",
        "hugo", "jekyll",
    ];

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            new("process", "list", "List running processes", "safe process list [--filter <name>]", SafetyLevel.ReadOnly, RunList),
            new("process", "find", "Find process by name", "safe process find <name>", SafetyLevel.ReadOnly, RunFind),
            new("process", "ports", "Show listening ports", "safe process ports", SafetyLevel.ReadOnly, RunPorts),
            new("process", "kill-port", "Kill process on specific port", "safe process kill-port <port>", SafetyLevel.CheckedWrite, RunKillPort),
            new("process", "kill-name", "Kill process by name (dev tools only)", "safe process kill-name <name>", SafetyLevel.CheckedWrite, RunKillName)
                { Policy = Policy.Default.AllowOnlyFirstArg(AllowedKillNames, "Process") },
        ]);
    }

    internal static int RunList(Ports p, string[] args)
    {
        var filter = Args.Value(args, "--filter") ?? "";

        var processes = p.Processes.List()
            .Where(pi => string.IsNullOrEmpty(filter) || pi.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(pi => new { pid = pi.Pid, name = pi.Name, memory = pi.Memory })
            .OrderBy(pi => pi.name)
            .Take(100)
            .ToArray();

        if (p.Render.JsonMode)
            p.Render.Json(new { count = processes.Length, processes });
        else
            foreach (var pi in processes)
                p.Render.Info($"{pi.pid,8}  {pi.name,-30}  {pi.memory / 1024 / 1024,6} MB");
        return 0;
    }

    internal static int RunFind(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe process find <name>"); return 1; }
        var name = args[0];

        var processes = p.Processes.FindByName(name)
            .Select(pi => new { pid = pi.Pid, name = pi.Name, memory = pi.Memory })
            .ToArray();

        if (p.Render.JsonMode)
            p.Render.Json(new { count = processes.Length, processes });
        else if (processes.Length == 0)
            p.Render.Info($"No processes found matching '{name}'");
        else
            foreach (var pi in processes)
                p.Render.Info($"{pi.pid,8}  {pi.name,-30}  {pi.memory / 1024 / 1024,6} MB");

        return 0;
    }

    internal static int RunPorts(Ports p, string[] args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return EmitPorts(p, p.Exec.Run("netstat", ["-ano", "-p", "TCP"]));

        // Unix: prefer ss, fall back to lsof.
        var useSs = p.Exec.Run("which", ["ss"]).ExitCode == 0;
        return useSs
            ? EmitPorts(p, p.Exec.Run("ss", ["-tlnp"]))
            : EmitPorts(p, p.Exec.Run("lsof", ["-i", "-P", "-n"]));
    }

    private static int EmitPorts(Ports p, ExecResult r)
    {
        if (p.Render.JsonMode)
            p.Render.Json(new { output = r.StdOut });
        else if (!string.IsNullOrEmpty(r.StdOut))
            p.Render.Info(r.StdOut);
        return r.ExitCode;
    }

    internal static int RunKillPort(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe process kill-port <port>"); return 1; }
        if (!int.TryParse(args[0], out var port) || port < 1 || port > 65535)
        {
            p.Render.Error("Invalid port number");
            return 1;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var r = p.Exec.Run("netstat", ["-ano", "-p", "TCP"]);
            if (r.ExitCode != 0) { p.Render.Error("Failed to query ports"); return 1; }

            var pids = r.StdOut.Split('\n')
                .Where(l => l.Contains($":{port} ") || l.Contains($":{port}\t"))
                .Where(l => l.Contains("LISTENING"))
                .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last())
                .Distinct()
                .ToArray();

            if (pids.Length == 0)
            {
                if (p.Render.JsonMode) p.Render.Json(new { port, killed = Array.Empty<object>() });
                else p.Render.Info($"No process listening on port {port}");
                return 0;
            }

            var killed = new List<object>();
            foreach (var pidStr in pids)
            {
                if (int.TryParse(pidStr, out var pid))
                {
                    var outcome = p.Processes.Kill(pid);
                    if (outcome.Killed)
                        killed.Add(new { pid, name = outcome.Name });
                    else
                        p.Render.Warning($"Could not kill PID {pid}: {outcome.Error}");
                }
            }

            if (p.Render.JsonMode)
                p.Render.Json(new { port, killed });
            else
                foreach (var k in killed)
                    p.Render.Info($"Killed process on port {port}: {k}");
        }
        else
        {
            // Unix: lsof -t lists the pids holding the port.
            var r = p.Exec.Run("lsof", ["-t", $"-i:{port}"]);
            if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.StdOut))
            {
                if (p.Render.JsonMode) p.Render.Json(new { port, killed = Array.Empty<object>() });
                else p.Render.Info($"No process listening on port {port}");
                return 0;
            }

            foreach (var pidStr in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(pidStr.Trim(), out var pid))
                {
                    var outcome = p.Processes.Kill(pid);
                    if (outcome.Killed)
                    {
                        if (p.Render.JsonMode) p.Render.Json(new { killed = true, pid, port });
                        else p.Render.Info($"Killed PID {pid} on port {port}");
                    }
                    else
                        p.Render.Warning($"Could not kill PID {pid}: {outcome.Error}");
                }
            }
        }

        return 0;
    }

    internal static int RunKillName(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe process kill-name <name>"); return 1; }
        var name = args[0].ToLowerInvariant();

        var processes = p.Processes.FindByName(args[0]);
        if (processes.Count == 0)
        {
            if (p.Render.JsonMode) p.Render.Json(new { killed = Array.Empty<object>(), count = 0 });
            else p.Render.Info($"No processes found matching '{name}'");
            return 0;
        }

        var killed = new List<object>();
        foreach (var proc in processes)
        {
            var outcome = p.Processes.Kill(proc.Pid);
            if (outcome.Killed)
                killed.Add(new { pid = proc.Pid, name = proc.Name });
            else
                p.Render.Warning($"Could not kill {proc.Name} (PID {proc.Pid}): {outcome.Error}");
        }

        if (p.Render.JsonMode)
            p.Render.Json(new { killed, count = killed.Count });
        else
            p.Render.Info($"Killed {killed.Count} '{name}' processes");

        return 0;
    }
}
