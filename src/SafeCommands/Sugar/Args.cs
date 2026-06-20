namespace SafeCommands.Sugar;

/// <summary>
/// The single owner of raw arg-vector parsing for command handlers — "is this flag present", "what
/// value follows it", "which tokens are positional", "drop these flags". Previously each handler
/// re-derived these with <c>Array.IndexOf</c>, <c>args.Contains</c>, or hand-rolled LINQ; the
/// duplication and its inconsistent case-handling were the smell. This consolidates the mechanism.
///
/// Flag NAMES match case-insensitively (a token is a "flag" iff it starts with '-'); values are
/// returned verbatim. The <c>--name=value</c> form is NOT split here — that normalization is the
/// policy layer's job (see <c>Safety/Flag</c>); convenience parsing mirrors the handlers' historical
/// "--name value" expectation.
///
/// SAFETY INVARIANT: when a handler reads a path-bearing flag/positional that a <c>Policy</c> also
/// extracts via <c>Safety/PathArg</c>, the two MUST agree on which token they pick. <c>PathArg</c>
/// matches case-insensitively to stay aligned with this helper; diverging would let a path slip
/// past containment (the handler acts on a token the policy never checked).
/// </summary>
static class Args
{
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool HasFlag(string[] args, string flag) => args.Any(a => Eq(a, flag));

    /// <summary>The token immediately after the first occurrence of <paramref name="flag"/>, or null
    /// when the flag is absent or is the final token.</summary>
    public static string? Value(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (Eq(args[i], flag))
                return args[i + 1];
        return null;
    }

    /// <summary><see cref="Value"/> parsed as an int, falling back to <paramref name="fallback"/>
    /// when the flag is absent or its value is not an integer.</summary>
    public static int IntValue(string[] args, string flag, int fallback)
        => Value(args, flag) is { } v && int.TryParse(v, out var n) ? n : fallback;

    /// <summary>Every token after the first occurrence of <paramref name="flag"/> — for flags whose
    /// value is the rest of the line (e.g. <c>--content</c>). Empty when the flag is absent or final.</summary>
    public static string[] ValuesAfter(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
            if (Eq(args[i], flag))
                return args[(i + 1)..];
        return [];
    }

    /// <summary>Positional tokens (those not starting with '-'), skipping the value of any flag named
    /// in <paramref name="valueFlags"/>. Mirrors <c>Safety/PathArg.Positional</c>'s skip logic.</summary>
    public static IEnumerable<string> Positionals(string[] args, params string[] valueFlags)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (valueFlags.Any(f => Eq(f, a))) { i++; continue; } // skip flag + its value
            if (a.StartsWith('-')) continue;                       // skip boolean flag
            yield return a;
        }
    }

    /// <summary>The arg vector with every token equal to one of <paramref name="flags"/> removed.</summary>
    public static string[] Without(string[] args, params string[] flags)
        => args.Where(a => !flags.Any(f => Eq(f, a))).ToArray();
}
