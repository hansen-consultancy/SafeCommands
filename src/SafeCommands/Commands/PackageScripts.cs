namespace SafeCommands.Commands;

/// <summary>The package-manager scripts agents may invoke via `npm/pnpm/bun run` — shared by
/// all three groups so the allowlist has a single source of truth.</summary>
static class PackageScripts
{
    public static readonly HashSet<string> Allowed =
    [
        "build", "dev", "start", "test", "lint", "format",
        "typecheck", "check", "compile", "watch", "serve", "preview",
        "generate", "codegen", "migrate", "seed", "prisma",
        "storybook", "e2e", "cypress", "playwright",
        "clean", "prebuild", "postbuild",
    ];
}
