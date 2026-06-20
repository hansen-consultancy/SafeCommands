namespace SafeCommands.Commands.Generate;

/// <summary>
/// Pure assembly of random-derived values. The entropy source is injected (a byte buffer, or a
/// <c>nextIndex</c> delegate) so the deterministic parts — encoding and alphabet mapping — are
/// table-testable; the handler supplies <c>RandomNumberGenerator</c> at the edge.
/// </summary>
static class RandomValues
{
    public static string Encode(byte[] bytes, bool hex)
        => hex ? Convert.ToHexString(bytes).ToLowerInvariant() : Convert.ToBase64String(bytes);

    /// <summary>Build a string of <paramref name="length"/> characters drawn from
    /// <paramref name="alphabet"/>, each index produced by <paramref name="nextIndex"/> (called with
    /// the alphabet length, expected to return [0, length)). Used for passwords and nanoids.</summary>
    public static string FromAlphabet(string alphabet, int length, Func<int, int> nextIndex)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = alphabet[nextIndex(alphabet.Length)];
        return new string(chars);
    }
}
