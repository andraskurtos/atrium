using Atrium.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class MigrationModelTests
{
    private const string DummyNpgsqlConnection =
        "Host=127.0.0.1;Port=5433;Database=atrium_design_check;Username=atrium;Password=test";

    [Fact]
    public void NpgsqlModel_HasNoPendingModelChanges_AgainstMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DummyNpgsqlConnection)
            .Options;

        using var db = new AppDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void IdempotentMigrationScript_ContainsCalendarsAndEventsTablesAndIndexes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DummyNpgsqlConnection)
            .Options;

        using var db = new AppDbContext(options);
        var migrator = db.Database.GetInfrastructure().GetRequiredService<IMigrator>();
        var sql = migrator.GenerateScript(
            fromMigration: null,
            toMigration: null,
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("20261125222000_InitialAuthAndAccounts", sql, StringComparison.Ordinal);
        Assert.Contains("20261126120000_AddCalendarsAndEvents", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE calendars", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE calendar_events", sql, StringComparison.Ordinal);
        Assert.Contains("ix_calendar_events_calendar_external_uid", sql, StringComparison.Ordinal);
    }
}
