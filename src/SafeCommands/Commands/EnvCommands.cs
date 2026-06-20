using System.Runtime.InteropServices;
using SafeCommands.Infrastructure;
using SafeCommands.Registry;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class EnvCommands
{
    private static readonly HashSet<string> SafeVarPrefixes =
    [
        "PATH", "HOME", "HOMEPATH", "HOMEDRIVE", "USERPROFILE", "USER", "USERNAME", "LOGNAME",
        "SHELL", "TERM", "TERM_PROGRAM", "COLORTERM", "LANG", "LANGUAGE", "LC_ALL", "LC_CTYPE",
        "EDITOR", "VISUAL", "PAGER", "BROWSER",
        "TMPDIR", "TEMP", "TMP",
        "PWD", "OLDPWD", "SHLVL",
        "HOSTNAME", "COMPUTERNAME", "PROCESSOR_ARCHITECTURE",
        "OS", "OSTYPE", "SYSTEMROOT", "WINDIR", "PROGRAMFILES", "PROGRAMFILES(X86)", "COMMONPROGRAMFILES",
        "DOTNET_ROOT", "DOTNET_HOST_PATH", "NUGET_PACKAGES",
        "NODE_ENV", "NODE_PATH", "NPM_CONFIG_PREFIX",
        "GOPATH", "GOROOT", "CARGO_HOME", "RUSTUP_HOME",
        "JAVA_HOME", "MAVEN_HOME", "GRADLE_HOME",
        "PYTHONPATH", "VIRTUAL_ENV", "CONDA_PREFIX",
        "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_RUNTIME_DIR",
        "DISPLAY", "WAYLAND_DISPLAY", "WSL_DISTRO_NAME", "WSLENV",
        "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL",
        "SSH_AUTH_SOCK",
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY",
        "CI", "GITHUB_ACTIONS", "GITHUB_REPOSITORY", "GITHUB_REF", "GITHUB_SHA", "GITHUB_WORKFLOW",
    ];

    private static readonly HashSet<string> SecretPatterns =
    [
        "PASSWORD", "PASSWD", "SECRET", "TOKEN", "KEY", "CREDENTIAL", "AUTH",
        "PRIVATE", "API_KEY", "APIKEY", "CONNECTION_STRING", "CONNECTIONSTRING",
        "AWS_SECRET", "AZURE_CLIENT_SECRET", "SIGNING", "WEBHOOK", "STRIPE",
        "OPENAI", "ANTHROPIC", "JWT", "BEARER", "CERTIFICATE", "CERT",
        "ENCRYPTION", "DECRYPT", "MASTER", "ROOT_PASSWORD", "DB_PASS",
        "ACCESS_KEY", "SECRET_KEY", "CLIENT_SECRET", "GITHUB_TOKEN",
        "NPM_TOKEN", "NUGET_API", "PYPI_TOKEN", "DOCKER_PASSWORD",
    ];

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            new("env", "info", "Show OS, runtime, and shell info", "safe env info", SafetyLevel.ReadOnly, RunInfo),
            new("env", "path", "Show PATH entries", "safe env path", SafetyLevel.ReadOnly, RunPath),
            new("env", "check", "Check if a tool is available", "safe env check <tool>", SafetyLevel.ReadOnly, RunCheck),
            new("env", "which", "Show tool location", "safe env which <tool>", SafetyLevel.ReadOnly, RunWhich),
            new("env", "vars", "Show environment variables (safe vars only, use --all for full list with masking)", "safe env vars [--all] [<filter>]", SafetyLevel.ReadOnly, RunVars),
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

    private static bool IsSafeVar(string key)
    {
        var upper = key.ToUpperInvariant();
        return SafeVarPrefixes.Any(p => upper == p || upper.StartsWith(p + "_") || upper.StartsWith(p + "("));
    }

    private static bool IsSecretVar(string key)
    {
        var upper = key.ToUpperInvariant();
        return SecretPatterns.Any(p => upper.Contains(p));
    }

    private static int RunVars(string[] args, bool json)
    {
        var showAll = Args.HasFlag(args, "--all");
        var remaining = Args.Without(args, "--all");
        var filter = remaining.Length > 0 ? remaining[0] : null;

        var vars = Environment.GetEnvironmentVariables();
        var entries = new Dictionary<string, string>();

        foreach (System.Collections.DictionaryEntry entry in vars)
        {
            var key = entry.Key?.ToString() ?? "";
            var value = entry.Value?.ToString() ?? "";

            if (filter != null && !key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (showAll)
            {
                // Show all variables but mask secret-like ones
                if (IsSecretVar(key) && !string.IsNullOrEmpty(value))
                    value = "***masked***";
            }
            else
            {
                // Only show known-safe variables
                if (!IsSafeVar(key))
                    continue;
            }

            entries[key] = value;
        }

        if (showAll && !json)
            Console.WriteLine("WARNING: Showing all variables with secret values masked. Some secrets may still leak through unusual naming.");

        if (json)
            OutputFormatter.WriteJson(entries);
        else
            foreach (var (key, value) in entries.OrderBy(e => e.Key))
                Console.WriteLine($"{key}={value}");

        return 0;
    }
}
