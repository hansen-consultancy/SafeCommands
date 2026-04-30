using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace SafeCommands.Infrastructure;

static class OutputFormatter
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void WriteJson(object data)
    {
        Console.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
    }

    public static void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{message.EscapeMarkup()}[/]");
    }

    public static void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {message.EscapeMarkup()}");
    }

    public static void WriteBlocked(string command, string reason, string? suggestion = null)
    {
        AnsiConsole.MarkupLine($"[red]Blocked:[/] [yellow]{command.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [dim]Reason:[/] {reason.EscapeMarkup()}");
        if (suggestion != null)
            AnsiConsole.MarkupLine($"  [dim]Try:[/] [green]{suggestion.EscapeMarkup()}[/]");
    }

    public static void WriteWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] {message.EscapeMarkup()}");
    }

    public static void WritePassthrough(string output)
    {
        if (!string.IsNullOrEmpty(output))
            Console.WriteLine(output);
    }

    public static void WritePassthroughError(string error)
    {
        if (!string.IsNullOrEmpty(error))
            Console.Error.WriteLine(error);
    }
}
