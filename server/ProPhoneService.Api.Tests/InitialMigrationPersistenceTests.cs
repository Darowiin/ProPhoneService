using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static ProPhoneService.Api.Tests.MessengerBindingPersistenceTests;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// Создание и пересоздание текущей схемы одной начальной миграцией
/// </summary>
public class InitialMigrationPersistenceTests
{
    [PostgresFact]
    public async Task InitialCreate_CreatesCurrentSchemaAndCanBeRolledBackAndReapplied()
    {
        await using var database = await Database.CreateAsync(migrate: false);
        await using var db = database.Context();
        var migrator = db.GetService<IMigrator>();

        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();

        var migration = Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.EndsWith("_InitialCreate", migration);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(new[]
        {
            "account", "auth_code", "client", "device", "messenger_binding", "notification_log",
            "repair_order", "repair_order_service", "repair_status_history", "service",
        }, await db.Database.SqlQueryRaw<string>(
            """
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name
            """).ToArrayAsync());
        Assert.Equal(10, await db.Services.CountAsync());
        Assert.All(await db.Services.AsNoTracking().ToListAsync(), service => Assert.True(service.IsActive));
        Assert.Empty(await db.Database.SqlQueryRaw<string>(
            """
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name IN ('email', 'contact_email')
            """).ToListAsync());

        await migrator.MigrateAsync("0");

        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            """
            SELECT count(*)::int AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            """).SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            """
            SELECT count(*)::int AS "Value" FROM information_schema.sequences
            WHERE sequence_schema = 'public' AND sequence_name = 'repair_order_number_seq'
            """).SingleAsync());

        await db.Database.MigrateAsync();

        Assert.Equal(migration, Assert.Single(await db.Database.GetAppliedMigrationsAsync()));
        Assert.Equal(10, await db.Services.CountAsync());
    }
}
