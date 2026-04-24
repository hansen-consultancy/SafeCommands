using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SafeCommands.Infrastructure;
using SafeCommands.Registry;

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
            new("process", "kill-name", "Kill process by name (dev tools only)", "safe process kill-name <name>", SafetyLevel.CheckedWrite, RunKillName),
        ]);
    }

    private static int RunList(string[] args, bool json)
    {
        var filter = "";
        var filterIdx = Array.IndexOf(args, "--filter");
        if (filterIdx >= 0 && filterIdx + 1 < args.Length)
            filter = args[filterIdx + 1];

        var processes = Process.GetProcesses()
            .Where(p =>
            {
                try { return string.IsNullOrEmpty(filter) || p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            })
            .Select(p =>
            {
                try { return new { pid = p.Id, name = p.ProcessName, memory = p.WorkingSet64 }; }
                catch { return new { pid = p.Id, name = p.ProcessName, memory = 0L }; }
            })
            .OrderBy(p => p.name)
            .Take(100)
            .ToArray();

        if (json)
            OutputFormatter.WriteJson(new { count = processes.Length, processes });
        else
        {
            foreach (var p in processes)
                Console.WriteLine($"{p.pid,8}  {p.name,-30}  {p.memory / 1024 / 1024,6} MB");
        }
        return 0;
    }

    private static int RunFind(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe process find <name>"); return 1; }
        var name = args[0];

        var processes = Process.GetProcessesByName(name)
            .Select(p =>
            {
                try { return new { pid = p.Id, name = p.ProcessName, memory = p.WorkingSet64 }; }
                catch { return new { pid = p.Id, name = p.ProcessName, memory = 0L }; }
            })
            .ToArray();

        if (json)
            OutputFormatter.WriteJson(new { count = processes.Length, processes });
        else if (processes.Length == 0)
            Console.WriteLine($"No processes found matching '{name}'");
        else
            foreach (var p in processes)
                Console.WriteLine($"{p.pid,8}  {p.name,-30}  {p.memory / 1024 / 1024,6} MB");

        return 0;
    }

    private static int RunPorts(string[] args, bool json)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var (code, output, error) = ProcessRunner.Run("netstat", ["-ano", "-p", "TCP"]);
            if (json)
                OutputFormatter.WriteJson(new { output });
            else
                OutputFormatter.WritePassthrough(output);
            return code;
        }
        else
        {
            // Try ss first, then lsof
            if (ProcessRunner.CommandExists("ss"))
            {
                var (code, output, _) = ProcessRunner.Run("ss", ["-tlnp"]);
                if (json) OutputFormatter.WriteJson(new { output });
                else OutputFormatter.WritePassthrough(output);
                return code;
            }
            else
            {
                var (code, output, _) = ProcessRunner.Run("lsof", ["-i", "-P", "-n"]);
                if (json) OutputFormatter.WriteJson(new { output });
                else OutputFormatter.WritePassthrough(output);
                return code;
            }
        }
    }

    private static int RunKillPort(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe process kill-port <port>"); return 1; }
        if (!int.TryParse(args[0], out var port) || port < 1 || port > 65535)
        {
            OutputFormatter.WriteError("Invalid port number");
            return 1;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var (code, output, _) = ProcessRunner.Run("netstat", ["-ano", "-p", "TCP"]);
            if (code != 0) { OutputFormatter.WriteError("Failed to query ports"); return 1; }

            var pids = output.Split('\n')
                .Where(l => l.Contains($":{port} ") || l.Contains($":{port}\t"))
                .Where(l => l.Contains("LISTENING"))
                .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last())
                .Distinct()
                .ToArray();

            if (pids.Length == 0)
            {
                Console.WriteLine($"No process listening on port {port}");
                return 0;
            }

            var killed = new List<object>();
            foreach (var pidStr in pids)
            {
                if (int.TryParse(pidStr, out var pid))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        var name = proc.ProcessName;
                        proc.Kill();
                        killed.Add(new { pid, name });
                    }
                    catch (Exception ex)
                    {
                        OutputFormatter.WriteWarning($"Could not kill PID {pid}: {ex.Message}");
                    }
                }
            }

            if (json)
                OutputFormatter.WriteJson(new { port, killed });
            else
                foreach (var k in killed)
                    OutputFormatter.WriteSuccess($"Killed process on port {port}: {k}");
        }
        else
        {
            // Unix: use lsof or fuser
            var (code, output, _) = ProcessRunner.Run("lsof", ["-t", $"-i:{port}"]);
            if (code != 0 || string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"No process listening on port {port}");
                return 0;
            }

            foreach (var pidStr in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(pidStr.Trim(), out var pid))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        proc.Kill();
                        if (json) OutputFormatter.WriteJson(new { killed = true, pid, port });
                        else OutputFormatter.WriteSuccess($"Killed PID {pid} on port {port}");
                    }
                    catch (Exception ex)
                    {
                        OutputFormatter.WriteWarning($"Could not kill PID {pid}: {ex.Message}");
                    }
                }
            }
        }

        return 0;
    }

    private static int RunKillName(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe process kill-name <name>"); return 1; }
        var name = args[0].ToLowerInvariant();

        if (!AllowedKillNames.Contains(name))
        {
            OutputFormatter.WriteBlocked($"process kill-name {name}",
                $"Process '{name}' is not in the allowed kill list",
                $"Allowed: {string.Join(", ", AllowedKillNames.Take(10))}...");
            return 1;
        }

        var processes = Process.GetProcessesByName(args[0]);
        if (processes.Length == 0)
        {
            Console.WriteLine($"No processes found matching '{name}'");
            return 0;
        }

        var killed = new List<object>();
        foreach (var proc in processes)
        {
            try
            {
                var pid = proc.Id;
                proc.Kill();
                killed.Add(new { pid, name = proc.ProcessName });
            }
            catch (Exception ex)
            {
                OutputFormatter.WriteWarning($"Could not kill {proc.ProcessName} (PID {proc.Id}): {ex.Message}");
            }
        }

        if (json)
            OutputFormatter.WriteJson(new { killed, count = killed.Count });
        else
            OutputFormatter.WriteSuccess($"Killed {killed.Count} '{name}' processes");

        return 0;
    }
}
