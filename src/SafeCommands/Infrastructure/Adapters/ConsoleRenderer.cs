using System.Text.Json;
using SafeCommands.Infrastructure.Ports;
using Spectre.Console;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IRenderer"/> adapter. Reuses <see cref="OutputFormatter.JsonOptions"/> so the
/// JSON envelope shape is identical to legacy callers. Markup paths delegate to Spectre.
/// </summary>
sealed class ConsoleRenderer : IRenderer
{
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    public bool JsonMode { get; }

    public ConsoleRenderer(bool jsonMode, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        JsonMode = jsonMode;
        _stdout = stdout ?? Console.Out;
        _stderr = stderr ?? Console.Error;
    }

    public void Result(ExecResult r)
    {
        if (JsonMode)
        {
            _stdout.WriteLine(JsonSerializer.Serialize(
                new { exitCode = r.ExitCode, output = r.StdOut, error = r.StdErr },
                OutputFormatter.JsonOptions));
            return;
        }
        if (!string.IsNullOrEmpty(r.StdOut)) _stdout.WriteLine(r.StdOut);
        if (!string.IsNullOrEmpty(r.StdErr)) _stderr.WriteLine(r.StdErr);
    }

    public void Json(object payload)
    {
        if (!JsonMode) return;
        _stdout.WriteLine(JsonSerializer.Serialize(payload, OutputFormatter.JsonOptions));
    }

    public void Blocked(string command, string reason, string? suggestion)
    {
        if (JsonMode)
        {
            _stdout.WriteLine(JsonSerializer.Serialize(
                new { blocked = true, command, reason, suggestion },
                OutputFormatter.JsonOptions));
            return;
        }
        // Markup wording mirrors 0.3.x OutputFormatter.WriteBlocked exactly.
        AnsiConsole.MarkupLine($"[red]Blocked:[/] [yellow]{command.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [dim]Reason:[/] {reason.EscapeMarkup()}");
        if (suggestion != null)
            AnsiConsole.MarkupLine($"  [dim]Try:[/] [green]{suggestion.EscapeMarkup()}[/]");
    }

    public void Info(string message)
    {
        if (JsonMode) return;
        _stdout.WriteLine(message);
    }

    public void Warning(string message)
    {
        if (JsonMode) return;
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] {message.EscapeMarkup()}");
    }

    public void Error(string message)
    {
        if (JsonMode)
        {
            _stderr.WriteLine(message);
            return;
        }
        AnsiConsole.MarkupLine($"[red]Error:[/] {message.EscapeMarkup()}");
    }
}
