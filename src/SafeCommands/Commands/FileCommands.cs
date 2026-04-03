using Spectre.Console;
using SafeCommands.Infrastructure;
using SafeCommands.Registry;

namespace SafeCommands.Commands;

static class FileCommands
{
    private static readonly HashSet<string> SafeDeleteDirs =
    [
        "bin", "obj", "node_modules", ".tmp", "tmp", "__pycache__",
        ".cache", "dist", "build", "out", ".next", ".nuget",
        "TestResults", "coverage", ".pytest_cache", ".parcel-cache",
        ".angular", ".turbo", ".svelte-kit", ".output", ".vercel",
        "target", // Rust/Java
    ];

    private static readonly HashSet<string> LockFiles =
    [
        ".git/index.lock",
        ".git/refs/heads/*.lock",
        ".git/config.lock",
        ".git/HEAD.lock",
    ];

    private static readonly HashSet<string> TempPatterns =
    [
        "*.tmp", "*.temp", "*.bak", "*.swp", "*.swo", "*~",
        "thumbs.db", ".ds_store", "desktop.ini",
    ];

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
            new("file", "list", "List directory contents", "safe file list [<path>]", SafetyLevel.ReadOnly, RunList),
            new("file", "read", "Read file content", "safe file read <path> [--lines <n>]", SafetyLevel.ReadOnly, RunRead),
            new("file", "exists", "Check if file or directory exists", "safe file exists <path>", SafetyLevel.ReadOnly, RunExists),
            new("file", "info", "Show file metadata", "safe file info <path>", SafetyLevel.ReadOnly, RunInfo),
            new("file", "find", "Find files by pattern", "safe file find <pattern> [--in <dir>]", SafetyLevel.ReadOnly, RunFind),
            new("file", "tree", "Show directory tree", "safe file tree [<path>] [--depth <n>]", SafetyLevel.ReadOnly, RunTree),

            // Safe writes
            new("file", "mkdir", "Create directory", "safe file mkdir <path>", SafetyLevel.SafeWrite, RunMkdir),
            new("file", "copy", "Copy file (no overwrite)", "safe file copy <src> <dest>", SafetyLevel.SafeWrite, RunCopy),
            new("file", "write", "Write to new file (no overwrite)", "safe file write <path> --content <text>", SafetyLevel.SafeWrite, RunWrite),

            // Targeted writes
            new("file", "delete-tracked", "Delete a git-tracked file", "safe file delete-tracked <file>", SafetyLevel.TargetedWrite, RunDeleteTracked),
            new("file", "delete-temp", "Delete temp/cache/build files", "safe file delete-temp [<path>]", SafetyLevel.TargetedWrite, RunDeleteTemp),
            new("file", "delete-locks", "Delete lock files", "safe file delete-locks", SafetyLevel.TargetedWrite, RunDeleteLocks),
            new("file", "delete-pattern", "Delete files matching pattern in safe dirs", "safe file delete-pattern <glob> --in <dir>", SafetyLevel.TargetedWrite, RunDeletePattern),
            new("file", "move", "Move/rename git-tracked file", "safe file move <src> <dest>", SafetyLevel.TargetedWrite, RunMove),
        ]);
    }

    // === Read-only ===

    private static int RunList(string[] args, bool json)
    {
        var path = args.Length > 0 ? args[0] : ".";
        if (!Directory.Exists(path))
        {
            OutputFormatter.WriteError($"Directory not found: {path}");
            return 1;
        }

        var entries = Directory.GetFileSystemEntries(path)
            .Select(e => new
            {
                name = Path.GetFileName(e),
                type = Directory.Exists(e) ? "dir" : "file",
                size = File.Exists(e) ? new FileInfo(e).Length : 0
            })
            .OrderBy(e => e.type == "file" ? 1 : 0)
            .ThenBy(e => e.name)
            .ToArray();

        if (json)
        {
            OutputFormatter.WriteJson(new { path = Path.GetFullPath(path), entries });
        }
        else
        {
            foreach (var entry in entries)
            {
                var prefix = entry.type == "dir" ? "[blue]" : "[default]";
                var suffix = entry.type == "dir" ? "/[/]" : "[/]";
                Spectre.Console.AnsiConsole.MarkupLine($"{prefix}{entry.name.EscapeMarkup()}{suffix}");
            }
        }
        return 0;
    }

    private static int RunRead(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file read <path>"); return 1; }
        var path = args[0];
        if (!File.Exists(path))
        {
            OutputFormatter.WriteError($"File not found: {path}");
            return 1;
        }

        var lines = -1;
        var linesIdx = Array.IndexOf(args, "--lines");
        if (linesIdx >= 0 && linesIdx + 1 < args.Length)
            int.TryParse(args[linesIdx + 1], out lines);

        var content = lines > 0
            ? string.Join('\n', File.ReadLines(path).Take(lines))
            : File.ReadAllText(path);

        if (json)
            OutputFormatter.WriteJson(new { path = Path.GetFullPath(path), content, lineCount = content.Split('\n').Length });
        else
            Console.Write(content);

        return 0;
    }

    private static int RunExists(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file exists <path>"); return 1; }
        var path = args[0];
        var fileExists = File.Exists(path);
        var dirExists = Directory.Exists(path);
        var exists = fileExists || dirExists;
        var type = fileExists ? "file" : dirExists ? "directory" : "none";

        if (json)
            OutputFormatter.WriteJson(new { path, exists, type });
        else
            Console.WriteLine(exists ? $"Exists ({type}): {Path.GetFullPath(path)}" : $"Not found: {path}");

        return exists ? 0 : 1;
    }

    private static int RunInfo(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file info <path>"); return 1; }
        var path = args[0];

        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            if (json)
                OutputFormatter.WriteJson(new { path = fi.FullName, type = "file", size = fi.Length, created = fi.CreationTimeUtc, modified = fi.LastWriteTimeUtc, readOnly = fi.IsReadOnly });
            else
            {
                Console.WriteLine($"Path:     {fi.FullName}");
                Console.WriteLine($"Type:     file");
                Console.WriteLine($"Size:     {fi.Length:N0} bytes");
                Console.WriteLine($"Created:  {fi.CreationTimeUtc:u}");
                Console.WriteLine($"Modified: {fi.LastWriteTimeUtc:u}");
                Console.WriteLine($"ReadOnly: {fi.IsReadOnly}");
            }
            return 0;
        }

        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            if (json)
                OutputFormatter.WriteJson(new { path = di.FullName, type = "directory", created = di.CreationTimeUtc, modified = di.LastWriteTimeUtc });
            else
            {
                Console.WriteLine($"Path:     {di.FullName}");
                Console.WriteLine($"Type:     directory");
                Console.WriteLine($"Created:  {di.CreationTimeUtc:u}");
                Console.WriteLine($"Modified: {di.LastWriteTimeUtc:u}");
            }
            return 0;
        }

        OutputFormatter.WriteError($"Not found: {path}");
        return 1;
    }

    private static int RunFind(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file find <pattern> [--in <dir>]"); return 1; }

        var pattern = args[0];
        var dir = ".";
        var inIdx = Array.IndexOf(args, "--in");
        if (inIdx >= 0 && inIdx + 1 < args.Length)
            dir = args[inIdx + 1];

        if (!Directory.Exists(dir))
        {
            OutputFormatter.WriteError($"Directory not found: {dir}");
            return 1;
        }

        try
        {
            var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f))
                .Take(500)
                .ToArray();

            if (json)
                OutputFormatter.WriteJson(new { pattern, directory = Path.GetFullPath(dir), count = files.Length, files });
            else
                foreach (var file in files)
                    Console.WriteLine(file);

            return 0;
        }
        catch (Exception ex)
        {
            OutputFormatter.WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunTree(string[] args, bool json)
    {
        var path = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : ".";
        var maxDepth = 3;
        var depthIdx = Array.IndexOf(args, "--depth");
        if (depthIdx >= 0 && depthIdx + 1 < args.Length)
            int.TryParse(args[depthIdx + 1], out maxDepth);

        if (!Directory.Exists(path))
        {
            OutputFormatter.WriteError($"Directory not found: {path}");
            return 1;
        }

        if (json)
        {
            var tree = BuildTree(path, maxDepth, 0);
            OutputFormatter.WriteJson(tree);
        }
        else
        {
            PrintTree(path, "", maxDepth, 0);
        }
        return 0;
    }

    private static object BuildTree(string path, int maxDepth, int depth)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;

        if (depth >= maxDepth || !Directory.Exists(path))
            return new { name, type = Directory.Exists(path) ? "dir" : "file" };

        var children = Directory.GetFileSystemEntries(path)
            .Where(e => !Path.GetFileName(e).StartsWith('.'))
            .OrderBy(e => File.Exists(e) ? 1 : 0)
            .ThenBy(e => Path.GetFileName(e))
            .Select(e => BuildTree(e, maxDepth, depth + 1))
            .ToArray();

        return new { name, type = "dir", children };
    }

    private static void PrintTree(string path, string prefix, int maxDepth, int depth)
    {
        if (depth >= maxDepth) return;

        var entries = Directory.GetFileSystemEntries(path)
            .Where(e => !Path.GetFileName(e).StartsWith('.'))
            .OrderBy(e => File.Exists(e) ? 1 : 0)
            .ThenBy(e => Path.GetFileName(e))
            .ToArray();

        for (int i = 0; i < entries.Length; i++)
        {
            var isLast = i == entries.Length - 1;
            var connector = isLast ? "└── " : "├── ";
            var name = Path.GetFileName(entries[i]);
            var isDir = Directory.Exists(entries[i]);

            Console.WriteLine($"{prefix}{connector}{name}{(isDir ? "/" : "")}");

            if (isDir)
            {
                var newPrefix = prefix + (isLast ? "    " : "│   ");
                PrintTree(entries[i], newPrefix, maxDepth, depth + 1);
            }
        }
    }

    // === Safe writes ===

    private static int RunMkdir(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file mkdir <path>"); return 1; }
        var path = args[0];
        Directory.CreateDirectory(path);
        if (json)
            OutputFormatter.WriteJson(new { created = Path.GetFullPath(path) });
        else
            OutputFormatter.WriteSuccess($"Created: {Path.GetFullPath(path)}");
        return 0;
    }

    private static int RunCopy(string[] args, bool json)
    {
        if (args.Length < 2) { OutputFormatter.WriteError("Usage: safe file copy <src> <dest>"); return 1; }
        var src = args[0];
        var dest = args[1];

        if (!File.Exists(src)) { OutputFormatter.WriteError($"Source not found: {src}"); return 1; }
        if (File.Exists(dest))
        {
            OutputFormatter.WriteBlocked("file copy", "Destination already exists - overwrite not allowed",
                "Delete the destination first or choose a different name");
            return 1;
        }

        File.Copy(src, dest);
        if (json)
            OutputFormatter.WriteJson(new { source = Path.GetFullPath(src), destination = Path.GetFullPath(dest) });
        else
            OutputFormatter.WriteSuccess($"Copied: {src} -> {dest}");
        return 0;
    }

    private static int RunWrite(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file write <path> --content <text>"); return 1; }
        var path = args[0];

        if (File.Exists(path))
        {
            OutputFormatter.WriteBlocked("file write", "File already exists - overwrite not allowed",
                "Use a different path or delete the file first");
            return 1;
        }

        var contentIdx = Array.IndexOf(args, "--content");
        if (contentIdx < 0 || contentIdx + 1 >= args.Length)
        {
            OutputFormatter.WriteError("Usage: safe file write <path> --content <text>");
            return 1;
        }

        var content = string.Join(' ', args[(contentIdx + 1)..]);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
        if (json)
            OutputFormatter.WriteJson(new { path = Path.GetFullPath(path), bytes = content.Length });
        else
            OutputFormatter.WriteSuccess($"Written: {Path.GetFullPath(path)}");
        return 0;
    }

    // === Targeted writes ===

    private static int RunDeleteTracked(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file delete-tracked <file>"); return 1; }
        var file = args[0];

        if (!File.Exists(file)) { OutputFormatter.WriteError($"File not found: {file}"); return 1; }

        // Check if file is git-tracked
        var (code, _, _) = ProcessRunner.Run("git", ["ls-files", "--error-unmatch", file]);
        if (code != 0)
        {
            OutputFormatter.WriteBlocked($"file delete-tracked {file}",
                "File is not tracked by git - cannot safely delete",
                "Only git-tracked files can be deleted (recoverable via git checkout)");
            return 1;
        }

        // Check if file has uncommitted changes
        var (_, diffOutput, _) = ProcessRunner.Run("git", ["diff", "--name-only", file]);
        var (_, stagedOutput, _) = ProcessRunner.Run("git", ["diff", "--staged", "--name-only", file]);
        if (!string.IsNullOrWhiteSpace(diffOutput) || !string.IsNullOrWhiteSpace(stagedOutput))
        {
            OutputFormatter.WriteBlocked($"file delete-tracked {file}",
                "File has uncommitted changes - commit or stash first",
                "safe git stash, then safe file delete-tracked " + file);
            return 1;
        }

        File.Delete(file);
        if (json)
            OutputFormatter.WriteJson(new { deleted = file, recoverable = true, recovery = $"git checkout HEAD -- {file}" });
        else
            OutputFormatter.WriteSuccess($"Deleted: {file} (recover with: git checkout HEAD -- {file})");
        return 0;
    }

    private static int RunDeleteTemp(string[] args, bool json)
    {
        var basePath = args.Length > 0 ? args[0] : ".";
        if (!Directory.Exists(basePath))
        {
            OutputFormatter.WriteError($"Directory not found: {basePath}");
            return 1;
        }

        var deleted = new List<string>();

        // Delete safe directories
        foreach (var dir in SafeDeleteDirs)
        {
            var fullPath = Path.Combine(basePath, dir);
            if (Directory.Exists(fullPath))
            {
                try
                {
                    Directory.Delete(fullPath, true);
                    deleted.Add(fullPath);
                }
                catch (Exception ex)
                {
                    OutputFormatter.WriteWarning($"Could not delete {fullPath}: {ex.Message}");
                }
            }
        }

        // Delete temp file patterns in root only (not recursive)
        foreach (var pattern in TempPatterns)
        {
            try
            {
                foreach (var file in Directory.GetFiles(basePath, pattern))
                {
                    File.Delete(file);
                    deleted.Add(file);
                }
            }
            catch { /* skip if pattern fails */ }
        }

        if (json)
            OutputFormatter.WriteJson(new { deleted, count = deleted.Count });
        else if (deleted.Count > 0)
        {
            OutputFormatter.WriteSuccess($"Deleted {deleted.Count} temp items:");
            foreach (var d in deleted)
                Console.WriteLine($"  {d}");
        }
        else
            Console.WriteLine("No temp files or directories found.");

        return 0;
    }

    private static int RunDeleteLocks(string[] args, bool json)
    {
        var deleted = new List<string>();

        // Find and delete git lock files
        foreach (var pattern in LockFiles)
        {
            var dir = Path.GetDirectoryName(pattern) ?? ".";
            var filePattern = Path.GetFileName(pattern);

            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var file in Directory.GetFiles(dir, filePattern))
                {
                    File.Delete(file);
                    deleted.Add(file);
                }
            }
            catch { /* skip */ }
        }

        // Also check for package manager lock scenarios
        var lockCandidates = new[] { "package-lock.json.lock", "yarn.lock.lock", ".npmrc.lock" };
        foreach (var lockFile in lockCandidates)
        {
            if (File.Exists(lockFile))
            {
                File.Delete(lockFile);
                deleted.Add(lockFile);
            }
        }

        if (json)
            OutputFormatter.WriteJson(new { deleted, count = deleted.Count });
        else if (deleted.Count > 0)
        {
            OutputFormatter.WriteSuccess($"Deleted {deleted.Count} lock files:");
            foreach (var d in deleted)
                Console.WriteLine($"  {d}");
        }
        else
            Console.WriteLine("No lock files found.");

        return 0;
    }

    private static int RunDeletePattern(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe file delete-pattern <glob> --in <dir>"); return 1; }

        var pattern = args[0];
        var inIdx = Array.IndexOf(args, "--in");
        if (inIdx < 0 || inIdx + 1 >= args.Length)
        {
            OutputFormatter.WriteError("--in <dir> is required. Specify a safe target directory.");
            return 1;
        }

        var dir = args[inIdx + 1];
        if (!Directory.Exists(dir))
        {
            OutputFormatter.WriteError($"Directory not found: {dir}");
            return 1;
        }

        // Validate directory is in safe list
        var dirName = Path.GetFileName(dir.TrimEnd('/', '\\'));
        if (!SafeDeleteDirs.Contains(dirName.ToLowerInvariant()))
        {
            OutputFormatter.WriteBlocked($"file delete-pattern {pattern} --in {dir}",
                $"Directory '{dirName}' is not in the safe delete list",
                $"Safe directories: {string.Join(", ", SafeDeleteDirs.Take(10))}...");
            return 1;
        }

        var deleted = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                File.Delete(file);
                deleted.Add(file);
            }
        }
        catch (Exception ex)
        {
            OutputFormatter.WriteError(ex.Message);
            return 1;
        }

        if (json)
            OutputFormatter.WriteJson(new { deleted, count = deleted.Count });
        else
            OutputFormatter.WriteSuccess($"Deleted {deleted.Count} files matching '{pattern}' in {dir}");

        return 0;
    }

    private static int RunMove(string[] args, bool json)
    {
        if (args.Length < 2) { OutputFormatter.WriteError("Usage: safe file move <src> <dest>"); return 1; }
        var src = args[0];
        var dest = args[1];

        if (!File.Exists(src)) { OutputFormatter.WriteError($"Source not found: {src}"); return 1; }

        // Check if file is git-tracked
        var (code, _, _) = ProcessRunner.Run("git", ["ls-files", "--error-unmatch", src]);
        if (code != 0)
        {
            OutputFormatter.WriteBlocked($"file move {src}",
                "Only git-tracked files can be moved (ensuring recoverability)");
            return 1;
        }

        if (File.Exists(dest))
        {
            OutputFormatter.WriteBlocked("file move", "Destination already exists");
            return 1;
        }

        // Use git mv so git tracks the rename
        var result = ProcessRunner.Run("git", ["mv", src, dest]);
        if (result.ExitCode != 0)
        {
            OutputFormatter.WriteError(result.Error);
            return 1;
        }

        if (json)
            OutputFormatter.WriteJson(new { source = src, destination = dest });
        else
            OutputFormatter.WriteSuccess($"Moved: {src} -> {dest}");
        return 0;
    }
}
