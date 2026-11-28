#!/usr/bin/env bash
set -Eeuo pipefail

# Atrium Restore Utility — Verified PostgreSQL + Uploads Restore
#
# Restores a backup archive created by scripts/backup.sh:
#   1. Verifies archive + inner SHA-256 checksums and pg_restore --list
#   2. Takes an automatic pre-restore safety backup (unless --skip-pre-backup)
#   3. Stops the atrium app container (if running) during restore
#   4. Atomically restores PostgreSQL (--single-transaction --exit-on-error)
#   5. Restores uploads to the Docker atrium_uploads volume or host directory
#   6. Restarts the atrium app container (if it was running) and checks /api/health
#
# Usage:
#   ./scripts/restore.sh --latest
#   ./scripts/restore.sh backups/atrium-backup-20261128-033000.tar.gz
#   ./scripts/restore.sh --latest --verify-only

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/docker-compose.yml"
ENV_FILE="${REPO_ROOT}/.env"

if [[ -t 1 ]]; then
  C_RESET=$'\033[0m'
  C_BOLD=$'\033[1m'
  C_CYAN=$'\033[36m'
  C_GREEN=$'\033[32m'
  C_YELLOW=$'\033[33m'
  C_RED=$'\033[31m'
else
  C_RESET='' C_BOLD='' C_CYAN='' C_GREEN='' C_YELLOW='' C_RED=''
fi

info()    { printf '%s[restore]%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
success() { printf '%s[ok]%s      %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn()    { printf '%s[warn]%s    %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
error()   { printf '%s[error]%s   %s\n' "$C_RED" "$C_RESET" "$*" >&2; }

read_env_var() {
  local key="$1"
  local default_val="${2:-}"
  if [[ -f "$ENV_FILE" ]]; then
    local line
    line="$(grep -E "^[[:space:]]*${key}=" "$ENV_FILE" | tail -n 1 || true)"
    if [[ -n "$line" ]]; then
      local val="${line#*=}"
      val="${val%\"}"
      val="${val#\"}"
      val="${val%\'}"
      val="${val#\'}"
      printf '%s' "$val"
      return 0
    fi
  fi
  printf '%s' "$default_val"
}

POSTGRES_DB="${POSTGRES_DB:-$(read_env_var POSTGRES_DB atrium)}"
POSTGRES_USER="${POSTGRES_USER:-$(read_env_var POSTGRES_USER atrium)}"
API_PORT="${API_PORT:-$(read_env_var API_PORT 5000)}"
BACKUP_DIR="${ATRIUM_BACKUP_DIR:-$(read_env_var ATRIUM_BACKUP_DIR "${REPO_ROOT}/backups")}"
UPLOADS_MODE="${ATRIUM_BACKUP_UPLOADS_SOURCE:-$(read_env_var ATRIUM_BACKUP_UPLOADS_SOURCE auto)}"
UPLOADS_DIR="${ATRIUM_BACKUP_UPLOADS_DIR:-$(read_env_var ATRIUM_BACKUP_UPLOADS_DIR "")}"
CONFIGURED_UPLOADS_PATH="${ATRIUM_UPLOADS_PATH:-$(read_env_var ATRIUM_UPLOADS_PATH "")}"
if [[ "$UPLOADS_MODE" == "auto" && -z "$UPLOADS_DIR" && -n "$CONFIGURED_UPLOADS_PATH" ]]; then
  UPLOADS_MODE="host-dir"
  UPLOADS_DIR="$CONFIGURED_UPLOADS_PATH"
fi

ARCHIVE_PATH=""
USE_LATEST=0
VERIFY_ONLY=0
DB_ONLY=0
UPLOADS_ONLY=0
RESTORE_ENV=0
SKIP_PRE_BACKUP=0
ASSUME_YES=0
WORK_DIR=""
APP_WAS_RUNNING=0

cleanup() {
  if [[ -n "$WORK_DIR" && -d "$WORK_DIR" ]]; then
    rm -rf "$WORK_DIR"
  fi
}
trap cleanup EXIT

usage() {
  cat <<'EOF'
Atrium Restore Utility — Verified PostgreSQL + Uploads Restore

Usage:
  ./scripts/restore.sh <archive.tar.gz> [options]
  ./scripts/restore.sh --latest [options]

Options:
  --latest                 Restore the newest backup in the backup directory
  --backup-dir <dir>       Backup directory for --latest and safety backups (default: ./backups)
  --verify-only            Verify checksums and dump readability without modifying anything
  --db-only                Restore only PostgreSQL (leave uploads untouched)
  --uploads-only           Restore only uploads (leave PostgreSQL untouched)
  --uploads-dir <path>     Restore uploads into a host directory (e.g. api/Atrium.Api/uploads)
  --docker-volume          Force restoring uploads into the Docker atrium_uploads volume
  --restore-env            Also restore .env if present in the backup (backs up current .env first)
  --skip-pre-backup        Skip taking an automatic safety backup before restoring
  -y, --yes                Skip interactive confirmation prompt
  -h, --help               Show this help message

Examples:
  ./scripts/restore.sh --latest --verify-only
  ./scripts/restore.sh --latest
  ./scripts/restore.sh backups/atrium-backup-20261128-033000.tar.gz -y
EOF
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --latest)
        USE_LATEST=1
        shift
        ;;
      --backup-dir)
        BACKUP_DIR="$2"
        shift 2
        ;;
      --verify-only)
        VERIFY_ONLY=1
        shift
        ;;
      --db-only)
        DB_ONLY=1
        shift
        ;;
      --uploads-only)
        UPLOADS_ONLY=1
        shift
        ;;
      --uploads-dir)
        UPLOADS_MODE="host-dir"
        UPLOADS_DIR="$2"
        shift 2
        ;;
      --docker-volume)
        UPLOADS_MODE="docker-volume"
        shift
        ;;
      --restore-env)
        RESTORE_ENV=1
        shift
        ;;
      --skip-pre-backup)
        SKIP_PRE_BACKUP=1
        shift
        ;;
      -y|--yes)
        ASSUME_YES=1
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      -*)
        error "Unknown option: $1"
        usage
        exit 1
        ;;
      *)
        if [[ -z "$ARCHIVE_PATH" ]]; then
          ARCHIVE_PATH="$1"
          shift
        else
          error "Unexpected argument: $1"
          usage
          exit 1
        fi
        ;;
    esac
  done

  if [[ "$DB_ONLY" -eq 1 && "$UPLOADS_ONLY" -eq 1 ]]; then
    error "Cannot combine --db-only and --uploads-only."
    exit 1
  fi

  if [[ "$USE_LATEST" -eq 1 ]]; then
    if [[ ! -d "$BACKUP_DIR" ]]; then
      error "Backup directory '${BACKUP_DIR}' does not exist."
      exit 1
    fi
    local latest_name
    latest_name="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'atrium-backup-*.tar.gz' -printf '%f\n' | sort -r | head -n 1 || true)"
    if [[ -z "$latest_name" ]]; then
      error "No backups found in '${BACKUP_DIR}'."
      exit 1
    fi
    ARCHIVE_PATH="${BACKUP_DIR}/${latest_name}"
  fi

  if [[ -z "$ARCHIVE_PATH" ]]; then
    error "Please provide a backup archive path or pass --latest."
    usage
    exit 1
  fi

  if [[ ! -f "$ARCHIVE_PATH" ]]; then
    error "Backup archive not found: ${ARCHIVE_PATH}"
    exit 1
  fi
}

sha256_file() {
  local target="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$target" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$target" | awk '{print $1}'
  else
    error "Neither sha256sum nor shasum is installed."
    exit 1
  fi
}

manifest_sha256_for() {
  local manifest="$1"
  local entry="$2"
  grep -F "\"${entry}\"" "$manifest" | sed -E 's/.*:[[:space:]]*"([^"]+)".*/\1/' | head -n 1 || true
}

detect_docker_uploads_volume() {
  local vol=""
  vol="$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/app/uploads"}}{{.Name}}{{end}}{{end}}' atrium-app 2>/dev/null || true)"
  if [[ -n "$vol" ]]; then
    printf '%s' "$vol"
    return 0
  fi

  local project_name
  project_name="$(basename "$REPO_ROOT" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9_-]//g')"
  vol="$(docker volume ls -q --filter "label=com.docker.compose.volume=atrium_uploads" --filter "label=com.docker.compose.project=${project_name}" 2>/dev/null | head -n 1 || true)"
  if [[ -n "$vol" ]]; then
    printf '%s' "$vol"
    return 0
  fi

  vol="$(docker volume ls -q --filter "label=com.docker.compose.volume=atrium_uploads" 2>/dev/null | head -n 1 || true)"
  printf '%s' "$vol"
}

verify_archive() {
  info "Verifying archive: ${ARCHIVE_PATH}"

  # 1. Optional sidecar .sha256 check
  if [[ -f "${ARCHIVE_PATH}.sha256" ]]; then
    local expected_outer actual_outer
    expected_outer="$(awk '{print $1}' "${ARCHIVE_PATH}.sha256")"
    actual_outer="$(sha256_file "$ARCHIVE_PATH")"
    if [[ "$expected_outer" != "$actual_outer" ]]; then
      error "Outer archive SHA-256 mismatch! Expected ${expected_outer}, got ${actual_outer}."
      exit 1
    fi
    success "Outer archive SHA-256 verified."
  fi

  WORK_DIR="$(mktemp -d)"
  chmod 700 "$WORK_DIR"
  tar -xzf "$ARCHIVE_PATH" -C "$WORK_DIR"

  local manifest="${WORK_DIR}/manifest.json"
  local db_dump="${WORK_DIR}/database.dump"
  local uploads_tar="${WORK_DIR}/uploads.tar.gz"

  if [[ ! -f "$manifest" || ! -f "$db_dump" || ! -f "$uploads_tar" ]]; then
    error "Archive is missing required files (manifest.json, database.dump, uploads.tar.gz)."
    exit 1
  fi

  # 2. Verify inner SHA-256 checksums from manifest.json
  local expected_db actual_db expected_up actual_up
  expected_db="$(manifest_sha256_for "$manifest" "database.dump")"
  actual_db="$(sha256_file "$db_dump")"
  if [[ -z "$expected_db" || "$expected_db" != "$actual_db" ]]; then
    error "database.dump SHA-256 mismatch!"
    exit 1
  fi

  expected_up="$(manifest_sha256_for "$manifest" "uploads.tar.gz")"
  actual_up="$(sha256_file "$uploads_tar")"
  if [[ -z "$expected_up" || "$expected_up" != "$actual_up" ]]; then
    error "uploads.tar.gz SHA-256 mismatch!"
    exit 1
  fi

  if [[ -f "${WORK_DIR}/.env" ]]; then
    local expected_env actual_env
    expected_env="$(manifest_sha256_for "$manifest" ".env")"
    actual_env="$(sha256_file "${WORK_DIR}/.env")"
    if [[ -n "$expected_env" && "$expected_env" != "$actual_env" ]]; then
      error ".env SHA-256 mismatch!"
      exit 1
    fi
  fi
  success "Inner manifest checksums verified."

  # 3. Verify uploads tarball readability
  if ! tar -tzf "$uploads_tar" >/dev/null 2>&1; then
    error "uploads.tar.gz is corrupted or unreadable."
    exit 1
  fi

  # 4. Verify PostgreSQL dump readability (if postgres container is running)
  if [[ "$UPLOADS_ONLY" -eq 0 ]]; then
    local pg_running
    pg_running="$(docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null | grep -x 'postgres' || true)"
    if [[ -z "$pg_running" ]]; then
      error "PostgreSQL container is not running. Start it with: docker compose up -d postgres"
      exit 1
    fi
    if ! docker compose -f "$COMPOSE_FILE" exec -T postgres pg_restore --list < "$db_dump" >/dev/null; then
      error "database.dump failed pg_restore --list verification."
      exit 1
    fi
    success "database.dump verified with pg_restore --list."
  fi

  printf '\n%sBackup manifest:%s\n' "$C_BOLD" "$C_RESET"
  cat "$manifest"
  printf '\n'
}

restore_database() {
  info "Restoring PostgreSQL database '${POSTGRES_DB}'..."
  local db_dump="${WORK_DIR}/database.dump"

  # Terminate active connections to the target DB
  docker compose -f "$COMPOSE_FILE" exec -T postgres \
    psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d postgres -c \
    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${POSTGRES_DB}' AND pid <> pg_backend_pid();" \
    >/dev/null

  # Reset public schema so stale tables/migrations don't conflict
  docker compose -f "$COMPOSE_FILE" exec -T postgres \
    psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
    "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO \"${POSTGRES_USER}\"; GRANT ALL ON SCHEMA public TO public;" \
    >/dev/null

  # Restore atomically in a single transaction
  docker compose -f "$COMPOSE_FILE" exec -T postgres \
    pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
      --no-owner --no-acl --single-transaction --exit-on-error \
    < "$db_dump"

  success "PostgreSQL database restored cleanly."
}

restore_uploads() {
  local uploads_tar="${WORK_DIR}/uploads.tar.gz"
  local docker_vol=""
  local target_mode="$UPLOADS_MODE"

  if [[ "$target_mode" == "auto" ]]; then
    docker_vol="$(detect_docker_uploads_volume)"
    local local_uploads="${REPO_ROOT}/api/Atrium.Api/uploads"
    local app_container_exists=""
    app_container_exists="$(docker ps -a --format '{{.Names}}' 2>/dev/null | grep -x 'atrium-app' || true)"

    if [[ -n "$docker_vol" && ( -n "$app_container_exists" || ! -d "$local_uploads" ) ]]; then
      target_mode="docker-volume"
    elif [[ -d "$local_uploads" ]]; then
      target_mode="host-dir"
      UPLOADS_DIR="$local_uploads"
    elif [[ -n "$docker_vol" ]]; then
      target_mode="docker-volume"
    else
      target_mode="host-dir"
      UPLOADS_DIR="$local_uploads"
    fi
  elif [[ "$target_mode" == "docker-volume" ]]; then
    docker_vol="$(detect_docker_uploads_volume)"
    if [[ -z "$docker_vol" ]]; then
      error "Requested --docker-volume, but no atrium_uploads Docker volume was found."
      exit 1
    fi
  elif [[ "$target_mode" == "host-dir" && -z "$UPLOADS_DIR" ]]; then
    UPLOADS_DIR="${REPO_ROOT}/api/Atrium.Api/uploads"
  fi

  if [[ "$target_mode" == "docker-volume" ]]; then
    info "Restoring uploads into Docker volume '${docker_vol}'..."
    # Clear existing files and extract archive; set ownership to .NET 10 non-root APP_UID (1654)
    docker run --rm -i -v "${docker_vol}:/uploads" postgres:17-alpine sh -c \
      'find /uploads -mindepth 1 -delete && tar -xzf - -C /uploads && chown -R 1654:1654 /uploads' \
      < "$uploads_tar"
    success "Uploads restored into Docker volume '${docker_vol}'."
  else
    info "Restoring uploads into host directory '${UPLOADS_DIR}'..."
    mkdir -p "$UPLOADS_DIR"
    find "$UPLOADS_DIR" -mindepth 1 -delete
    tar -xzf "$uploads_tar" -C "$UPLOADS_DIR"
    success "Uploads restored into '${UPLOADS_DIR}'."
  fi
}

main() {
  parse_args "$@"

  if ! command -v docker >/dev/null 2>&1; then
    error "Docker is required to restore Atrium backups."
    exit 1
  fi

  verify_archive

  if [[ "$VERIFY_ONLY" -eq 1 ]]; then
    success "Verification succeeded (--verify-only). No changes were made."
    exit 0
  fi

  if [[ "$ASSUME_YES" -ne 1 ]]; then
    warn "This will OVERWRITE your current Atrium data with:"
    printf '  %s%s%s\n\n' "$C_BOLD" "$ARCHIVE_PATH" "$C_RESET"
    local reply
    read -r -p "Type 'restore' to continue: " reply
    if [[ "$reply" != "restore" ]]; then
      info "Restore cancelled."
      exit 0
    fi
  fi

  # Automatic safety backup before overwriting
  if [[ "$SKIP_PRE_BACKUP" -eq 0 ]]; then
    info "Taking a pre-restore safety backup first..."
    "${SCRIPT_DIR}/backup.sh" --out "$BACKUP_DIR"
  fi

  # Stop atrium container if currently running so nothing writes mid-restore
  local app_running
  app_running="$(docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null | grep -x 'atrium' || true)"
  if [[ -n "$app_running" ]]; then
    APP_WAS_RUNNING=1
    info "Stopping atrium container during restore..."
    docker compose -f "$COMPOSE_FILE" stop atrium >/dev/null
  fi

  if [[ "$UPLOADS_ONLY" -eq 0 ]]; then
    restore_database
  fi

  if [[ "$DB_ONLY" -eq 0 ]]; then
    restore_uploads
  fi

  if [[ "$RESTORE_ENV" -eq 1 ]]; then
    if [[ -f "${WORK_DIR}/.env" ]]; then
      if [[ -f "$ENV_FILE" ]]; then
        local env_bak="${ENV_FILE}.pre-restore.$(date -u +%Y%m%d-%H%M%S)"
        cp "$ENV_FILE" "$env_bak"
        chmod 600 "$env_bak"
        info "Saved existing .env to ${env_bak}"
      fi
      cp "${WORK_DIR}/.env" "$ENV_FILE"
      chmod 600 "$ENV_FILE"
      success "Restored .env from backup."
    else
      warn "--restore-env was passed, but this backup archive does not contain .env."
    fi
  fi

  if [[ "$APP_WAS_RUNNING" -eq 1 ]]; then
    info "Starting atrium container..."
    docker compose -f "$COMPOSE_FILE" start atrium >/dev/null

    local health_url="http://127.0.0.1:${API_PORT}/api/health"
    local attempt=1
    local ok=0
    while [[ "$attempt" -le 20 ]]; do
      if curl -fsS --max-time 3 "$health_url" 2>/dev/null | grep -q '"status":"ok"'; then
        ok=1
        break
      fi
      sleep 2
      attempt=$((attempt + 1))
    done
    if [[ "$ok" -eq 1 ]]; then
      success "Atrium is back up and healthy (${health_url})."
    else
      warn "Atrium started, but ${health_url} did not report ok within 40s. Check: docker compose logs atrium"
    fi
  fi

  success "Restore complete!"
  if [[ "$RESTORE_ENV" -eq 0 ]]; then
    info "Note: if you restored onto a new machine with a different ATRIUM_JWT_SECRET, re-enter any saved AI provider API keys in Settings."
  fi
}

main "$@"

