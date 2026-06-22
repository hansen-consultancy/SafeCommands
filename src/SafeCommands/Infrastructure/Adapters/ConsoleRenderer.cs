using System.Text.Json;
using System.Text.Json.Serialization;
using SafeCommands.Infrastructure.Ports;
using Spectre.Console;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IRenderer"/> adapter and the single owner of the <c>--json</c> envelope shape
/// (<see cref="JsonOptions"/>). Markup paths route through per-instance <see cref="IAnsiConsole"/>s
/// bound to the constructor-injected writers, so tests can capture human-mode output and Error markup
/// goes to stderr (CLI convention).
/// </summary>
sealed class ConsoleRenderer : IRenderer
{
    /// <summary>Serialization options for every JSON envelope: indented, camelCase, null-omitting.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly IAnsiConsole _outAnsi;
    private readonly IAnsiConsole _errAnsi;

    public bool JsonMode { get; }

    public ConsoleRenderer(bool jsonMode, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        JsonMode = jsonMode;
        _stdout = stdout ?? Console.Out;
        _stderr = stderr ?? Console.Error;
        _outAnsi = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(_stdout) });
        _errAnsi = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(_stderr) });
    }

    public void Result(ExecResult r)
    {
        if (JsonMode)
        {
            _stdout.WriteLine(JsonSerializer.Serialize(
                new { exitCode = r.ExitCode, output = r.StdOut, error = r.StdErr },
                JsonOptions));
            return;
        }
        // ProcessRunner.Run TrimEnds captured stdout/stderr (Infrastructure/ProcessRunner.cs:45),
        // so WriteLine here adds back exactly one trailing newline — matching 0.3.x behaviour.
        if (!string.IsNullOrEmpty(r.StdOut)) _stdout.WriteLine(r.StdOut);
        if (!string.IsNullOrEmpty(r.StdErr)) _stderr.WriteLine(r.StdErr);
    }

    public void Json(object payload)
    {
        if (!JsonMode) return;
        _stdout.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public void Blocked(string command, string reason, string? suggestion)
    {
        if (JsonMode)
        {
            _stdout.WriteLine(JsonSerializer.Serialize(
                new { blocked = true, command, reason, suggestion },
                JsonOptions));
            return;
        }
        // Markup wording mirrors 0.3.x OutputFormatter.WriteBlocked exactly.
        _outAnsi.MarkupLine($"[red]Blocked:[/] [yellow]{command.EscapeMarkup()}[/]");
        _outAnsi.MarkupLine($"  [dim]Reason:[/] {reason.EscapeMarkup()}");
        if (suggestion != null)
            _outAnsi.MarkupLine($"  [dim]Try:[/] [green]{suggestion.EscapeMarkup()}[/]");
    }

    public void Info(string message)
    {
        if (JsonMode) return;
        _stdout.WriteLine(message);
    }

    public void Raw(string text) => _stdout.Write(text);

    public void Warning(string message)
    {
        if (JsonMode) return;
        _outAnsi.MarkupLine($"[yellow]Warning:[/] {message.EscapeMarkup()}");
    }

    public void Error(string message)
    {
        if (JsonMode)
        {
            _stderr.WriteLine(message);
            return;
        }
        // CLI convention: errors to stderr even in human mode.
        _errAnsi.MarkupLine($"[red]Error:[/] {message.EscapeMarkup()}");
    }
}
