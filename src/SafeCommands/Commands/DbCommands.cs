using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

/// <summary>
/// Safe database migration commands. Real-world incidents show agents frequently
/// run migration commands with --force flags that DROP ALL TABLES:
/// - prisma db push --force-reset (wiped 60+ tables)
/// - prisma migrate reset --force (dropped entire schema)
/// - drizzle-kit push --force (wiped production PostgreSQL)
/// - artisan migrate:fresh (dropped all tables via wrong .env)
/// - npx prisma db push --accept-data-loss
/// </summary>
static class DbCommands
{
    /// <summary>
    /// Flags that are NEVER allowed on any migration command. Shared across Prisma,
    /// Drizzle, and EF Core migrate commands.
    /// </summary>
    private static readonly HashSet<string> DestructiveFlags =
    [
        "--force", "-f",
        "--force-reset",
        "--accept-data-loss",
        "--skip-seed",
        "--skip-generate",
    ];

    private static readonly Policy DestructivePolicy = Policy.Default.DenyFlags([.. DestructiveFlags]);

    /// <summary>
    /// Substring tokens that mark destructive Artisan migration variants
    /// (<c>migrate:fresh</c>, <c>migrate:reset</c>, <c>migrate:rollback</c>, <c>db:wipe</c>).
    /// Used both by <see cref="ArtisanDestructivePolicy"/> and the offending-arg lookup
    /// inside <see cref="RunArtisanMigrate"/>; keeping them in one place avoids drift.
    /// </summary>
    private static readonly string[] ArtisanDestructiveTerms = ["fresh", "reset", "rollback", "wipe"];

    private static readonly Policy ArtisanDestructivePolicy =
        Policy.Default.DenyArgsContaining(ArtisanDestructiveTerms);

    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Prisma - Read-only
            new("db", "prisma-status", "Show Prisma migration status", "safe db prisma-status", SafetyLevel.ReadOnly, RunPrismaStatus),
            new("db", "prisma-studio", "Open Prisma Studio (read-only browser)", "safe db prisma-studio", SafetyLevel.ReadOnly, RunPrismaStudio),

            // Prisma - Safe writes
            new("db", "prisma-generate", "Generate Prisma client", "safe db prisma-generate", SafetyLevel.SafeWrite, RunPrismaGenerate),
            new("db", "prisma-format", "Format Prisma schema", "safe db prisma-format", SafetyLevel.SafeWrite, RunPrismaFormat),
            new("db", "prisma-validate", "Validate Prisma schema", "safe db prisma-validate", SafetyLevel.ReadOnly, RunPrismaValidate),
            new("db", "prisma-migrate-dev", "Create new migration (dev only, no --force)", "safe db prisma-migrate-dev --name <name>", SafetyLevel.CheckedWrite, RunPrismaMigrateDev),
            new("db", "prisma-migrate-deploy", "Apply pending migrations", "safe db prisma-migrate-deploy", SafetyLevel.CheckedWrite, RunPrismaMigrateDeploy),
            new("db", "prisma-db-pull", "Pull schema from database", "safe db prisma-db-pull", SafetyLevel.SafeWrite, RunPrismaDbPull),
            new("db", "prisma-db-seed", "Run database seed", "safe db prisma-db-seed", SafetyLevel.SafeWrite, RunPrismaDbSeed),

            // Drizzle - Read-only
            new("db", "drizzle-check", "Check Drizzle schema", "safe db drizzle-check", SafetyLevel.ReadOnly, RunDrizzleCheck),
            new("db", "drizzle-status", "Show Drizzle migration status", "safe db drizzle-status", SafetyLevel.ReadOnly, RunDrizzleStatus),

            // Drizzle - Safe writes
            new("db", "drizzle-generate", "Generate Drizzle migration", "safe db drizzle-generate", SafetyLevel.SafeWrite, RunDrizzleGenerate),
            new("db", "drizzle-migrate", "Apply Drizzle migrations", "safe db drizzle-migrate", SafetyLevel.CheckedWrite, RunDrizzleMigrate),

            // EF Core
            new("db", "ef-migrations-list", "List EF Core migrations", "safe db ef-migrations-list", SafetyLevel.ReadOnly, RunEfMigrationsList),
            new("db", "ef-migrations-add", "Add new EF Core migration", "safe db ef-migrations-add <name>", SafetyLevel.SafeWrite, RunEfMigrationsAdd),
            new("db", "ef-database-update", "Apply EF Core migrations", "safe db ef-database-update", SafetyLevel.CheckedWrite, RunEfDatabaseUpdate),
            new("db", "ef-migrations-script", "Generate SQL script from migrations", "safe db ef-migrations-script", SafetyLevel.ReadOnly, RunEfMigrationsScript),

            // Laravel / Artisan
            new("db", "artisan-migrate-status", "Show Laravel migration status", "safe db artisan-migrate-status", SafetyLevel.ReadOnly, RunArtisanMigrateStatus),
            new("db", "artisan-migrate", "Run Laravel migrations (no fresh/reset/rollback)", "safe db artisan-migrate", SafetyLevel.CheckedWrite, RunArtisanMigrate),

            // Django
            new("db", "django-showmigrations", "Show Django migration status", "safe db django-showmigrations", SafetyLevel.ReadOnly, RunDjangoShowMigrations),
            new("db", "django-migrate", "Apply Django migrations", "safe db django-migrate", SafetyLevel.CheckedWrite, RunDjangoMigrate),
            new("db", "django-makemigrations", "Create Django migrations", "safe db django-makemigrations", SafetyLevel.SafeWrite, RunDjangoMakeMigrations),
        ]);
    }

    /// <summary>
    /// Evaluates <see cref="DestructivePolicy"/> with the project's "drops tables/data"
    /// wording rather than DenyFlags' generic message. Returns 0 on Allow, 1 on Block.
    /// </summary>
    private static int CheckDestructive(Ports p, string tool, string[] args)
    {
        if (DestructivePolicy.Evaluate(args) is PolicyResult.Block)
        {
            var offending = args.FirstOrDefault(a => DestructiveFlags.Contains(a.ToLowerInvariant())) ?? "";
            p.Render.Blocked($"{tool} {string.Join(' ', args)}".TrimEnd(),
                $"Flag '{offending}' can cause irreversible data loss (drops tables/data)",
                $"Remove '{offending}' for a safe operation");
            return 1;
        }
        return 0;
    }

    // === Prisma ===

    internal static int RunPrismaStatus(Ports p, string[] args)   => Run.Tool(p, "npx", ["prisma", "migrate", "status"]);
    internal static int RunPrismaStudio(Ports p, string[] args)   => Run.Tool(p, "npx", ["prisma", "studio"]);
    internal static int RunPrismaGenerate(Ports p, string[] args) => Run.Tool(p, "npx", ["prisma", "generate"]);
    internal static int RunPrismaFormat(Ports p, string[] args)   => Run.Tool(p, "npx", ["prisma", "format"]);
    internal static int RunPrismaValidate(Ports p, string[] args) => Run.Tool(p, "npx", ["prisma", "validate"]);

    internal static int RunPrismaMigrateDev(Ports p, string[] args)
    {
        var check = CheckDestructive(p, "prisma migrate dev", args);
        if (check != 0) return check;

        var nameIdx = Array.IndexOf(args, "--name");
        if (nameIdx < 0 || nameIdx + 1 >= args.Length)
        {
            p.Render.Error("Usage: safe db prisma-migrate-dev --name <migration-name>");
            return 1;
        }

        return Run.Tool(p, "npx", ["prisma", "migrate", "dev", .. args]);
    }

    internal static int RunPrismaMigrateDeploy(Ports p, string[] args)
    {
        var check = CheckDestructive(p, "prisma migrate deploy", args);
        if (check != 0) return check;
        return Run.Tool(p, "npx", ["prisma", "migrate", "deploy"]);
    }

    internal static int RunPrismaDbPull(Ports p, string[] args) => Run.Tool(p, "npx", ["prisma", "db", "pull"]);
    internal static int RunPrismaDbSeed(Ports p, string[] args) => Run.Tool(p, "npx", ["prisma", "db", "seed"]);

    // === Drizzle ===

    internal static int RunDrizzleCheck(Ports p, string[] args)    => Run.Tool(p, "npx", ["drizzle-kit", "check"]);
    internal static int RunDrizzleStatus(Ports p, string[] args)   => Run.Tool(p, "npx", ["drizzle-kit", "status"]);
    internal static int RunDrizzleGenerate(Ports p, string[] args) => Run.Tool(p, "npx", ["drizzle-kit", "generate", .. args]);

    internal static int RunDrizzleMigrate(Ports p, string[] args)
    {
        var check = CheckDestructive(p, "drizzle-kit migrate", args);
        if (check != 0) return check;
        // drizzle-kit push is blocked entirely (it's the command that wiped 60+ tables)
        // only allow drizzle-kit migrate which applies generated SQL files
        return Run.Tool(p, "npx", ["drizzle-kit", "migrate", .. args]);
    }

    // === EF Core ===

    internal static int RunEfMigrationsList(Ports p, string[] args)
        => Run.Tool(p, "dotnet", ["ef", "migrations", "list", .. args]);

    internal static int RunEfMigrationsAdd(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe db ef-migrations-add <name>"); return 1; }
        return Run.Tool(p, "dotnet", ["ef", "migrations", "add", .. args]);
    }

    internal static int RunEfDatabaseUpdate(Ports p, string[] args)
    {
        var check = CheckDestructive(p, "ef database update", args);
        if (check != 0) return check;
        return Run.Tool(p, "dotnet", ["ef", "database", "update", .. args]);
    }

    internal static int RunEfMigrationsScript(Ports p, string[] args)
        => Run.Tool(p, "dotnet", ["ef", "migrations", "script", .. args]);

    // === Laravel / Artisan ===

    internal static int RunArtisanMigrateStatus(Ports p, string[] args)
        => Run.Tool(p, "php", ["artisan", "migrate:status"]);

    internal static int RunArtisanMigrate(Ports p, string[] args)
    {
        // Block destructive artisan variants. Substring match catches embedded forms
        // like 'migrate:fresh' that exact flag-match would miss.
        if (ArtisanDestructivePolicy.Evaluate(args) is PolicyResult.Block)
        {
            var offending = args.FirstOrDefault(a =>
            {
                var lo = a.ToLowerInvariant();
                return ArtisanDestructiveTerms.Any(t => lo.Contains(t));
            }) ?? "";
            p.Render.Blocked($"artisan migrate {offending}",
                "migrate:fresh, migrate:reset, migrate:rollback, and db:wipe drop tables",
                "safe db artisan-migrate (forward-only migrations)");
            return 1;
        }
        return Run.Tool(p, "php", ["artisan", "migrate", .. args]);
    }

    // === Django ===

    internal static int RunDjangoShowMigrations(Ports p, string[] args)
        => Run.Tool(p, "python", ["manage.py", "showmigrations", .. args]);

    internal static int RunDjangoMigrate(Ports p, string[] args)
    {
        // 'zero' as a positional arg drops all tables for the named app
        if (args.Contains("zero"))
        {
            p.Render.Blocked("django migrate <app> zero",
                "Migrating to 'zero' drops all tables for the app",
                "safe db django-migrate (forward-only)");
            return 1;
        }
        return Run.Tool(p, "python", ["manage.py", "migrate", .. args]);
    }

    internal static int RunDjangoMakeMigrations(Ports p, string[] args)
        => Run.Tool(p, "python", ["manage.py", "makemigrations", .. args]);
}
