using System.Text.RegularExpressions;

namespace SafeCommands.Commands.Generate;

/// <summary>Pure slug transform: lowercase, collapse non-alphanumeric runs to single hyphens,
/// trim leading/trailing hyphens.</summary>
static partial class Slug
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRun();

    public static string Make(string input)
        => NonAlphanumericRun().Replace(input.ToLowerInvariant(), "-").Trim('-');
}
