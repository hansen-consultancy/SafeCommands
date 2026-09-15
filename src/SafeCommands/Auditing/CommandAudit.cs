using System.Text.Json;
using SafeCommands.Registry;

namespace SafeCommands.Auditing;

sealed class CommandAudit
{
    private const string FailureDiagnostic = "SafeCommands audit logging failed; this invocation may not be recorded.";
    private readonly IAuditStore? _store;
    private readonly TimeProvider _time;
    private readonly TextWriter _diagnostics;
    private readonly bool _initializationFailed;

    internal CommandAudit(IAuditStore store, TimeProvider time, TextWriter diagnostics)
    {
        _store = store;
        _time = time;
        _diagnostics = diagnostics;
    }

    private CommandAudit(TextWriter diagnostics, bool initializationFailed = false)
    {
        _time = TimeProvider.System;
        _diagnostics = diagnostics;
        _initializationFailed = initializationFailed;
    }

    public static CommandAudit FromEnvironment(TextWriter diagnostics)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                throw new IOException();
            return FromDirectory(Path.Combine(home, ".safecommands"), diagnostics);
        }
        catch
        {
            return new CommandAudit(diagnostics, initializationFailed: true);
        }
    }

    internal static CommandAudit FromDirectory(string directory, TextWriter diagnostics)
    {
        try
        {
            using var config = File.OpenRead(Path.Combine(directory, "config.json"));
            using var document = JsonDocument.Parse(config);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            if (!document.RootElement.TryGetProperty("audit", out var enabled))
                return new CommandAudit(diagnostics);
            if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new JsonException();
            return enabled.GetBoolean()
                ? new CommandAudit(new JsonlAuditStore(directory), TimeProvider.System, diagnostics)
                : new CommandAudit(diagnostics);
        }
        catch (FileNotFoundException) { return new CommandAudit(diagnostics); }
        catch (DirectoryNotFoundException) { return new CommandAudit(diagnostics); }
        catch { return new CommandAudit(diagnostics, initializationFailed: true); }
    }

    // Audit work surrounds, but never catches and retries, the invocation itself.
    public int Run(string[] args, Func<int> execute)
    {
        var warned = false;
        void Warn()
        {
            if (warned) return;
            warned = true;
            try { _diagnostics.WriteLine(FailureDiagnostic); }
            catch { /* Diagnostics must not replace a command result or exception. */ }
        }

        if (_initializationFailed) Warn();
        if (_store == null) return execute();

        AuditRecord? record = null;
        long started = 0;
        try
        {
            started = _time.GetTimestamp();
            record = new AuditRecord(1, _time.GetUtcNow().ToUniversalTime(), ProjectCommand(args),
                args.Length, ReadMetadata(() => Environment.CurrentDirectory),
                ReadMetadata(() => Environment.UserName), Environment.ProcessId, 0, null, "threw");
        }
        catch { Warn(); }

        int? exitCode = null;
        try
        {
            var result = execute();
            exitCode = result;
            return result;
        }
        finally
        {
            if (record != null)
            {
                try
                {
                    var duration = Math.Max(0, (long)_time.GetElapsedTime(started).TotalMilliseconds);
                    _store.Append(record with
                    {
                        DurationMs = duration,
                        ExitCode = exitCode,
                        Outcome = exitCode.HasValue ? "returned" : "threw"
                    });
                }
                catch { Warn(); }
            }
        }
    }

    private static string? ReadMetadata(Func<string> read)
    {
        try { return read(); }
        catch { return null; }
    }

    private static string ProjectCommand(string[] args)
    {
        if (args.Length == 0) return "help";
        var first = args[0].ToLowerInvariant();
        switch (first)
        {
            case "help" or "-h" or "--help" or "h": return "help";
            case "version" or "-v" or "--version": return "version";
            case "instructions" or "setup": return "instructions";
        }
        if (args.Length == 1)
            return CommandRegistry.FindByGroup(first).Any() ? "help" : "unknown";
        return CommandRegistry.Find(first, args[1])?.FullName ?? "unknown";
    }
}
