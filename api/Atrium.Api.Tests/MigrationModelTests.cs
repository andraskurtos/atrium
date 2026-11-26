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
        Assert.Contains("20261126150000_AddRecurrenceOverrides", sql, StringComparison.Ordinal);
        Assert.Contains("20261126180000_AddUniversityModule", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE calendars", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE calendar_events", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE university_courses", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE university_course_activities", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE university_activity_items", sql, StringComparison.Ordinal);
        Assert.Contains("ix_calendar_events_calendar_external_uid", sql, StringComparison.Ordinal);
        Assert.Contains("ix_calendar_events_master_original_start", sql, StringComparison.Ordinal);
        Assert.Contains("ix_calendars_user_module", sql, StringComparison.Ordinal);
        Assert.Contains("ix_university_activity_items_calendar_event", sql, StringComparison.Ordinal);
    }
}
