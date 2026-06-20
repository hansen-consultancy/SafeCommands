using SafeCommands.Commands.Generate;

namespace SafeCommands.Tests;

/// <summary>
/// Table tests for the pure generate transforms. These are the deterministic core extracted out of
/// the CLI plumbing — known vectors (RFC 4122 UUIDs, NIST hash digests, the jwt.io sample token)
/// pin the bit-twiddling and encoding that previously had zero coverage.
/// </summary>
public class GeneratorsTests
{
    // ─────────────────────────────────────────────────────────────── UUID

    [Theory]
    [InlineData("dns", "6ba7b810-9dad-11d1-80b4-00c04fd430c8")]
    [InlineData("DNS", "6ba7b810-9dad-11d1-80b4-00c04fd430c8")]   // case-insensitive
    [InlineData("url", "6ba7b811-9dad-11d1-80b4-00c04fd430c8")]
    [InlineData("oid", "6ba7b812-9dad-11d1-80b4-00c04fd430c8")]
    [InlineData("x500", "6ba7b814-9dad-11d1-80b4-00c04fd430c8")]
    public void ResolveNamespace_WellKnown(string token, string expected)
        => Assert.Equal(Guid.Parse(expected), Uuid.ResolveNamespace(token));

    [Fact]
    public void ResolveNamespace_ParsesArbitraryGuid()
        => Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789012"),
            Uuid.ResolveNamespace("12345678-1234-1234-1234-123456789012"));

    [Fact]
    public void ResolveNamespace_Garbage_ReturnsNull()
        => Assert.Null(Uuid.ResolveNamespace("not-a-namespace"));

    [Fact]
    public void NameBased_V3_DnsPythonOrg_MatchesRfcVector()
    {
        var ns = Uuid.ResolveNamespace("dns")!.Value;
        Assert.Equal("6fa459ea-ee8a-3ca4-894e-db77e160355e",
            Uuid.Format(Uuid.NameBased(ns, "python.org", 3), upper: false));
    }

    [Fact]
    public void NameBased_V5_DnsPythonOrg_MatchesRfcVector()
    {
        var ns = Uuid.ResolveNamespace("dns")!.Value;
        Assert.Equal("886313e1-3b8a-5372-9b90-0c9aee199e5d",
            Uuid.Format(Uuid.NameBased(ns, "python.org", 5), upper: false));
    }

    [Fact]
    public void NameBased_IsDeterministic()
    {
        var ns = Uuid.ResolveNamespace("url")!.Value;
        Assert.Equal(Uuid.NameBased(ns, "example", 5), Uuid.NameBased(ns, "example", 5));
    }

    [Fact]
    public void Format_Upper_UppercasesValue()
    {
        var ns = Uuid.ResolveNamespace("dns")!.Value;
        Assert.Equal("886313E1-3B8A-5372-9B90-0C9AEE199E5D",
            Uuid.Format(Uuid.NameBased(ns, "python.org", 5), upper: true));
    }

    [Fact]
    public void V7_KnownTimestampAndZeroRandom_EncodesTimestampVersionVariant()
        // First 48 bits = the ms timestamp (big-endian); version nibble 7; variant 10xx (=> '8').
        => Assert.Equal("017f2d1b-3c4d-7000-8000-000000000000",
            Uuid.Format(Uuid.V7(0x017F2D1B3C4DL, new byte[16]), upper: false));

    [Fact]
    public void V7_SetsVersionAndVariant_RegardlessOfRandom()
    {
        var random = Enumerable.Repeat((byte)0xFF, 16).ToArray();
        var s = Uuid.Format(Uuid.V7(0, random), upper: false);
        Assert.Equal('7', s[14]);                 // version nibble
        Assert.Contains(s[19], "89ab");           // variant 10xx
    }

    [Fact]
    public void V7_IsDeterministicForSameInputs()
        => Assert.Equal(Uuid.V7(42, new byte[16]), Uuid.V7(42, new byte[16]));

    // ─────────────────────────────────────────────────────────────── Hashing

    [Theory]
    [InlineData("sha256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("sha384", "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7")]
    [InlineData("sha512", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    [InlineData("md5", "900150983cd24fb0d6963f7d28e17f72")]
    public void HashText_Abc_MatchesKnownDigest(string algo, string expected)
        => Assert.Equal(expected, Hashing.HashText("abc", algo));

    [Fact]
    public void HashText_AlgorithmIsCaseInsensitive()
        => Assert.Equal(Hashing.HashText("abc", "sha256"), Hashing.HashText("abc", "SHA256"));

    [Fact]
    public void HashText_UnknownAlgorithm_ReturnsNull()
        => Assert.Null(Hashing.HashText("abc", "crc32"));

    [Fact]
    public void Hash_Stream_MatchesHashText()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Hashing.Hash(stream, "sha256"));
    }

    // ─────────────────────────────────────────────────────────────── Codec

    [Fact]
    public void Base64_RoundTrips()
    {
        Assert.Equal("aGVsbG8=", Codec.Base64Encode("hello"));
        Assert.Equal("hello", Codec.Base64Decode("aGVsbG8="));
    }

    [Fact]
    public void Base64Decode_Invalid_ReturnsNull()
        => Assert.Null(Codec.Base64Decode("!!!not base64!!!"));

    [Fact]
    public void Url_RoundTrips()
    {
        Assert.Equal("a%20b%26c", Codec.UrlEncode("a b&c"));
        Assert.Equal("a b&c", Codec.UrlDecode("a%20b%26c"));
    }

    [Fact]
    public void Base64UrlDecode_DecodesJwtHeaderSegment()
        => Assert.Equal("{\"alg\":\"HS256\"}", Codec.Base64UrlDecode("eyJhbGciOiJIUzI1NiJ9"));

    // ─────────────────────────────────────────────────────────────── JWT

    private const string SampleJwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
        "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ." +
        "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    [Fact]
    public void Jwt_Decode_ValidToken_DecodesHeaderAndPayload()
    {
        var (header, payload, error) = Jwt.Decode(SampleJwt);
        Assert.Equal(Jwt.Error.None, error);
        Assert.Equal("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", header);
        Assert.Contains("John Doe", payload);
    }

    [Fact]
    public void Jwt_Decode_TooFewSegments()
        => Assert.Equal(Jwt.Error.TooFewSegments, Jwt.Decode("onlyonesegment").Error);

    [Fact]
    public void Jwt_Decode_BadBase64()
        => Assert.Equal(Jwt.Error.BadBase64, Jwt.Decode("!!!.@@@").Error);

    // ─────────────────────────────────────────────────────────────── Slug

    [Theory]
    [InlineData("Hello, World!", "hello-world")]
    [InlineData("  Foo__Bar  ", "foo-bar")]
    [InlineData("already-a-slug", "already-a-slug")]
    [InlineData("Multiple   Spaces", "multiple-spaces")]
    [InlineData("!!!", "")]
    [InlineData("CamelCase123", "camelcase123")]
    public void Slug_Make(string input, string expected)
        => Assert.Equal(expected, Slug.Make(input));

    // ─────────────────────────────────────────────────────────────── Timestamps

    [Fact]
    public void Timestamps_Iso8601_FormatsToMillisecondZulu()
        => Assert.Equal("2021-01-02T03:04:05.123Z",
            Timestamps.Iso8601(new DateTimeOffset(2021, 1, 2, 3, 4, 5, 123, TimeSpan.Zero)));

    [Fact]
    public void Timestamps_UnixAndUnixMs()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_600_000_000_500L);
        Assert.Equal(1_600_000_000L, Timestamps.Unix(now));
        Assert.Equal(1_600_000_000_500L, Timestamps.UnixMs(now));
    }

    // ─────────────────────────────────────────────────────────── RandomValues

    [Fact]
    public void Encode_HexAndBase64()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        Assert.Equal("deadbeef", RandomValues.Encode(bytes, hex: true));
        Assert.Equal("3q2+7w==", RandomValues.Encode(bytes, hex: false));
    }

    [Fact]
    public void FromAlphabet_UsesAlphabetLengthAndMapsIndices()
    {
        Assert.Equal("AAAA", RandomValues.FromAlphabet("AB", 4, _ => 0));

        int next = 0;
        Assert.Equal("ABCD", RandomValues.FromAlphabet("ABCD", 4, _ => next++));
    }

    [Fact]
    public void FromAlphabet_PassesAlphabetLengthToNextIndex()
        => Assert.Equal("A", RandomValues.FromAlphabet("ABC", 1, n =>
        {
            Assert.Equal(3, n);
            return 0;
        }));

    [Fact]
    public void FromAlphabet_ZeroLength_ReturnsEmpty()
        => Assert.Equal("", RandomValues.FromAlphabet("ABC", 0, _ => 0));
}
