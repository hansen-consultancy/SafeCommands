using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SafeCommands.Infrastructure;
using SafeCommands.Registry;

namespace SafeCommands.Commands;

static class GenerateCommands
{
    private static readonly string NanoIdAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";
    private const int NanoIdDefaultLength = 21;

    private static readonly string PasswordAlphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly string PasswordSpecial = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+[]{}|;:,.<>?";

    private static readonly Dictionary<string, Guid> WellKnownNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dns"]  = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8"),
        ["url"]  = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8"),
        ["oid"]  = Guid.Parse("6ba7b812-9dad-11d1-80b4-00c04fd430c8"),
        ["x500"] = Guid.Parse("6ba7b814-9dad-11d1-80b4-00c04fd430c8"),
    };

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
                SafetyLevel.ReadOnly, RunHashFile),

            new("generate", "slug", "Convert a string to a URL-safe slug",
                "safe generate slug <string>",
                SafetyLevel.ReadOnly, RunSlug),
        ]);
    }

    // ── UUID ───────────────────────────────────────────────────────────────

    private static int RunUuid(string[] args, bool json)
    {
        var upper = HasFlag(args, "--upper");

        if (HasFlag(args, "--v3"))
            return RunNamespacedUuid(args, 3, upper, json);
        if (HasFlag(args, "--v5"))
            return RunNamespacedUuid(args, 5, upper, json);
        if (HasFlag(args, "--v7"))
            return RunUuidV7(upper, json);

        // Default: v4
        var uuid = Guid.NewGuid();
        return OutputUuid(uuid, 4, upper, json);
    }

    private static int RunNamespacedUuid(string[] args, int version, bool upper, bool json)
    {
        var nsArg = GetOption(args, "--namespace");
        var name = GetOption(args, "--name");

        if (nsArg == null || name == null)
        {
            OutputFormatter.WriteError($"Usage: safe generate uuid --v{version} --namespace <dns|url|oid|x500|guid> --name <value>");
            return 1;
        }

        Guid nsGuid;
        if (WellKnownNamespaces.TryGetValue(nsArg, out var wellKnown))
            nsGuid = wellKnown;
        else if (!Guid.TryParse(nsArg, out nsGuid))
        {
            OutputFormatter.WriteError($"Invalid namespace: '{nsArg}'. Use dns, url, oid, x500, or a valid GUID.");
            return 1;
        }

        var uuid = version == 3
            ? CreateNameBasedUuid(nsGuid, name, MD5.Create(), 3)
            : CreateNameBasedUuid(nsGuid, name, SHA1.Create(), 5);

        return OutputUuid(uuid, version, upper, json);
    }

    private static int RunUuidV7(bool upper, bool json)
    {
        var uuid = CreateUuidV7();
        return OutputUuid(uuid, 7, upper, json);
    }

    private static int OutputUuid(Guid uuid, int version, bool upper, bool json)
    {
        var value = upper ? uuid.ToString("D").ToUpperInvariant() : uuid.ToString("D");
        if (json)
            OutputFormatter.WriteJson(new { version, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    /// <summary>Create a UUID v3 (MD5) or v5 (SHA1) from a namespace GUID and a name string.</summary>
    private static Guid CreateNameBasedUuid(Guid namespaceId, string name, HashAlgorithm algorithm, int version)
    {
        // Convert namespace GUID to big-endian bytes per RFC 4122
        var nsBytes = namespaceId.ToByteArray();
        SwapGuidEndianness(nsBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, input, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, nsBytes.Length, nameBytes.Length);

        var hash = algorithm.ComputeHash(input);
        algorithm.Dispose();

        // Take first 16 bytes, set version and variant
        var result = new byte[16];
        Array.Copy(hash, result, 16);
        result[6] = (byte)((result[6] & 0x0F) | (version << 4)); // version
        result[8] = (byte)((result[8] & 0x3F) | 0x80);           // variant 10xx

        SwapGuidEndianness(result); // back to .NET mixed-endian
        return new Guid(result);
    }

    /// <summary>Create a UUID v7 (Unix epoch ms + random).</summary>
    private static Guid CreateUuidV7()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // First 6 bytes = 48-bit timestamp (big-endian)
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        // Version: 0111 in bits 48-51
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        // Variant: 10xx in bits 64-65
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // Convert from big-endian (RFC) to .NET mixed-endian
        SwapGuidEndianness(bytes);
        return new Guid(bytes);
    }

    /// <summary>Swap between .NET's mixed-endian GUID layout and RFC 4122 big-endian.</summary>
    private static void SwapGuidEndianness(byte[] b)
    {
        // .NET stores first 3 groups as little-endian; RFC 4122 is big-endian
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
    }

    // ── Secret ─────────────────────────────────────────────────────────────

    private static int RunSecret(string[] args, bool json)
    {
        var length = GetIntOption(args, "--length", 32);
        if (length < 1 || length > 1024)
        {
            OutputFormatter.WriteError("Length must be between 1 and 1024 bytes.");
            return 1;
        }

        var encoding = GetOption(args, "--encoding")?.ToLowerInvariant() ?? "base64";

        var bytes = RandomNumberGenerator.GetBytes(length);
        var value = encoding switch
        {
            "hex" => Convert.ToHexString(bytes).ToLowerInvariant(),
            "base64" => Convert.ToBase64String(bytes),
            _ => Convert.ToBase64String(bytes),
        };

        if (json)
            OutputFormatter.WriteJson(new { length, encoding, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── Password ───────────────────────────────────────────────────────────

    private static int RunPassword(string[] args, bool json)
    {
        var length = GetIntOption(args, "--length", 20);
        if (length < 8 || length > 256)
        {
            OutputFormatter.WriteError("Length must be between 8 and 256 characters.");
            return 1;
        }

        var useSpecial = HasFlag(args, "--special");
        var alphabet = useSpecial ? PasswordSpecial : PasswordAlphanumeric;

        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        var value = new string(chars);

        if (json)
            OutputFormatter.WriteJson(new { length, special = useSpecial, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── Hash ───────────────────────────────────────────────────────────────

    private static int RunHash(string[] args, bool json)
    {
        // Parse: everything that isn't a flag is the input
        var algorithm = GetOption(args, "--algorithm")?.ToLowerInvariant() ?? "sha256";
        var inputParts = args.Where(a => !a.StartsWith("--") && a != algorithm).ToArray();

        // Also skip the value that follows --algorithm
        var algoIdx = Array.IndexOf(args, "--algorithm");
        var input = string.Join(" ", args.Where((a, i) =>
            !a.StartsWith("--") &&
            !(algoIdx >= 0 && i == algoIdx + 1)));

        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.WriteError("Usage: safe generate hash <input> [--algorithm sha256|sha384|sha512|md5]");
            return 1;
        }

        var inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes;

        switch (algorithm)
        {
            case "sha256":
                hashBytes = SHA256.HashData(inputBytes);
                break;
            case "sha384":
                hashBytes = SHA384.HashData(inputBytes);
                break;
            case "sha512":
                hashBytes = SHA512.HashData(inputBytes);
                break;
            case "md5":
                hashBytes = MD5.HashData(inputBytes);
                break;
            default:
                OutputFormatter.WriteError($"Unknown algorithm '{algorithm}'. Use sha256, sha384, sha512, or md5.");
                return 1;
        }

        var value = Convert.ToHexString(hashBytes).ToLowerInvariant();

        if (json)
            OutputFormatter.WriteJson(new { algorithm, input, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── Random Bytes ───────────────────────────────────────────────────────

    private static int RunRandomBytes(string[] args, bool json)
    {
        var length = GetIntOption(args, "--length", 32);
        if (length < 1 || length > 1024)
        {
            OutputFormatter.WriteError("Length must be between 1 and 1024 bytes.");
            return 1;
        }

        var encoding = GetOption(args, "--encoding")?.ToLowerInvariant() ?? "hex";

        var bytes = RandomNumberGenerator.GetBytes(length);
        var value = encoding switch
        {
            "base64" => Convert.ToBase64String(bytes),
            "hex" => Convert.ToHexString(bytes).ToLowerInvariant(),
            _ => Convert.ToHexString(bytes).ToLowerInvariant(),
        };

        if (json)
            OutputFormatter.WriteJson(new { length, encoding, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── Timestamp ──────────────────────────────────────────────────────────

    private static int RunTimestamp(string[] args, bool json)
    {
        var now = DateTimeOffset.UtcNow;

        if (HasFlag(args, "--unix"))
        {
            var value = now.ToUnixTimeSeconds();
            if (json)
                OutputFormatter.WriteJson(new { format = "unix", value });
            else
                Console.WriteLine(value);
            return 0;
        }

        if (HasFlag(args, "--unix-ms"))
        {
            var value = now.ToUnixTimeMilliseconds();
            if (json)
                OutputFormatter.WriteJson(new { format = "unix-ms", value });
            else
                Console.WriteLine(value);
            return 0;
        }

        // Default: ISO 8601 / RFC 3339
        var iso = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (json)
            OutputFormatter.WriteJson(new { format = "iso8601", value = iso });
        else
            Console.WriteLine(iso);
        return 0;
    }

    // ── NanoID ─────────────────────────────────────────────────────────────

    private static int RunNanoId(string[] args, bool json)
    {
        var length = GetIntOption(args, "--length", NanoIdDefaultLength);
        if (length < 1 || length > 256)
        {
            OutputFormatter.WriteError("Length must be between 1 and 256 characters.");
            return 1;
        }

        var alphabet = GetOption(args, "--alphabet") ?? NanoIdAlphabet;
        if (alphabet.Length < 2)
        {
            OutputFormatter.WriteError("Alphabet must have at least 2 characters.");
            return 1;
        }

        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        var value = new string(chars);

        if (json)
            OutputFormatter.WriteJson(new { length, alphabetSize = alphabet.Length, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── Base64 Encode/Decode ──────────────────────────────────────────────

    private static int RunBase64Encode(string[] args, bool json)
    {
        var input = string.Join(" ", args);
        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.WriteError("Usage: safe generate base64-encode <string>");
            return 1;
        }

        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(input));

        if (json)
            OutputFormatter.WriteJson(new { input, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    private static int RunBase64Decode(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe generate base64-decode <string>");
            return 1;
        }

        var input = args[0];
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(input);
        }
        catch (FormatException)
        {
            OutputFormatter.WriteError("Invalid base64 input.");
            return 1;
        }

        var value = Encoding.UTF8.GetString(bytes);

        if (json)
            OutputFormatter.WriteJson(new { input, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── URL Encode/Decode ──────────────────────────────────────────────────

    private static int RunUrlEncode(string[] args, bool json)
    {
        var input = string.Join(" ", args);
        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.WriteError("Usage: safe generate url-encode <string>");
            return 1;
        }

        var value = Uri.EscapeDataString(input);

        if (json)
            OutputFormatter.WriteJson(new { input, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    private static int RunUrlDecode(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe generate url-decode <string>");
            return 1;
        }

        var input = args[0];
        var value = Uri.UnescapeDataString(input);

        if (json)
            OutputFormatter.WriteJson(new { input, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── JWT Decode ─────────────────────────────────────────────────────────

    private static int RunJwtDecode(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe generate jwt-decode <token>");
            return 1;
        }

        var token = args[0];
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            OutputFormatter.WriteError("Invalid JWT: expected at least 2 dot-separated segments.");
            return 1;
        }

        string header, payload;
        try
        {
            header = DecodeBase64Url(parts[0]);
            payload = DecodeBase64Url(parts[1]);
        }
        catch (Exception)
        {
            OutputFormatter.WriteError("Invalid JWT: failed to decode base64url segments.");
            return 1;
        }

        if (json)
        {
            OutputFormatter.WriteJson(new { header, payload });
        }
        else
        {
            Console.WriteLine("Header:");
            Console.WriteLine(header);
            Console.WriteLine();
            Console.WriteLine("Payload:");
            Console.WriteLine(payload);
        }
        return 0;
    }

    private static string DecodeBase64Url(string input)
    {
        // base64url -> base64: replace - with +, _ with /, add padding
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    // ── Hash File ──────────────────────────────────────────────────────────

    private static int RunHashFile(string[] args, bool json)
    {
        // Parse out --algorithm and its value to find the file path
        var algorithm = GetOption(args, "--algorithm")?.ToLowerInvariant() ?? "sha256";
        var path = args.FirstOrDefault(a => !a.StartsWith("--") && !a.Equals(GetOption(args, "--algorithm"), StringComparison.OrdinalIgnoreCase));

        if (path == null)
        {
            OutputFormatter.WriteError("Usage: safe generate hash-file <path> [--algorithm sha256|sha384|sha512|md5]");
            return 1;
        }

        // Path validation: must be within project directory
        var fullPath = Path.GetFullPath(path);
        var projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (!projectRoot.EndsWith(Path.DirectorySeparatorChar))
            projectRoot += Path.DirectorySeparatorChar;

        if (!fullPath.Equals(projectRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            OutputFormatter.WriteBlocked("generate hash-file",
                $"Path '{fullPath}' is outside the project directory",
                $"All file operations are sandboxed to: {projectRoot.TrimEnd(Path.DirectorySeparatorChar)}");
            return 1;
        }

        if (!File.Exists(fullPath))
        {
            OutputFormatter.WriteError($"File not found: {fullPath}");
            return 1;
        }

        byte[] hashBytes;
        using var stream = File.OpenRead(fullPath);

        switch (algorithm)
        {
            case "sha256":
                hashBytes = SHA256.HashData(stream);
                break;
            case "sha384":
                hashBytes = SHA384.HashData(stream);
                break;
            case "sha512":
                hashBytes = SHA512.HashData(stream);
                break;
            case "md5":
                hashBytes = MD5.HashData(stream);
                break;
            default:
                OutputFormatter.WriteError($"Unknown algorithm '{algorithm}'. Use sha256, sha384, sha512, or md5.");
                return 1;
        }

        var value = Convert.ToHexString(hashBytes).ToLowerInvariant();

        if (json)
            OutputFormatter.WriteJson(new { algorithm, path = fullPath, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── Slug ───────────────────────────────────────────────────────────────

    private static int RunSlug(string[] args, bool json)
    {
        var input = string.Join(" ", args);
        if (string.IsNullOrEmpty(input))
        {
            OutputFormatter.WriteError("Usage: safe generate slug <string>");
            return 1;
        }

        // Lowercase, replace non-alphanumeric runs with hyphens, trim hyphens
        var value = Regex.Replace(input.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

        if (json)
            OutputFormatter.WriteJson(new { input, value });
        else
            Console.WriteLine(value);
        return 0;
    }

    // ── Arg parsing helpers ────────────────────────────────────────────────

    private static bool HasFlag(string[] args, string flag)
        => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static int GetIntOption(string[] args, string name, int defaultValue)
    {
        var val = GetOption(args, name);
        return val != null && int.TryParse(val, out var n) ? n : defaultValue;
    }
}
