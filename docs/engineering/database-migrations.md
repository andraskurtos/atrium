# Atrium — Database Migrations Guide

Atrium uses **Entity Framework Core 10** (`Npgsql.EntityFrameworkCore.PostgreSQL`) with code-first migrations stored in [`api/Atrium.Api/Data/Migrations/`](../../api/Atrium.Api/Data/Migrations/).

## Current Migrations

| Migration ID | Summary |
| :--- | :--- |
| `20261125222000_InitialAuthAndAccounts` | Creates `users`, `user_preferences`, `refresh_sessions`, `invites`, and `agent_tokens`. |
| `20261126120000_AddCalendarsAndEvents` | Creates `calendars` and `calendar_events` (multi-calendar support, overlapping events, and idempotent `.ics` UID indexing). |
| `20261126150000_AddRecurrenceOverrides` | Adds `recurrence_master_id`, `recurrence_original_start_at`, `is_recurrence_exception_deleted`, and `ix_calendar_events_master_original_start` for recurring event series overrides and single-occurrence deletions. |
| `20261126180000_AddUniversityModule` | Adds `calendars.module` (`ix_calendars_user_module`) and creates `university_courses`, `university_course_activities`, and `university_activity_items` for the University courses, attendance, and dedicated calendar module. |
| `20261126230000_AddDailyTracker` | Creates `daily_check_items` (`ix_daily_check_items_user_date_sort`) and `daily_notes` (`ix_daily_notes_user_date`) for per-day checklists and auto-saving daily shortform notes. |

---

## Option 1: Automatic Migration on Startup (Default)

By default, `Atrium:Database:AutoMigrate` is `true` in [`Program.cs`](../../api/Atrium.Api/Program.cs). Starting the API automatically runs `db.Database.MigrateAsync()` against PostgreSQL before serving requests:

- **Local development:**
  ```bash
  npm run dev
  ```
- **Docker Compose / self-hosted production:**
  ```bash
  git pull
  docker compose up -d --build
  ```

Existing users created before `20261126120000_AddCalendarsAndEvents` automatically receive a default `"Personal"` calendar the first time they load calendars or create an event.

---

## Option 2: Apply Migrations Explicitly (Without Starting the Web Server)

### Local / `.env` Postgres
```bash
npm run db:migrate
```
Or directly via .NET CLI (providing your connection string via `.env` or environment variable):
```bash
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5433;Database=atrium;Username=atrium;Password=YOUR_PASSWORD" \
  dotnet run --project api/Atrium.Api/Atrium.Api.csproj -- --migrate
```

### Inside Docker Compose
```bash
docker compose exec atrium dotnet Atrium.Api.dll --migrate
```

---

## Option 3: Generate & Apply an Idempotent SQL Script (Manual DBA Review)

If you disable automatic migrations (`Atrium__Database__AutoMigrate=false`) or want to inspect the exact PostgreSQL DDL before running it:

1. **Generate the idempotent SQL script:**
   ```bash
   npm run db:migration-script > atrium-migrations.sql
   ```
   *(Or `dotnet run --project api/Atrium.Api/Atrium.Api.csproj -- --migration-script > atrium-migrations.sql`.)*

2. **Inspect `atrium-migrations.sql`**, then apply it to your PostgreSQL database:
   - **Docker Compose Postgres:**
     ```bash
     cat atrium-migrations.sql | docker compose exec -T postgres psql -U atrium -d atrium
     ```
   - **Direct `psql`:**
     ```bash
     psql "postgresql://atrium:YOUR_PASSWORD@127.0.0.1:5433/atrium" -f atrium-migrations.sql
     ```

Because the script checks `"__EFMigrationsHistory"` for each migration ID, it is safe to run against both brand-new databases and existing databases that already have `20261125222000_InitialAuthAndAccounts` applied.

---

## Verifying Applied Migrations

Check the applied migrations recorded in PostgreSQL:
```bash
docker compose exec -T postgres psql -U atrium -d atrium \
  -c 'SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
```
Expected rows:
- `20261125222000_InitialAuthAndAccounts`
- `20261126120000_AddCalendarsAndEvents`
- `20261126150000_AddRecurrenceOverrides`
- `20261126180000_AddUniversityModule`
- `20261126230000_AddDailyTracker`
