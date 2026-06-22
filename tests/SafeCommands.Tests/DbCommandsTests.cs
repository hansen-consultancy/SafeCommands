using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class DbCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        return (new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost()), exec, render);
    }

    // === Prisma (npx-fronted) ===

    [Fact]
    public void RunPrismaStatus_SpawnsNpxPrismaMigrateStatus()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunPrismaStatus(ports, []);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("npx", call.Tool);
        Assert.Equal(new[] { "prisma", "migrate", "status" }, call.Args);
    }

    [Fact]
    public void RunPrismaGenerate_SpawnsNpxPrismaGenerate()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunPrismaGenerate(ports, []);
        Assert.Equal(new[] { "prisma", "generate" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunPrismaMigrateDev_NoName_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = DbCommands.RunPrismaMigrateDev(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunPrismaMigrateDev_WithName_SplicesArgsAfterDev()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunPrismaMigrateDev(ports, ["--name", "add_users"]);
        Assert.Equal(new[] { "prisma", "migrate", "dev", "--name", "add_users" }, Assert.Single(exec.Calls).Args);
    }

    // === Drizzle ===

    [Fact]
    public void RunDrizzleMigrate_SpawnsNpxDrizzleKitMigrate()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunDrizzleMigrate(ports, []);
        Assert.Equal(new[] { "drizzle-kit", "migrate" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunDrizzleMigrate_SplicesArgsAfterMigrate()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunDrizzleMigrate(ports, ["--config", "drizzle.config.ts"]);
        Assert.Equal(new[] { "drizzle-kit", "migrate", "--config", "drizzle.config.ts" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunDrizzleGenerate_SplicesArgsAfterGenerate()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunDrizzleGenerate(ports, ["--name", "init"]);
        Assert.Equal(new[] { "drizzle-kit", "generate", "--name", "init" }, Assert.Single(exec.Calls).Args);
    }

    // === EF Core (dotnet-fronted) ===

    [Fact]
    public void RunEfMigrationsList_SpawnsDotnetEfMigrationsList()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunEfMigrationsList(ports, []);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("dotnet", call.Tool);
        Assert.Equal(new[] { "ef", "migrations", "list" }, call.Args);
    }

    [Fact]
    public void RunEfMigrationsAdd_NoArgs_EmitsErrorAndDoesNotSpawn()
    {
        var (ports, exec, render) = Setup();
        var rc = DbCommands.RunEfMigrationsAdd(ports, []);
        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }

    [Fact]
    public void RunEfMigrationsAdd_WithName_AppendsName()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunEfMigrationsAdd(ports, ["Init"]);
        Assert.Equal(new[] { "ef", "migrations", "add", "Init" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunEfDatabaseUpdate_SplicesArgsAfterUpdate()
    {
        // Distinct middle token "database" (every other EF handler uses "migrations") + ..args splice.
        var (ports, exec, _) = Setup();
        DbCommands.RunEfDatabaseUpdate(ports, ["--connection", "cs"]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("dotnet", call.Tool);
        Assert.Equal(new[] { "ef", "database", "update", "--connection", "cs" }, call.Args);
    }

    [Fact]
    public void RunEfMigrationsScript_SplicesArgsAfterScript()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunEfMigrationsScript(ports, ["--idempotent"]);
        Assert.Equal(new[] { "ef", "migrations", "script", "--idempotent" }, Assert.Single(exec.Calls).Args);
    }

    // === Laravel / Artisan (php-fronted) ===

    [Fact]
    public void RunArtisanMigrate_SpawnsPhpArtisanMigrate()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunArtisanMigrate(ports, ["--step"]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("php", call.Tool);
        Assert.Equal(new[] { "artisan", "migrate", "--step" }, call.Args);
    }

    [Fact]
    public void RunArtisanMigrateStatus_IgnoresArgs()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunArtisanMigrateStatus(ports, ["ignored"]);
        Assert.Equal(new[] { "artisan", "migrate:status" }, Assert.Single(exec.Calls).Args);
    }

    // === Django (python-fronted) ===

    [Fact]
    public void RunDjangoMigrate_SpawnsPythonManageMigrate()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunDjangoMigrate(ports, ["myapp"]);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("python", call.Tool);
        Assert.Equal(new[] { "manage.py", "migrate", "myapp" }, call.Args);
    }

    [Fact]
    public void RunDjangoMakeMigrations_SpawnsPythonManageMakemigrations()
    {
        var (ports, exec, _) = Setup();
        DbCommands.RunDjangoMakeMigrations(ports, []);
        Assert.Equal(new[] { "manage.py", "makemigrations" }, Assert.Single(exec.Calls).Args);
    }

    [Fact]
    public void RunDjangoShowMigrations_PropagatesExecExitCode()
    {
        var (ports, exec, _) = Setup();
        exec.NextResult = new ExecResult(2, "", "boom");
        Assert.Equal(2, DbCommands.RunDjangoShowMigrations(ports, []));
    }
}
