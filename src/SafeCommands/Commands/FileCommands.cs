using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

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
            new("file", "list", "List directory contents", "safe file list [<path>]", SafetyLevel.ReadOnly, RunList)
                { Policy = Policy.Default.RequirePathWithinProject() },
            new("file", "read", "Read file content", "safe file read <path> [--lines <n>]", SafetyLevel.ReadOnly, RunRead)
                { Policy = Policy.Default.RequirePathWithinProject(), MinArgs = 1 },
            new("file", "exists", "Check if file or directory exists", "safe file exists <path>", SafetyLevel.ReadOnly, RunExists)
                { Policy = Policy.Default.RequirePathWithinProject(), MinArgs = 1 },
            new("file", "info", "Show file metadata", "safe file info <path>", SafetyLevel.ReadOnly, RunInfo)
                { Policy = Policy.Default.RequirePathWithinProject(), MinArgs = 1 },
            new("file", "count", "Count lines/words/chars in a file", "safe file count <path> [--lines|--words|--chars]", SafetyLevel.ReadOnly, RunCount)
                { Policy = Policy.Default.RequirePathWithinProject(), MinArgs = 1 },
            new("file", "find", "Find files by pattern", "safe file find <pattern> [--in <dir>]", SafetyLevel.ReadOnly, RunFind)
                { Policy = Policy.Default.RequirePathWithinProject(new PathArg.FlagValue("--in")), MinArgs = 1 },
            new("file", "tree", "Show directory tree", "safe file tree [<path>] [--depth <n>]", SafetyLevel.ReadOnly, RunTree)
                { Policy = Policy.Default.RequirePathWithinProject() },

            // Safe writes
            new("file", "mkdir", "Create directory", "safe file mkdir <path>", SafetyLevel.SafeWrite, RunMkdir)
                { Policy = Policy.Default.RequirePathWithinProject(), MinArgs = 1 },
            new("file", "copy", "Copy file (no overwrite)", "safe file copy <src> <dest>", SafetyLevel.SafeWrite, RunCopy)
                { Policy = Policy.Default.RequirePathWithinProject(0).RequirePathWithinProject(1), MinArgs = 2 },
            new("file", "write", "Write to new file (no overwrite)", "safe file write <path> --content <text>", SafetyLevel.SafeWrite, RunWrite)
                { Policy = Policy.Default.RequirePathWithinProject(), MinArgs = 1 },

            // Targeted writes
            new("file", "delete-tracked", "Delete a git-tracked file", "safe file delete-tracked <file>", SafetyLevel.CheckedWrite, RunDeleteTracked)
                { Policy = Policy.Default.RequirePathWithinProject(), MinArgs = 1 },
            new("file", "delete-temp", "Delete temp/cache/build files", "safe file delete-temp [<path>]", SafetyLevel.CheckedWrite, RunDeleteTemp)
                { Policy = Policy.Default.RequirePathWithinProject() },
            new("file", "delete-locks", "Delete lock files", "safe file delete-locks", SafetyLevel.CheckedWrite, RunDeleteLocks),
            new("file", "delete-pattern", "Delete files matching pattern in safe dirs", "safe file delete-pattern <glob> --in <dir>", SafetyLevel.CheckedWrite, RunDeletePattern)
                { Policy = Policy.Default.RequirePathWithinProject(new PathArg.FlagValue("--in")).RequireWithinSafeDeleteDir(new PathArg.FlagValue("--in"), SafeDeleteDirs), MinArgs = 1 },
            new("file", "move", "Move/rename git-tracked file", "safe file move <src> <dest>", SafetyLevel.CheckedWrite, RunMove)
                { Policy = Policy.Default.RequirePathWithinProject(0).RequirePathWithinProject(1), MinArgs = 2 },
        ]);
    }

    // === Read-only ===

    internal static int RunList(Ports p, string[] args)
    {
        var path = args.Length > 0 ? args[0] : ".";
        if (!Directory.Exists(path))
        {
            p.Render.Error($"Directory not found: {path}");
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

        if (p.Render.JsonMode)
            p.Render.Json(new { path = Path.GetFullPath(path), entries });
        else
            foreach (var entry in entries)
                p.Render.Info(entry.type == "dir" ? $"{entry.name}/" : entry.name);

        return 0;
    }

    internal static int RunRead(Ports p, string[] args)
    {
        var path = args[0];
        if (!File.Exists(path))
        {
            p.Render.Error($"File not found: {path}");
            return 1;
        }

        var lines = Args.IntValue(args, "--lines", -1);

        var content = lines > 0
            ? string.Join('\n', File.ReadLines(path).Take(lines))
            : File.ReadAllText(path);

        if (p.Render.JsonMode)
            p.Render.Json(new { path = Path.GetFullPath(path), content, lineCount = content.Split('\n').Length });
        else
            p.Render.Raw(content);

        return 0;
    }

    internal static int RunExists(Ports p, string[] args)
    {
        var path = args[0];
        var fileExists = File.Exists(path);
        var dirExists = Directory.Exists(path);
        var exists = fileExists || dirExists;
        var type = fileExists ? "file" : dirExists ? "directory" : "none";

        if (p.Render.JsonMode)
            p.Render.Json(new { path, exists, type });
        else
            p.Render.Info(exists ? $"Exists ({type}): {Path.GetFullPath(path)}" : $"Not found: {path}");

        return exists ? 0 : 1;
    }

    internal static int RunInfo(Ports p, string[] args)
    {
        var path = args[0];

        if (File.Exists(path))
        {
            var fi = new FileInfo(path);
            if (p.Render.JsonMode)
                p.Render.Json(new { path = fi.FullName, type = "file", size = fi.Length, created = fi.CreationTimeUtc, modified = fi.LastWriteTimeUtc, readOnly = fi.IsReadOnly });
            else
            {
                p.Render.Info($"Path:     {fi.FullName}");
                p.Render.Info($"Type:     file");
                p.Render.Info($"Size:     {fi.Length:N0} bytes");
                p.Render.Info($"Created:  {fi.CreationTimeUtc:u}");
                p.Render.Info($"Modified: {fi.LastWriteTimeUtc:u}");
                p.Render.Info($"ReadOnly: {fi.IsReadOnly}");
            }
            return 0;
        }

        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            if (p.Render.JsonMode)
                p.Render.Json(new { path = di.FullName, type = "directory", created = di.CreationTimeUtc, modified = di.LastWriteTimeUtc });
            else
            {
                p.Render.Info($"Path:     {di.FullName}");
                p.Render.Info($"Type:     directory");
                p.Render.Info($"Created:  {di.CreationTimeUtc:u}");
                p.Render.Info($"Modified: {di.LastWriteTimeUtc:u}");
            }
            return 0;
        }

        p.Render.Error($"Not found: {path}");
        return 1;
    }

    internal static int RunCount(Ports p, string[] args)
    {
        var path = args[0];
        if (!File.Exists(path)) { p.Render.Error($"File not found: {path}"); return 1; }

        var wantLines = Args.HasFlag(args, "--lines");
        var wantWords = Args.HasFlag(args, "--words");
        var wantChars = Args.HasFlag(args, "--chars");
        if (!wantLines && !wantWords && !wantChars) wantLines = true;

        var content = File.ReadAllText(path);
        long lines = content.Count(c => c == '\n');
        long words = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        long chars = content.Length;

        if (p.Render.JsonMode)
        {
            var result = new Dictionary<string, object> { ["path"] = Path.GetFullPath(path) };
            if (wantLines) result["lines"] = lines;
            if (wantWords) result["words"] = words;
            if (wantChars) result["chars"] = chars;
            p.Render.Json(result);
        }
        else
        {
            var parts = new List<string>();
            if (wantLines) parts.Add($"{lines,8} lines");
            if (wantWords) parts.Add($"{words,8} words");
            if (wantChars) parts.Add($"{chars,8} chars");
            p.Render.Info($"{string.Join("  ", parts)}  {path}");
        }
        return 0;
    }

    internal static int RunFind(Ports p, string[] args)
    {
        var pattern = args[0];
        var dir = Args.Value(args, "--in") ?? ".";

        if (!Directory.Exists(dir))
        {
            p.Render.Error($"Directory not found: {dir}");
            return 1;
        }

        try
        {
            var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f))
                .Take(500)
                .ToArray();

            if (p.Render.JsonMode)
                p.Render.Json(new { pattern, directory = Path.GetFullPath(dir), count = files.Length, files });
            else
                foreach (var file in files)
                    p.Render.Info(file);

            return 0;
        }
        catch (Exception ex)
        {
            p.Render.Error(ex.Message);
            return 1;
        }
    }

    internal static int RunTree(Ports p, string[] args)
    {
        var path = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : ".";
        var maxDepth = Args.IntValue(args, "--depth", 3);

        if (!Directory.Exists(path))
        {
            p.Render.Error($"Directory not found: {path}");
            return 1;
        }

        if (p.Render.JsonMode)
            p.Render.Json(BuildTree(path, maxDepth, 0));
        else
            PrintTree(p.Render, path, "", maxDepth, 0);

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

    private static void PrintTree(IRenderer render, string path, string prefix, int maxDepth, int depth)
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

            render.Info($"{prefix}{connector}{name}{(isDir ? "/" : "")}");

            if (isDir)
            {
                var newPrefix = prefix + (isLast ? "    " : "│   ");
                PrintTree(render, entries[i], newPrefix, maxDepth, depth + 1);
            }
        }
    }

    // === Safe writes ===

    internal static int RunMkdir(Ports p, string[] args)
    {
        var path = args[0];
        Directory.CreateDirectory(path);
        if (p.Render.JsonMode)
            p.Render.Json(new { created = Path.GetFullPath(path) });
        else
            p.Render.Info($"Created: {Path.GetFullPath(path)}");
        return 0;
    }

    internal static int RunCopy(Ports p, string[] args)
    {
        var src = args[0];
        var dest = args[1];

        if (!File.Exists(src)) { p.Render.Error($"Source not found: {src}"); return 1; }
        if (File.Exists(dest))
        {
            p.Render.Blocked("file copy", "Destination already exists - overwrite not allowed",
                "Delete the destination first or choose a different name");
            return 1;
        }

        File.Copy(src, dest);
        if (p.Render.JsonMode)
            p.Render.Json(new { source = Path.GetFullPath(src), destination = Path.GetFullPath(dest) });
        else
            p.Render.Info($"Copied: {src} -> {dest}");
        return 0;
    }

    internal static int RunWrite(Ports p, string[] args)
    {
        var path = args[0];

        if (File.Exists(path))
        {
            p.Render.Blocked("file write", "File already exists - overwrite not allowed",
                "Use a different path or delete the file first");
            return 1;
        }

        var contentParts = Args.ValuesAfter(args, "--content");
        if (contentParts.Length == 0)
        {
            p.Render.Error("Usage: safe file write <path> --content <text>");
            return 1;
        }

        var content = string.Join(' ', contentParts);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
        if (p.Render.JsonMode)
            p.Render.Json(new { path = Path.GetFullPath(path), bytes = content.Length });
        else
            p.Render.Info($"Written: {Path.GetFullPath(path)}");
        return 0;
    }

    // === Targeted writes ===

    internal static int RunDeleteTracked(Ports p, string[] args)
    {
        var file = args[0];

        if (!File.Exists(file)) { p.Render.Error($"File not found: {file}"); return 1; }

        // Check if file is git-tracked
        if (p.Exec.Run("git", ["ls-files", "--error-unmatch", file]).ExitCode != 0)
        {
            p.Render.Blocked($"file delete-tracked {file}",
                "File is not tracked by git - cannot safely delete",
                "Only git-tracked files can be deleted (recoverable via git checkout)");
            return 1;
        }

        // Check if file has uncommitted changes
        var diffOutput = p.Exec.Run("git", ["diff", "--name-only", file]).StdOut;
        var stagedOutput = p.Exec.Run("git", ["diff", "--staged", "--name-only", file]).StdOut;
        if (!string.IsNullOrWhiteSpace(diffOutput) || !string.IsNullOrWhiteSpace(stagedOutput))
        {
            p.Render.Blocked($"file delete-tracked {file}",
                "File has uncommitted changes - commit or stash first",
                "safe git stash, then safe file delete-tracked " + file);
            return 1;
        }

        File.Delete(file);
        if (p.Render.JsonMode)
            p.Render.Json(new { deleted = file, recoverable = true, recovery = $"git checkout HEAD -- {file}" });
        else
            p.Render.Info($"Deleted: {file} (recover with: git checkout HEAD -- {file})");
        return 0;
    }

    internal static int RunDeleteTemp(Ports p, string[] args)
    {
        var basePath = args.Length > 0 ? args[0] : ".";
        if (!Directory.Exists(basePath))
        {
            p.Render.Error($"Directory not found: {basePath}");
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
                    p.Render.Warning($"Could not delete {fullPath}: {ex.Message}");
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

        if (p.Render.JsonMode)
            p.Render.Json(new { deleted, count = deleted.Count });
        else if (deleted.Count > 0)
        {
            p.Render.Info($"Deleted {deleted.Count} temp items:");
            foreach (var d in deleted)
                p.Render.Info($"  {d}");
        }
        else
            p.Render.Info("No temp files or directories found.");

        return 0;
    }

    internal static int RunDeleteLocks(Ports p, string[] args)
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

        if (p.Render.JsonMode)
            p.Render.Json(new { deleted, count = deleted.Count });
        else if (deleted.Count > 0)
        {
            p.Render.Info($"Deleted {deleted.Count} lock files:");
            foreach (var d in deleted)
                p.Render.Info($"  {d}");
        }
        else
            p.Render.Info("No lock files found.");

        return 0;
    }

    internal static int RunDeletePattern(Ports p, string[] args)
    {
        var pattern = args[0];
        var dir = Args.Value(args, "--in");
        if (dir == null)
        {
            p.Render.Error("--in <dir> is required. Specify a safe target directory.");
            return 1;
        }

        if (!Directory.Exists(dir))
        {
            p.Render.Error($"Directory not found: {dir}");
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
            p.Render.Error(ex.Message);
            return 1;
        }

        if (p.Render.JsonMode)
            p.Render.Json(new { deleted, count = deleted.Count });
        else
            p.Render.Info($"Deleted {deleted.Count} files matching '{pattern}' in {dir}");

        return 0;
    }

    internal static int RunMove(Ports p, string[] args)
    {
        var src = args[0];
        var dest = args[1];

        if (!File.Exists(src)) { p.Render.Error($"Source not found: {src}"); return 1; }

        // Check if file is git-tracked
        if (p.Exec.Run("git", ["ls-files", "--error-unmatch", src]).ExitCode != 0)
        {
            p.Render.Blocked($"file move {src}",
                "Only git-tracked files can be moved (ensuring recoverability)", null);
            return 1;
        }

        if (File.Exists(dest))
        {
            p.Render.Blocked("file move", "Destination already exists", null);
            return 1;
        }

        // Use git mv so git tracks the rename
        var result = p.Exec.Run("git", ["mv", src, dest]);
        if (result.ExitCode != 0)
        {
            p.Render.Error(result.StdErr);
            return 1;
        }

        if (p.Render.JsonMode)
            p.Render.Json(new { source = src, destination = dest });
        else
            p.Render.Info($"Moved: {src} -> {dest}");
        return 0;
    }
}
