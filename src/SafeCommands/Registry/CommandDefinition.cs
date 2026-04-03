namespace SafeCommands.Registry;

enum SafetyLevel
{
    ReadOnly,       // Pure read - no side effects
    SafeWrite,      // Additive or recoverable write
    TargetedWrite,  // Write with safety checks (e.g., only if working tree clean)
}

record CommandDefinition(
    string Group,
    string Name,
    string Description,
    string Usage,
    SafetyLevel Safety,
    Func<string[], bool, int> Handler  // (args, jsonOutput) => exitCode
)
{
    public string FullName => $"{Group} {Name}";

    public string SafetyLabel => Safety switch
    {
        SafetyLevel.ReadOnly => "read-only",
        SafetyLevel.SafeWrite => "safe-write",
        SafetyLevel.TargetedWrite => "checked-write",
        _ => "unknown"
    };
}
