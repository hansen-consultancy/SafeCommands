namespace SafeCommands.Auditing;

sealed record AuditRecord(
    int SchemaVersion,
    DateTimeOffset TimestampUtc,
    string Command,
    int ArgumentCount,
    string? WorkingDirectory,
    string? User,
    int ProcessId,
    long DurationMs,
    int? ExitCode,
    string Outcome);
