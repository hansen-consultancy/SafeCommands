namespace SafeCommands.Infrastructure.Ports;

/// <summary>
/// Bundle of infrastructure ports threaded through every command handler.
/// Constructed once in Program.cs; passed to handlers as the first parameter.
/// </summary>
sealed record Ports(IExecutor Exec, IRenderer Render, IGitRepo Git);
