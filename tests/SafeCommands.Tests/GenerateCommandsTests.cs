using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Handler-level tests for the migrated generate group: arg routing, usage errors, and the
/// renderer's dual-mode contract (human value via Info, structured payload via Json). The transform
/// correctness itself lives in <see cref="GeneratorsTests"/>; here we assert the plumbing.
/// </summary>
public class GenerateCommandsTests
{
    private static (Ports ports, FakeRenderer render) Setup(bool jsonMode = false)
    {
        var render = new FakeRenderer { JsonMode = jsonMode };
        return (new Ports(new FakeExecutor(), render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), render);
    }

    // ───────────────────────────────────────────── dual-mode rendering contract
    // Handlers emit BOTH Info (human) and Json (payload) unconditionally; the real ConsoleRenderer
    // routes by --json. These two exercise that end-to-end through the real renderer.

    private static string RenderToStdout(bool jsonMode, Func<Ports, int> run)
    {
        var stdout = new StringWriter();
        var render = new ConsoleRenderer(jsonMode, stdout, new StringWriter());
        run(new Ports(new FakeExecutor(), render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()));
        return stdout.ToString();
    }

    [Fact]
    public void HumanMode_EmitsPlainValue_NotJson()
    {
        var output = RenderToStdout(jsonMode: false, p => GenerateCommands.RunSlug(p, ["Hello World"]));
        Assert.Contains("hello-world", output);
        Assert.DoesNotContain("{", output);  // no JSON envelope in human mode
    }

    [Fact]
    public void JsonMode_EmitsJsonEnvelope_NotPlainValue()
    {
        var output = RenderToStdout(jsonMode: true, p => GenerateCommands.RunSlug(p, ["Hello World"]));
        var doc = JsonDocument.Parse(output);  // parses => the only stdout line is the JSON payload
        Assert.Equal("hello-world", doc.RootElement.GetProperty("value").GetString());
    }

    // ───────────────────────────────────────────────────────────────── uuid

    [Fact]
    public void Uuid_DefaultV4_EmitsCanonicalUuid()
    {
        var (ports, render) = Setup();
        Assert.Equal(0, GenerateCommands.RunUuid(ports, []));
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", Assert.Single(render.Infos));
    }

    [Fact]
    public void Uuid_Upper_EmitsUppercase()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunUuid(ports, ["--upper"]);
        var value = Assert.Single(render.Infos);
        Assert.Equal(value.ToUpperInvariant(), value);
    }

    [Fact]
    public void Uuid_V5_WithNamespaceAndName_IsDeterministic()
    {
        var (ports, render) = Setup();
        Assert.Equal(0, GenerateCommands.RunUuid(ports, ["--v5", "--namespace", "dns", "--name", "python.org"]));
        Assert.Equal("886313e1-3b8a-5372-9b90-0c9aee199e5d", Assert.Single(render.Infos));
    }

    [Fact]
    public void Uuid_V3_MissingNamespace_UsageError()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunUuid(ports, ["--v3", "--name", "x"]));
        Assert.Single(render.Errors);
        Assert.Empty(render.Infos);
    }

    [Fact]
    public void Uuid_V5_InvalidNamespace_Errors()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunUuid(ports, ["--v5", "--namespace", "garbage", "--name", "x"]));
        Assert.Contains("Invalid namespace", Assert.Single(render.Errors));
    }

    // ───────────────────────────────────────────────────────────────── hash

    [Fact]
    public void Hash_DefaultSha256()
    {
        var (ports, render) = Setup();
        Assert.Equal(0, GenerateCommands.RunHash(ports, ["abc"]));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Assert.Single(render.Infos));
    }

    [Fact]
    public void Hash_ExplicitAlgorithm()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunHash(ports, ["abc", "--algorithm", "md5"]);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Assert.Single(render.Infos));
    }

    [Fact]
    public void Hash_UnknownAlgorithm_Errors()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunHash(ports, ["abc", "--algorithm", "crc32"]));
        Assert.Contains("Unknown algorithm", Assert.Single(render.Errors));
    }

    [Fact]
    public void Hash_NoInput_UsageError()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunHash(ports, ["--algorithm", "sha256"]));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void Hash_LeadingDashTokensAreTreatedAsFlags_NotInput()
    {
        // Intended unification: a token starting with '-' is a flag, not hash input (consistent with
        // the shared Args parser and PathArg). So a bare "-5" leaves no positional input -> usage error.
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunHash(ports, ["-5"]));
        Assert.Single(render.Errors);
    }

    // ─────────────────────────────────────────────────────── secret / password

    [Theory]
    [InlineData(0)]
    [InlineData(2000)]
    public void Secret_LengthOutOfRange_Errors(int length)
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunSecret(ports, ["--length", length.ToString()]));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void Secret_DefaultEncoding_IsBase64()
    {
        // Pins secret's default (base64) — the opposite of random-bytes' default (hex). The value
        // must base64-decode back to the requested byte count.
        var (ports, render) = Setup();
        Assert.Equal(0, GenerateCommands.RunSecret(ports, ["--length", "16"]));
        Assert.Equal(16, Convert.FromBase64String(Assert.Single(render.Infos)).Length);
    }

    [Fact]
    public void Secret_HexEncoding_IsLowercaseHex()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunSecret(ports, ["--length", "16", "--encoding", "hex"]);
        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(render.Infos));  // 16 bytes => 32 hex chars
    }

    [Fact]
    public void RandomBytes_DefaultEncoding_IsHex()
    {
        // Pins random-bytes' default (hex) — guards the secret/random-bytes opposite-default pair.
        var (ports, render) = Setup();
        Assert.Equal(0, GenerateCommands.RunRandomBytes(ports, ["--length", "32"]));
        Assert.Matches("^[0-9a-f]{64}$", Assert.Single(render.Infos));  // 32 bytes => 64 hex chars
    }

    [Fact]
    public void RandomBytes_Base64Encoding_SwitchesToBase64()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunRandomBytes(ports, ["--length", "32", "--encoding", "base64"]);
        Assert.Equal(32, Convert.FromBase64String(Assert.Single(render.Infos)).Length);
    }

    [Fact]
    public void RandomBytes_LengthOutOfRange_Errors()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunRandomBytes(ports, ["--length", "9999"]));
        Assert.Single(render.Errors);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(500)]
    public void Password_LengthOutOfRange_Errors(int length)
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunPassword(ports, ["--length", length.ToString()]));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void Password_DefaultLength_Is20Chars()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunPassword(ports, []);
        Assert.Equal(20, Assert.Single(render.Infos).Length);
    }

    [Fact]
    public void NanoId_DefaultLength_Is21Chars()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunNanoId(ports, []);
        Assert.Equal(21, Assert.Single(render.Infos).Length);
    }

    [Fact]
    public void NanoId_CustomLengthAndAlphabet()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunNanoId(ports, ["--length", "8", "--alphabet", "ab"]);
        var value = Assert.Single(render.Infos);
        Assert.Equal(8, value.Length);
        Assert.All(value, c => Assert.Contains(c, "ab"));
    }

    [Fact]
    public void NanoId_AlphabetTooSmall_Errors()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunNanoId(ports, ["--alphabet", "A"]));
        Assert.Single(render.Errors);
    }

    // ─────────────────────────────────────────────────────── base64 / jwt

    [Fact]
    public void Base64Decode_Invalid_Errors()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunBase64Decode(ports, ["!!!not-base64!!!"]));
        Assert.Contains("Invalid base64", Assert.Single(render.Errors));
    }

    [Fact]
    public void Base64Decode_NoArgs_UsageError()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunBase64Decode(ports, []));
        Assert.Single(render.Errors);
    }

    [Fact]
    public void JwtDecode_Valid_EmitsHeaderAndPayloadLines()
    {
        var (ports, render) = Setup();
        var token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.sig";
        Assert.Equal(0, GenerateCommands.RunJwtDecode(ports, [token]));
        Assert.Contains("Header:", render.Infos);
        Assert.Contains("{\"alg\":\"HS256\"}", render.Infos);
    }

    [Fact]
    public void JwtDecode_TooFewSegments_Errors()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunJwtDecode(ports, ["onlyone"]));
        Assert.Single(render.Errors);
    }

    // ───────────────────────────────────────────────────────────── timestamp

    [Fact]
    public void Timestamp_Default_IsIso8601()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunTimestamp(ports, []);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", Assert.Single(render.Infos));
    }

    [Fact]
    public void Timestamp_Unix_IsNumeric()
    {
        var (ports, render) = Setup();
        GenerateCommands.RunTimestamp(ports, ["--unix"]);
        Assert.Matches(@"^\d+$", Assert.Single(render.Infos));
    }

    // ───────────────────────────────────────────────────────────── hash-file

    [Fact]
    public void HashFile_HashesContents()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            var (ports, render) = Setup();
            Assert.Equal(0, GenerateCommands.RunHashFile(ports, [path]));
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Assert.Single(render.Infos));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HashFile_NotFound_Errors()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunHashFile(ports, [Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())]));
        Assert.Contains("File not found", Assert.Single(render.Errors));
    }

    [Fact]
    public void HashFile_MissingPath_UsageError()
    {
        var (ports, render) = Setup();
        Assert.Equal(1, GenerateCommands.RunHashFile(ports, []));
        Assert.Single(render.Errors);
    }
}
