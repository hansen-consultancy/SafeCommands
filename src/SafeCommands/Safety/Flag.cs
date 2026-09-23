namespace SafeCommands.Safety;

/// <summary>
/// Central flag normalization, applied by flag rules before matching. Which form a rule uses
/// depends on the direction its mistakes fail:
/// - <see cref="Base"/> (case-folded) for BLOCK lists: <c>--Force</c> must still block, so folding fails safe.
/// - <see cref="Name"/> (case kept) for ALLOW lists and exemptions: folding there fails OPEN, because
///   tools distinguish case on exactly the risky flags (gh <c>-r</c> reviewer vs <c>-R</c> repo) — STRIDE E6.
/// </summary>
static class Flag
{
    /// <summary>Strips any <c>=value</c> suffix, keeping case. <c>"--Force=true"</c> → <c>"--Force"</c>.</summary>
    public static string Name(string token)
    {
        var eq = token.IndexOf('=');
        return eq >= 0 ? token[..eq] : token;
    }

    /// <summary><see cref="Name"/>, lowercased. <c>"--Force=true"</c> → <c>"--force"</c>.</summary>
    public static string Base(string token) => Name(token).ToLowerInvariant();
}
