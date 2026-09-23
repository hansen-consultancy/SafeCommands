using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SafeCommands.Infrastructure;

static class ProcessRunner
{
    public static (int ExitCode, string Output, string Error) Run(string command, string[] args, string? workingDir = null, bool captureOutput = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        // '\n' rather than AppendLine: Environment.NewLine would put '\r' back on every line on Windows.
        if (captureOutput)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.Append(e.Data).Append('\n'); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.Append(e.Data).Append('\n'); };
        }

        process.Start();

        if (captureOutput)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        process.WaitForExit();

        return (process.ExitCode, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
    }

    public static bool CommandExists(string command)
    {
        try
        {
            var which = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
            var (exitCode, _, _) = Run(which, [command]);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
