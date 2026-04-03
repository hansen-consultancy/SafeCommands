using System.Runtime.InteropServices;
using SafeCommands.Infrastructure;
using SafeCommands.Registry;

namespace SafeCommands.Commands;

static class EnvCommands
{
    private static readonly HashSet<string> SecretPatterns =
    [
        "PASSWORD", "SECRET", "TOKEN", "KEY", "CREDENTIAL",
        "AUTH", "PRIVATE", "API_KEY", "APIKEY", "CONNECTION_STRING",
        "CONNECTIONSTRING", "AWS_SECRET", "AZURE_CLIENT_SECRET",
    ];

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            new("env", "info", "Show OS, runtime, and shell info", "safe env info", SafetyLevel.ReadOnly, RunInfo),
            new("env", "path", "Show PATH entries", "safe env path", SafetyLevel.ReadOnly, RunPath),
            new("env", "check", "Check if a tool is available", "safe env check <tool>", SafetyLevel.ReadOnly, RunCheck),
            new("env", "which", "Show tool location", "safe env which <tool>", SafetyLevel.ReadOnly, RunWhich),
            new("env", "vars", "Show environment variables (secrets filtered)", "safe env vars [<filter>]", SafetyLevel.ReadOnly, RunVars),
        ]);
    }

    private static int RunInfo(string[] args, bool json)
    {
        var info = new
        {
            os = RuntimeInformation.OSDescription,
            arch = RuntimeInformation.OSArchitecture.ToString(),
            platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "unknown",
            dotnetVersion = Environment.Version.ToString(),
            processId = Environment.ProcessId,
            currentDirectory = Environment.CurrentDirectory,
            userName = Environment.UserName,
            machineName = Environment.MachineName,
            processorCount = Environment.ProcessorCount,
        };

        if (json)
        {
            OutputFormatter.WriteJson(info);
        }
        else
        {
            Console.WriteLine($"OS:          {info.os}");
            Console.WriteLine($"Arch:        {info.arch}");
            Console.WriteLine($"Platform:    {info.platform}");
            Console.WriteLine($".NET:        {info.dotnetVersion}");
            Console.WriteLine($"Directory:   {info.currentDirectory}");
            Console.WriteLine($"User:        {info.userName}");
            Console.WriteLine($"Machine:     {info.machineName}");
            Console.WriteLine($"CPUs:        {info.processorCount}");
        }
        return 0;
    }

    private static int RunPath(string[] args, bool json)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var entries = pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        if (json)
            OutputFormatter.WriteJson(new { entries });
        else
            foreach (var entry in entries)
                Console.WriteLine(entry);

        return 0;
    }

    private static int RunCheck(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe env check <tool>"); return 1; }
        var tool = args[0];
        var available = ProcessRunner.CommandExists(tool);

        string? version = null;
        if (available)
        {
            // Try to get version
            var (code, output, _) = ProcessRunner.Run(tool, ["--version"]);
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
                version = output.Split('\n')[0].Trim();
        }

        if (json)
            OutputFormatter.WriteJson(new { tool, available, version });
        else if (available)
            Console.WriteLine($"{tool}: available{(version != null ? $" ({version})" : "")}");
        else
            Console.WriteLine($"{tool}: not found");

        return available ? 0 : 1;
    }

    private static int RunWhich(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe env which <tool>"); return 1; }
        var tool = args[0];
        var whichCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
        var (code, output, _) = ProcessRunner.Run(whichCmd, [tool]);

        if (json)
            OutputFormatter.WriteJson(new { tool, found = code == 0, path = code == 0 ? output.Split('\n')[0].Trim() : null });
        else if (code == 0)
            Console.WriteLine(output.Split('\n')[0].Trim());
        else
            Console.WriteLine($"{tool}: not found");

        return code == 0 ? 0 : 1;
    }

    private static int RunVars(string[] args, bool json)
    {
        var filter = args.Length > 0 ? args[0] : null;
        var vars = Environment.GetEnvironmentVariables();
        var entries = new Dictionary<string, string>();

        foreach (System.Collections.DictionaryEntry entry in vars)
        {
            var key = entry.Key?.ToString() ?? "";
            var value = entry.Value?.ToString() ?? "";

            if (filter != null && !key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Mask potential secrets
            if (SecretPatterns.Any(p => key.ToUpperInvariant().Contains(p)) && !string.IsNullOrEmpty(value))
                value = "***masked***";

            entries[key] = value;
        }

        if (json)
            OutputFormatter.WriteJson(entries);
        else
            foreach (var (key, value) in entries.OrderBy(e => e.Key))
                Console.WriteLine($"{key}={value}");

        return 0;
    }
}
