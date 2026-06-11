namespace SafeCommands.Safety;

/// <summary>
/// Central flag normalization, applied by flag rules before matching. Strips any
/// <c>=value</c> suffix and lowercases. <c>"--force=true"</c> → <c>"--force"</c>;
/// <c>"-f"</c> → <c>"-f"</c>; <c>"."</c> → <c>"."</c>.
/// </summary>
static class Flag
{
    public static string Base(string token)
    {
        var eq = token.IndexOf('=');
        var basePart = eq >= 0 ? token[..eq] : token;
        return basePart.ToLowerInvariant();
    }
}
