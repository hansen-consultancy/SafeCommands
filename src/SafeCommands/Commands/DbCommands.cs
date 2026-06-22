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
    // Flags that are NEVER allowed on any migration command
    private static readonly HashSet<string> DestructiveFlags =
    [
        "--force", "-f",
        "--force-reset",
        "--accept-data-loss",
        "--skip-seed",
        "--skip-generate",
    ];

    // Prisma commands that are destructive by nature
    private static readonly HashSet<string> PrismaBlockedCommands =
    [
        "migrate reset",   // drops all tables
        "db push",         // can drop tables with --force-reset or --accept-data-loss
    ];

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
            new("db", "prisma-migrate-dev", "Create new migration (dev only, no --force)", "safe db prisma-migrate-dev --name <name>", SafetyLevel.CheckedWrite, RunPrismaMigrateDev)
                { Policy = Policy.Default.BlockFlags(DestructiveFlags, "This flag can cause irreversible data loss (drops tables or data)", "Remove the destructive flag (e.g. --force, --accept-data-loss) for a safe operation") },
            new("db", "prisma-migrate-deploy", "Apply pending migrations", "safe db prisma-migrate-deploy", SafetyLevel.CheckedWrite, RunPrismaMigrateDeploy)
                { Policy = Policy.Default.BlockFlags(DestructiveFlags, "This flag can cause irreversible data loss (drops tables or data)", "Remove the destructive flag (e.g. --force, --accept-data-loss) for a safe operation") },
            new("db", "prisma-db-pull", "Pull schema from database", "safe db prisma-db-pull", SafetyLevel.SafeWrite, RunPrismaDbPull),
            new("db", "prisma-db-seed", "Run database seed", "safe db prisma-db-seed", SafetyLevel.SafeWrite, RunPrismaDbSeed),

            // Drizzle - Read-only
            new("db", "drizzle-check", "Check Drizzle schema", "safe db drizzle-check", SafetyLevel.ReadOnly, RunDrizzleCheck),
            new("db", "drizzle-status", "Show Drizzle migration status", "safe db drizzle-status", SafetyLevel.ReadOnly, RunDrizzleStatus),

            // Drizzle - Safe writes
            new("db", "drizzle-generate", "Generate Drizzle migration", "safe db drizzle-generate", SafetyLevel.SafeWrite, RunDrizzleGenerate),
            new("db", "drizzle-migrate", "Apply Drizzle migrations", "safe db drizzle-migrate", SafetyLevel.CheckedWrite, RunDrizzleMigrate)
                { Policy = Policy.Default.BlockFlags(DestructiveFlags, "This flag can cause irreversible data loss (drops tables or data)", "Remove the destructive flag (e.g. --force, --accept-data-loss) for a safe operation") },

            // EF Core
            new("db", "ef-migrations-list", "List EF Core migrations", "safe db ef-migrations-list", SafetyLevel.ReadOnly, RunEfMigrationsList),
            new("db", "ef-migrations-add", "Add new EF Core migration", "safe db ef-migrations-add <name>", SafetyLevel.SafeWrite, RunEfMigrationsAdd),
            new("db", "ef-database-update", "Apply EF Core migrations", "safe db ef-database-update", SafetyLevel.CheckedWrite, RunEfDatabaseUpdate)
                { Policy = Policy.Default.BlockFlags(DestructiveFlags, "This flag can cause irreversible data loss (drops tables or data)", "Remove the destructive flag (e.g. --force, --accept-data-loss) for a safe operation") },
            new("db", "ef-migrations-script", "Generate SQL script from migrations", "safe db ef-migrations-script", SafetyLevel.ReadOnly, RunEfMigrationsScript),

            // Laravel / Artisan
            new("db", "artisan-migrate-status", "Show Laravel migration status", "safe db artisan-migrate-status", SafetyLevel.ReadOnly, RunArtisanMigrateStatus),
            new("db", "artisan-migrate", "Run Laravel migrations (no fresh/reset/rollback)", "safe db artisan-migrate", SafetyLevel.CheckedWrite, RunArtisanMigrate)
                { Policy = Policy.Default.BlockSubstrings(["fresh", "reset", "rollback", "wipe"], "migrate:fresh, migrate:reset, migrate:rollback, and db:wipe drop tables", "safe db artisan-migrate (forward-only migrations)") },

            // Django
            new("db", "django-showmigrations", "Show Django migration status", "safe db django-showmigrations", SafetyLevel.ReadOnly, RunDjangoShowMigrations),
            // "zero" is a positional target token, not a flag, but BlockFlags matches any arg's normalized base (exact-token, case-insensitive) — the closest faithful reuse of the existing rule.
            new("db", "django-migrate", "Apply Django migrations", "safe db django-migrate", SafetyLevel.CheckedWrite, RunDjangoMigrate)
                { Policy = Policy.Default.BlockFlags(["zero"], "Migrating to 'zero' drops all tables for the app", "safe db django-migrate (forward-only)") },
            new("db", "django-makemigrations", "Create Django migrations", "safe db django-makemigrations", SafetyLevel.SafeWrite, RunDjangoMakeMigrations),
        ]);
    }

    // npx-fronted migration tools (Prisma, Drizzle) share one facade; safety is enforced
    // centrally at dispatch, so each handler is a thin tool invocation.
    private static int RunNpx(Ports p, string[] args) => Run.Tool(p, "npx", args);

    // === Prisma ===

    internal static int RunPrismaStatus(Ports p, string[] args) => RunNpx(p, ["prisma", "migrate", "status"]);
    internal static int RunPrismaStudio(Ports p, string[] args) => RunNpx(p, ["prisma", "studio"]);
    internal static int RunPrismaGenerate(Ports p, string[] args) => RunNpx(p, ["prisma", "generate"]);
    internal static int RunPrismaFormat(Ports p, string[] args) => RunNpx(p, ["prisma", "format"]);
    internal static int RunPrismaValidate(Ports p, string[] args) => RunNpx(p, ["prisma", "validate"]);

    internal static int RunPrismaMigrateDev(Ports p, string[] args)
    {
        if (Args.Value(args, "--name") == null)
        {
            p.Render.Error("Usage: safe db prisma-migrate-dev --name <migration-name>");
            return 1;
        }

        return RunNpx(p, ["prisma", "migrate", "dev", ..args]);
    }

    internal static int RunPrismaMigrateDeploy(Ports p, string[] args) => RunNpx(p, ["prisma", "migrate", "deploy"]);

    internal static int RunPrismaDbPull(Ports p, string[] args) => RunNpx(p, ["prisma", "db", "pull"]);
    internal static int RunPrismaDbSeed(Ports p, string[] args) => RunNpx(p, ["prisma", "db", "seed"]);

    // === Drizzle ===

    internal static int RunDrizzleCheck(Ports p, string[] args) => RunNpx(p, ["drizzle-kit", "check"]);
    internal static int RunDrizzleStatus(Ports p, string[] args) => RunNpx(p, ["drizzle-kit", "status"]);

    internal static int RunDrizzleGenerate(Ports p, string[] args) => RunNpx(p, ["drizzle-kit", "generate", ..args]);

    // drizzle-kit push is intentionally not exposed (it's the command that wiped 60+ tables);
    // only drizzle-kit migrate, which applies generated SQL files, is reachable.
    internal static int RunDrizzleMigrate(Ports p, string[] args) => RunNpx(p, ["drizzle-kit", "migrate", ..args]);

    // === EF Core ===

    internal static int RunEfMigrationsList(Ports p, string[] args) => Run.Tool(p, "dotnet", ["ef", "migrations", "list", ..args]);

    internal static int RunEfMigrationsAdd(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe db ef-migrations-add <name>"); return 1; }
        return Run.Tool(p, "dotnet", ["ef", "migrations", "add", ..args]);
    }

    internal static int RunEfDatabaseUpdate(Ports p, string[] args) => Run.Tool(p, "dotnet", ["ef", "database", "update", ..args]);

    internal static int RunEfMigrationsScript(Ports p, string[] args) => Run.Tool(p, "dotnet", ["ef", "migrations", "script", ..args]);

    // === Laravel / Artisan ===

    internal static int RunArtisanMigrateStatus(Ports p, string[] args) => Run.Tool(p, "php", ["artisan", "migrate:status"]);
    internal static int RunArtisanMigrate(Ports p, string[] args) => Run.Tool(p, "php", ["artisan", "migrate", ..args]);

    // === Django ===

    internal static int RunDjangoShowMigrations(Ports p, string[] args) => Run.Tool(p, "python", ["manage.py", "showmigrations", ..args]);
    internal static int RunDjangoMigrate(Ports p, string[] args) => Run.Tool(p, "python", ["manage.py", "migrate", ..args]);
    internal static int RunDjangoMakeMigrations(Ports p, string[] args) => Run.Tool(p, "python", ["manage.py", "makemigrations", ..args]);
}
