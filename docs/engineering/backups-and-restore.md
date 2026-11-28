# Backups & Restore (Local Self-Hosted)

Atrium stores persistent state in two places:
1. **PostgreSQL** (`atrium-postgres` container, `atrium_pgdata` volume) — notes, courses, quizzes, calendar events, chat threads, and encrypted AI provider keys.
2. **Uploads** (`atrium_uploads` Docker volume at `/app/uploads`, or `api/Atrium.Api/uploads` when running locally without the app container) — PDFs, extracted slide images, chat attachments, and note images.

> **Important (`.env` / `ATRIUM_JWT_SECRET`):** Stored AI provider and web search API keys are encrypted with AES-GCM using a key derived from `ATRIUM_JWT_SECRET` in `.env`. By default, `.env` is **not** bundled into backup archives so backups can be safely copied to other drives. Keep a separate secure copy of `.env` (for example in your password manager) if you want AI API keys to decrypt automatically after restoring onto a brand-new machine.

---

## Creating a Backup

```bash
# Run an immediate verified backup into ./backups/
./scripts/backup.sh
# or
npm run backup
```

Each backup produces a `chmod 600` archive (`atrium-backup-YYYYMMDD-HHMMSS.tar.gz`) and a `.sha256` sidecar containing:
- `database.dump` — atomic `pg_dump --format=custom` snapshot, verified with `pg_restore --list`
- `uploads.tar.gz` — archive of all uploaded files
- `manifest.json` — metadata, file counts, sizes, and SHA-256 checksums for every inner file

### Useful Options

```bash
# Copy every backup automatically to a second drive or mounted NAS
./scripts/backup.sh --copy-to /mnt/backup-drive/atrium

# Change retention (default: keep 14 most recent backups; 0 = keep all)
./scripts/backup.sh --keep 30

# List existing backups
./scripts/backup.sh --list

# Also sync offsite with rclone (optional)
./scripts/backup.sh --rclone-remote b2:my-bucket/atrium
```

You can persist these defaults in `.env` (`ATRIUM_BACKUP_DIR`, `ATRIUM_BACKUP_KEEP`, `ATRIUM_BACKUP_COPY_DIR`, `ATRIUM_BACKUP_RCLONE_REMOTE`).

---

## Automated Daily Backups (Local Linux Host)

Install a user-level `systemd` timer (no `sudo` required):

```bash
./scripts/backup.sh --install-timer
```

- Runs daily at **03:30** (`OnCalendar=*-*-* 03:30:00`).
- Uses `Persistent=true`, so if your machine was off or asleep at 03:30, the backup runs automatically shortly after boot.
- Check status & logs:
  ```bash
  systemctl --user list-timers atrium-backup.timer
  journalctl --user -u atrium-backup.service -n 50
  ```
- Remove the timer:
  ```bash
  ./scripts/backup.sh --uninstall-timer
  ```

---

## Verifying & Restoring a Backup

### Verify a backup without modifying anything

```bash
./scripts/restore.sh --latest --verify-only
```

### Restore the latest (or a specific) backup

```bash
./scripts/restore.sh --latest
# or
./scripts/restore.sh backups/atrium-backup-20261128-033000.tar.gz
```

What `restore.sh` does safely:
1. Verifies the outer `.sha256`, every inner checksum in `manifest.json`, and `pg_restore --list`.
2. Prompts for confirmation (`restore`) unless `-y` is passed.
3. Takes an automatic **pre-restore safety backup** first (unless `--skip-pre-backup` is passed).
4. Stops the `atrium` app container during the restore so nothing writes mid-restore.
5. Restores PostgreSQL atomically (`--single-transaction --exit-on-error`) and restores uploads with proper non-root ownership (`1654:1654`).
6. Restarts `atrium` and verifies `/api/health`.

