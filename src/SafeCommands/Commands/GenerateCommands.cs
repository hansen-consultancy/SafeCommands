using System.Security.Cryptography;
using SafeCommands.Commands.Generate;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

/// <summary>
/// CLI plumbing for the <c>generate</c> group: parse args (<see cref="Args"/>), call the pure
/// transforms in <see cref="Generate"/>, and render via <see cref="IRenderer"/>. All the real logic
/// lives in the pure modules; these handlers stay thin. Dual-mode output is handled by the renderer:
/// <c>Info</c> emits in human mode (suppressed under --json), <c>Json</c> emits under --json
/// (suppressed in human mode), so each handler unconditionally offers both.
/// </summary>
static class GenerateCommands
{
    private const string NanoIdAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";
    private const int NanoIdDefaultLength = 21;

    private const string PasswordAlphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string PasswordSpecial = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+[]{}|;:,.<>?";

    // hash-file's path argument: the first positional, skipping --algorithm's value. Shared by BOTH
    // the declared Policy and RunHashFile so the token the policy validates is EXACTLY the token the
    // handler hashes. A divergence here is a containment bypass: the handler must never read a path
    // the policy did not check (e.g. a decoy "sha256 <outside> --algorithm sha256").
    internal static readonly PathArg HashFilePath = new PathArg.Positional(0, ["--algorithm"]);

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            new("generate", "uuid", "Generate a UUID (v4 default, or v3/v5/v7)",
                "safe generate uuid [--v3|--v5 --namespace <ns> --name <name>] [--v7] [--upper]",
                SafetyLevel.ReadOnly, RunUuid),

            new("generate", "secret", "Generate a cryptographic random secret",
                "safe generate secret [--length <bytes>] [--encoding base64|hex]",
                SafetyLevel.ReadOnly, RunSecret),

            new("generate", "password", "Generate a random password",
                "safe generate password [--length <chars>] [--special]",
                SafetyLevel.ReadOnly, RunPassword),

            new("generate", "hash", "Hash a string (SHA256 default)",
                "safe generate hash <input> [--algorithm sha256|sha384|sha512|md5]",
                SafetyLevel.ReadOnly, RunHash),

            new("generate", "random-bytes", "Generate cryptographic random bytes",
                "safe generate random-bytes [--length <bytes>] [--encoding hex|base64]",
                SafetyLevel.ReadOnly, RunRandomBytes),

            new("generate", "timestamp", "Get current timestamp",
                "safe generate timestamp [--unix|--unix-ms|--rfc3339]",
                SafetyLevel.ReadOnly, RunTimestamp),

            new("generate", "nanoid", "Generate a short URL-safe ID",
                "safe generate nanoid [--length <chars>] [--alphabet <chars>]",
                SafetyLevel.ReadOnly, RunNanoId),

            new("generate", "base64-encode", "Encode a string to base64",
                "safe generate base64-encode <string>",
                SafetyLevel.ReadOnly, RunBase64Encode),

            new("generate", "base64-decode", "Decode a base64 string",
                "safe generate base64-decode <string>",
                SafetyLevel.ReadOnly, RunBase64Decode),

            new("generate", "url-encode", "Percent-encode a string for URLs",
                "safe generate url-encode <string>",
                SafetyLevel.ReadOnly, RunUrlEncode),

            new("generate", "url-decode", "Decode a percent-encoded string",
                "safe generate url-decode <string>",
                SafetyLevel.ReadOnly, RunUrlDecode),

            new("generate", "jwt-decode", "Decode a JWT payload (no verification)",
                "safe generate jwt-decode <token>",
                SafetyLevel.ReadOnly, RunJwtDecode),

            new("generate", "hash-file", "Hash a file's contents (SHA256 default)",
                "safe generate hash-file <path> [--algorithm sha256|sha384|sha512|md5]",
                SafetyLevel.ReadOnly, RunHashFile)
                { Policy = Policy.Default.RequirePathWithinProject(HashFilePath) },

            new("generate", "slug", "Convert a string to a URL-safe slug",
                "safe generate slug <string>",
                SafetyLevel.ReadOnly, RunSlug),
        ]);
    }

    // ── UUID ───────────────────────────────────────────────────────────────

    internal static int RunUuid(Ports p, string[] args)
    {
        var upper = Args.HasFlag(args, "--upper");

        if (Args.HasFlag(args, "--v3")) return NamespacedUuid(p, args, 3, upper);
        if (Args.HasFlag(args, "--v5")) return NamespacedUuid(p, args, 5, upper);
        if (Args.HasFlag(args, "--v7"))
        {
            Span<byte> random = stackalloc byte[16];
            RandomNumberGenerator.Fill(random);
            return OutputUuid(p, Uuid.V7(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), random), 7, upper);
        }

        return OutputUuid(p, Guid.NewGuid(), 4, upper);
    }

    private static int NamespacedUuid(Ports p, string[] args, int version, bool upper)
    {
        var nsArg = Args.Value(args, "--namespace");
        var name = Args.Value(args, "--name");

        if (nsArg == null || name == null)
        {
            p.Render.Error($"Usage: safe generate uuid --v{version} --namespace <dns|url|oid|x500|guid> --name <value>");
            return 1;
        }

        var ns = Uuid.ResolveNamespace(nsArg);
        if (ns == null)
        {
            p.Render.Error($"Invalid namespace: '{nsArg}'. Use dns, url, oid, x500, or a valid GUID.");
            return 1;
        }

        return OutputUuid(p, Uuid.NameBased(ns.Value, name, version), version, upper);
    }

    private static int OutputUuid(Ports p, Guid uuid, int version, bool upper)
    {
        var value = Uuid.Format(uuid, upper);
        p.Render.Info(value);
        p.Render.Json(new { version, value });
        return 0;
    }

    // ── Secret ─────────────────────────────────────────────────────────────

    internal static int RunSecret(Ports p, string[] args)
    {
        var length = Args.IntValue(args, "--length", 32);
        if (length < 1 || length > 1024)
        {
            p.Render.Error("Length must be between 1 and 1024 bytes.");
            return 1;
        }

        // Default encoding base64; only "hex" selects hex.
        var hex = Args.Value(args, "--encoding")?.ToLowerInvariant() == "hex";
        var value = RandomValues.Encode(RandomNumberGenerator.GetBytes(length), hex);

        p.Render.Info(value);
        p.Render.Json(new { length, encoding = hex ? "hex" : "base64", value });
        return 0;
    }

    // ── Password ───────────────────────────────────────────────────────────

    internal static int RunPassword(Ports p, string[] args)
    {
        var length = Args.IntValue(args, "--length", 20);
        if (length < 8 || length > 256)
        {
            p.Render.Error("Length must be between 8 and 256 characters.");
            return 1;
        }

        var useSpecial = Args.HasFlag(args, "--special");
        var alphabet = useSpecial ? PasswordSpecial : PasswordAlphanumeric;
        var value = RandomValues.FromAlphabet(alphabet, length, RandomNumberGenerator.GetInt32);

        p.Render.Info(value);
        p.Render.Json(new { length, special = useSpecial, value });
        return 0;
    }

    // ── Hash ───────────────────────────────────────────────────────────────

    internal static int RunHash(Ports p, string[] args)
    {
        var algorithm = Args.Value(args, "--algorithm")?.ToLowerInvariant() ?? "sha256";
        var input = string.Join(" ", Args.Positionals(args, "--algorithm"));

        if (string.IsNullOrEmpty(input))
        {
            p.Render.Error("Usage: safe generate hash <input> [--algorithm sha256|sha384|sha512|md5]");
            return 1;
        }

        var value = Hashing.HashText(input, algorithm);
        if (value == null)
        {
            p.Render.Error($"Unknown algorithm '{algorithm}'. Use sha256, sha384, sha512, or md5.");
            return 1;
        }

        p.Render.Info(value);
        p.Render.Json(new { algorithm, input, value });
        return 0;
    }

    // ── Random Bytes ───────────────────────────────────────────────────────

    internal static int RunRandomBytes(Ports p, string[] args)
    {
        var length = Args.IntValue(args, "--length", 32);
        if (length < 1 || length > 1024)
        {
            p.Render.Error("Length must be between 1 and 1024 bytes.");
            return 1;
        }

        // Default encoding hex; only "base64" selects base64.
        var hex = Args.Value(args, "--encoding")?.ToLowerInvariant() != "base64";
        var value = RandomValues.Encode(RandomNumberGenerator.GetBytes(length), hex);

        p.Render.Info(value);
        p.Render.Json(new { length, encoding = hex ? "hex" : "base64", value });
        return 0;
    }

    // ── Timestamp ──────────────────────────────────────────────────────────

    internal static int RunTimestamp(Ports p, string[] args)
    {
        var now = DateTimeOffset.UtcNow;

        if (Args.HasFlag(args, "--unix"))
        {
            var value = Timestamps.Unix(now);
            p.Render.Info(value.ToString());
            p.Render.Json(new { format = "unix", value });
            return 0;
        }

        if (Args.HasFlag(args, "--unix-ms"))
        {
            var value = Timestamps.UnixMs(now);
            p.Render.Info(value.ToString());
            p.Render.Json(new { format = "unix-ms", value });
            return 0;
        }

        var iso = Timestamps.Iso8601(now);
        p.Render.Info(iso);
        p.Render.Json(new { format = "iso8601", value = iso });
        return 0;
    }

    // ── NanoID ─────────────────────────────────────────────────────────────

    internal static int RunNanoId(Ports p, string[] args)
    {
        var length = Args.IntValue(args, "--length", NanoIdDefaultLength);
        if (length < 1 || length > 256)
        {
            p.Render.Error("Length must be between 1 and 256 characters.");
            return 1;
        }

        var alphabet = Args.Value(args, "--alphabet") ?? NanoIdAlphabet;
        if (alphabet.Length < 2)
        {
            p.Render.Error("Alphabet must have at least 2 characters.");
            return 1;
        }

        var value = RandomValues.FromAlphabet(alphabet, length, RandomNumberGenerator.GetInt32);

        p.Render.Info(value);
        p.Render.Json(new { length, alphabetSize = alphabet.Length, value });
        return 0;
    }

    // ── Base64 Encode/Decode ──────────────────────────────────────────────

    internal static int RunBase64Encode(Ports p, string[] args)
    {
        var input = string.Join(" ", args);
        if (string.IsNullOrEmpty(input))
        {
            p.Render.Error("Usage: safe generate base64-encode <string>");
            return 1;
        }

        var value = Codec.Base64Encode(input);
        p.Render.Info(value);
        p.Render.Json(new { input, value });
        return 0;
    }

    internal static int RunBase64Decode(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe generate base64-decode <string>");
            return 1;
        }

        var input = args[0];
        var value = Codec.Base64Decode(input);
        if (value == null)
        {
            p.Render.Error("Invalid base64 input.");
            return 1;
        }

        p.Render.Info(value);
        p.Render.Json(new { input, value });
        return 0;
    }

    // ── URL Encode/Decode ──────────────────────────────────────────────────

    internal static int RunUrlEncode(Ports p, string[] args)
    {
        var input = string.Join(" ", args);
        if (string.IsNullOrEmpty(input))
        {
            p.Render.Error("Usage: safe generate url-encode <string>");
            return 1;
        }

        var value = Codec.UrlEncode(input);
        p.Render.Info(value);
        p.Render.Json(new { input, value });
        return 0;
    }

    internal static int RunUrlDecode(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe generate url-decode <string>");
            return 1;
        }

        var input = args[0];
        var value = Codec.UrlDecode(input);
        p.Render.Info(value);
        p.Render.Json(new { input, value });
        return 0;
    }

    // ── JWT Decode ─────────────────────────────────────────────────────────

    internal static int RunJwtDecode(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe generate jwt-decode <token>");
            return 1;
        }

        var (header, payload, error) = Jwt.Decode(args[0]);
        switch (error)
        {
            case Jwt.Error.TooFewSegments:
                p.Render.Error("Invalid JWT: expected at least 2 dot-separated segments.");
                return 1;
            case Jwt.Error.BadBase64:
                p.Render.Error("Invalid JWT: failed to decode base64url segments.");
                return 1;
        }

        p.Render.Info("Header:");
        p.Render.Info(header!);
        p.Render.Info("");
        p.Render.Info("Payload:");
        p.Render.Info(payload!);
        p.Render.Json(new { header, payload });
        return 0;
    }

    // ── Hash File ──────────────────────────────────────────────────────────

    internal static int RunHashFile(Ports p, string[] args)
    {
        var algorithm = Args.Value(args, "--algorithm")?.ToLowerInvariant() ?? "sha256";
        // Use the SAME selector the policy validated (HashFilePath) so the token we hash is exactly
        // the one the containment check approved — see HashFilePath for why divergence is a bypass.
        var path = HashFilePath.Extract(args);

        if (path == null)
        {
            p.Render.Error("Usage: safe generate hash-file <path> [--algorithm sha256|sha384|sha512|md5]");
            return 1;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            p.Render.Error($"File not found: {fullPath}");
            return 1;
        }

        using var stream = File.OpenRead(fullPath);
        var value = Hashing.Hash(stream, algorithm);
        if (value == null)
        {
            p.Render.Error($"Unknown algorithm '{algorithm}'. Use sha256, sha384, sha512, or md5.");
            return 1;
        }

        p.Render.Info(value);
        p.Render.Json(new { algorithm, path = fullPath, value });
        return 0;
    }

    // ── Slug ───────────────────────────────────────────────────────────────

    internal static int RunSlug(Ports p, string[] args)
    {
        var input = string.Join(" ", args);
        if (string.IsNullOrEmpty(input))
        {
            p.Render.Error("Usage: safe generate slug <string>");
            return 1;
        }

        var value = Slug.Make(input);
        p.Render.Info(value);
        p.Render.Json(new { input, value });
        return 0;
    }
}
