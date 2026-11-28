#!/usr/bin/env bash
set -Eeuo pipefail

# Atrium Backup Utility — Atomic PostgreSQL + Uploads Snapshot
#
# Creates a verified, self-contained .tar.gz archive containing:
#   - database.dump   (pg_dump --format=custom, verified with pg_restore --list)
#   - uploads.tar.gz  (Atrium uploads from Docker volume or local host directory)
#   - manifest.json   (metadata + SHA-256 checksums)
#   - .env            (ONLY when --include-env is explicitly passed)
#
# Usage:
#   ./scripts/backup.sh [options]
#   ./scripts/backup.sh --copy-to /mnt/usb/atrium-backups
#   ./scripts/backup.sh --install-timer

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/docker-compose.yml"
ENV_FILE="${REPO_ROOT}/.env"

# Terminal colors (disabled when stdout is not a TTY)
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

info()    { printf '%s[backup]%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
success() { printf '%s[ok]%s     %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn()    { printf '%s[warn]%s   %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
error()   { printf '%s[error]%s  %s\n' "$C_RED" "$C_RESET" "$*" >&2; }

# Safe .env reader (does not execute arbitrary shell code)
read_env_var() {
  local key="$1"
  local default_val="${2:-}"
  if [[ -f "$ENV_FILE" ]]; then
    local line
    line="$(grep -E "^[[:space:]]*${key}=" "$ENV_FILE" | tail -n 1 || true)"
    if [[ -n "$line" ]]; then
      local val="${line#*=}"
      # Strip optional surrounding single or double quotes
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
BACKUP_DIR="${ATRIUM_BACKUP_DIR:-$(read_env_var ATRIUM_BACKUP_DIR "${REPO_ROOT}/backups")}"
KEEP_COUNT="${ATRIUM_BACKUP_KEEP:-$(read_env_var ATRIUM_BACKUP_KEEP 14)}"
COPY_DIR="${ATRIUM_BACKUP_COPY_DIR:-$(read_env_var ATRIUM_BACKUP_COPY_DIR "")}"
RCLONE_REMOTE="${ATRIUM_BACKUP_RCLONE_REMOTE:-$(read_env_var ATRIUM_BACKUP_RCLONE_REMOTE "")}"
INCLUDE_ENV="${ATRIUM_BACKUP_INCLUDE_ENV:-$(read_env_var ATRIUM_BACKUP_INCLUDE_ENV false)}"
UPLOADS_MODE="${ATRIUM_BACKUP_UPLOADS_SOURCE:-$(read_env_var ATRIUM_BACKUP_UPLOADS_SOURCE auto)}"
UPLOADS_DIR="${ATRIUM_BACKUP_UPLOADS_DIR:-$(read_env_var ATRIUM_BACKUP_UPLOADS_DIR "")}"
CONFIGURED_UPLOADS_PATH="${ATRIUM_UPLOADS_PATH:-$(read_env_var ATRIUM_UPLOADS_PATH "")}"
if [[ "$UPLOADS_MODE" == "auto" && -z "$UPLOADS_DIR" && -n "$CONFIGURED_UPLOADS_PATH" ]]; then
  UPLOADS_MODE="host-dir"
  UPLOADS_DIR="$CONFIGURED_UPLOADS_PATH"
fi
TIMER_SCHEDULE="${ATRIUM_BACKUP_SCHEDULE:-$(read_env_var ATRIUM_BACKUP_SCHEDULE "*-*-* 03:30:00")}"

ACTION="backup"
WORK_DIR=""

cleanup() {
  if [[ -n "$WORK_DIR" && -d "$WORK_DIR" ]]; then
    rm -rf "$WORK_DIR"
  fi
}
trap cleanup EXIT

usage() {
  cat <<'EOF'
Atrium Backup Utility — Atomic PostgreSQL + Uploads Snapshot

Usage:
  ./scripts/backup.sh [options]

Options:
  -o, --out <dir>              Backup output directory (default: ./backups)
  --keep <N>                   Keep the N most recent backups (default: 14, 0 = keep all)
  --uploads-dir <path>         Back up uploads from a host directory (e.g. api/Atrium.Api/uploads)
  --docker-volume              Force backing up uploads from the Docker atrium_uploads volume
  --copy-to <dir>              Also copy the verified backup to a second local/NAS/USB path
  --rclone-remote <remote>     Also copy the verified backup via rclone (e.g. b2:my-bucket/atrium)
  --include-env                Include .env in the backup archive (off by default for security)
  --list                       List existing backups in the backup directory
  --install-timer [schedule]   Install a daily user-level systemd timer (default: "*-*-* 03:30:00")
  --uninstall-timer            Disable and remove the user-level systemd timer
  -h, --help                   Show this help message

Examples:
  ./scripts/backup.sh
  ./scripts/backup.sh --out /mnt/backup-drive/atrium --keep 30
  ./scripts/backup.sh --copy-to /mnt/nas/atrium-backups
  ./scripts/backup.sh --install-timer
  ./scripts/backup.sh --list
EOF
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -o|--out)
        BACKUP_DIR="$2"
        shift 2
        ;;
      --keep)
        KEEP_COUNT="$2"
        shift 2
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
      --copy-to)
        COPY_DIR="$2"
        shift 2
        ;;
      --rclone-remote)
        RCLONE_REMOTE="$2"
        shift 2
        ;;
      --include-env)
        INCLUDE_ENV="true"
        shift
        ;;
      --list)
        ACTION="list"
        shift
        ;;
      --install-timer)
        ACTION="install-timer"
        if [[ $# -ge 2 && "$2" != -* ]]; then
          TIMER_SCHEDULE="$2"
          shift 2
        else
          shift
        fi
        ;;
      --uninstall-timer)
        ACTION="uninstall-timer"
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        error "Unknown option: $1"
        usage
        exit 1
        ;;
    esac
  done
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

file_size_bytes() {
  local target="$1"
  stat -c%s "$target" 2>/dev/null || stat -f%z "$target"
}

detect_docker_uploads_volume() {
  # 1. Check the atrium-app container mount first if it exists
  local vol=""
  vol="$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/app/uploads"}}{{.Name}}{{end}}{{end}}' atrium-app 2>/dev/null || true)"
  if [[ -n "$vol" ]]; then
    printf '%s' "$vol"
    return 0
  fi

  # 2. Fall back to finding the compose-labeled atrium_uploads volume for this repo
  local project_name
  project_name="$(basename "$REPO_ROOT" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9_-]//g')"
  vol="$(docker volume ls -q --filter "label=com.docker.compose.volume=atrium_uploads" --filter "label=com.docker.compose.project=${project_name}" 2>/dev/null | head -n 1 || true)"
  if [[ -n "$vol" ]]; then
    printf '%s' "$vol"
    return 0
  fi

  # 3. Last resort: any volume named *_atrium_uploads or atrium_uploads
  vol="$(docker volume ls -q --filter "label=com.docker.compose.volume=atrium_uploads" 2>/dev/null | head -n 1 || true)"
  printf '%s' "$vol"
}

prune_backups_in_dir() {
  local dir="$1"
  local keep="$2"
  if [[ ! "$keep" =~ ^[0-9]+$ ]] || [[ "$keep" -le 0 ]]; then
    return 0
  fi

  local -a archives=()
  while IFS= read -r f; do
    archives+=("$f")
  done < <(find "$dir" -maxdepth 1 -type f -name 'atrium-backup-*.tar.gz' -printf '%f\n' | sort -r)

  local total="${#archives[@]}"
  if [[ "$total" -le "$keep" ]]; then
    return 0
  fi

  local idx
  for (( idx = keep; idx < total; idx++ )); do
    local old="${dir}/${archives[$idx]}"
    rm -f "$old" "${old}.sha256"
    info "Pruned old backup: ${archives[$idx]}"
  done
}

list_backups() {
  if [[ ! -d "$BACKUP_DIR" ]]; then
    info "No backup directory found at ${BACKUP_DIR}."
    exit 0
  fi

  local -a archives=()
  while IFS= read -r f; do
    archives+=("$f")
  done < <(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'atrium-backup-*.tar.gz' -printf '%f\n' | sort -r)

  if [[ "${#archives[@]}" -eq 0 ]]; then
    info "No backups found in ${BACKUP_DIR}."
    exit 0
  fi

  printf '%sBackups in %s:%s\n' "$C_BOLD" "$BACKUP_DIR" "$C_RESET"
  local f size
  for f in "${archives[@]}"; do
    size="$(du -h "${BACKUP_DIR}/${f}" | awk '{print $1}')"
    printf '  %-42s  %8s\n' "$f" "$size"
  done
}

install_timer() {
  if ! command -v systemctl >/dev/null 2>&1 || ! systemctl --user status >/dev/null 2>&1; then
    warn "User-level systemd is not available in this session."
    printf '\nYou can schedule daily backups with crontab -e:\n'
    printf '  30 3 * * * %s/scripts/backup.sh >> %s/backup.log 2>&1\n\n' "$REPO_ROOT" "$BACKUP_DIR"
    exit 1
  fi

  local unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
  mkdir -p "$unit_dir"

  cat > "${unit_dir}/atrium-backup.service" <<EOF
[Unit]
Description=Atrium PostgreSQL & Uploads Backup
After=network-online.target

[Service]
Type=oneshot
WorkingDirectory=${REPO_ROOT}
ExecStart=${REPO_ROOT}/scripts/backup.sh
Nice=10
IOSchedulingClass=idle
EOF

  cat > "${unit_dir}/atrium-backup.timer" <<EOF
[Unit]
Description=Daily Atrium Backup Timer

[Timer]
OnCalendar=${TIMER_SCHEDULE}
Persistent=true
RandomizedDelaySec=5m

[Install]
WantedBy=timers.target
EOF

  systemctl --user daemon-reload
  systemctl --user enable --now atrium-backup.timer

  success "Installed and enabled user systemd timer: atrium-backup.timer (${TIMER_SCHEDULE})"
  info "Persistent=true is enabled: missed runs while the machine is off will run shortly after boot."
  info "Check status with: systemctl --user list-timers atrium-backup.timer"
  info "View logs with:    journalctl --user -u atrium-backup.service -n 50"
}

uninstall_timer() {
  local unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
  if command -v systemctl >/dev/null 2>&1; then
    systemctl --user disable --now atrium-backup.timer 2>/dev/null || true
  fi
  rm -f "${unit_dir}/atrium-backup.timer" "${unit_dir}/atrium-backup.service"
  if command -v systemctl >/dev/null 2>&1; then
    systemctl --user daemon-reload 2>/dev/null || true
  fi
  success "Removed user systemd timer: atrium-backup.timer"
}

run_backup() {
  if ! command -v docker >/dev/null 2>&1; then
    error "Docker is required to run Atrium backups."
    exit 1
  fi

  if [[ ! -f "$COMPOSE_FILE" ]]; then
    error "Could not find docker-compose.yml at ${COMPOSE_FILE}."
    exit 1
  fi

  # Verify postgres container is running
  local pg_running
  pg_running="$(docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null | grep -x 'postgres' || true)"
  if [[ -z "$pg_running" ]]; then
    error "PostgreSQL container is not running. Start it with: docker compose up -d postgres"
    exit 1
  fi

  mkdir -p "$BACKUP_DIR"
  WORK_DIR="$(mktemp -d "${BACKUP_DIR}/.atrium-backup-stage.XXXXXX")"
  chmod 700 "$WORK_DIR"

  local timestamp created_utc git_commit
  timestamp="$(date -u +%Y%m%d-%H%M%S)"
  created_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  git_commit="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || printf 'unknown')"

  # 1. Atomic PostgreSQL dump (custom compressed format)
  info "Dumping PostgreSQL database '${POSTGRES_DB}'..."
  local db_dump="${WORK_DIR}/database.dump"
  docker compose -f "$COMPOSE_FILE" exec -T postgres \
    pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --no-owner --no-acl \
    > "$db_dump"

  if [[ ! -s "$db_dump" ]]; then
    error "Database dump is empty."
    exit 1
  fi

  # Verify dump structure with pg_restore --list
  info "Verifying PostgreSQL dump integrity..."
  if ! docker compose -f "$COMPOSE_FILE" exec -T postgres pg_restore --list < "$db_dump" >/dev/null; then
    error "Database dump failed pg_restore --list verification."
    exit 1
  fi
  local db_bytes db_sha
  db_bytes="$(file_size_bytes "$db_dump")"
  db_sha="$(sha256_file "$db_dump")"
  success "Database dump verified (${db_bytes} bytes)."

  # 2. Uploads archive
  local uploads_tar="${WORK_DIR}/uploads.tar.gz"
  local uploads_source="none"
  local uploads_file_count=0
  local docker_vol=""

  if [[ "$UPLOADS_MODE" == "docker-volume" ]]; then
    docker_vol="$(detect_docker_uploads_volume)"
    if [[ -z "$docker_vol" ]]; then
      error "Requested --docker-volume, but no atrium_uploads Docker volume was found."
      exit 1
    fi
    uploads_source="docker-volume:${docker_vol}"
  elif [[ "$UPLOADS_MODE" == "host-dir" ]]; then
    if [[ -z "$UPLOADS_DIR" ]]; then
      UPLOADS_DIR="${REPO_ROOT}/api/Atrium.Api/uploads"
    fi
    uploads_source="host-dir:${UPLOADS_DIR}"
  else
    # Auto-detect: prefer Docker volume if atrium-app exists or volume exists and local dir does not
    docker_vol="$(detect_docker_uploads_volume)"
    local local_uploads="${REPO_ROOT}/api/Atrium.Api/uploads"
    local app_container_exists=""
    app_container_exists="$(docker ps -a --format '{{.Names}}' 2>/dev/null | grep -x 'atrium-app' || true)"

    if [[ -n "$docker_vol" && ( -n "$app_container_exists" || ! -d "$local_uploads" ) ]]; then
      uploads_source="docker-volume:${docker_vol}"
    elif [[ -d "$local_uploads" ]]; then
      UPLOADS_DIR="$local_uploads"
      uploads_source="host-dir:${UPLOADS_DIR}"
    elif [[ -n "$docker_vol" ]]; then
      uploads_source="docker-volume:${docker_vol}"
    else
      uploads_source="none"
    fi
  fi

  if [[ "$uploads_source" == docker-volume:* ]]; then
    info "Archiving uploads from Docker volume '${docker_vol}'..."
    # Reuse postgres:17-alpine (already present locally) to tar the volume read-only
    docker run --rm -v "${docker_vol}:/uploads:ro" postgres:17-alpine \
      tar -czf - -C /uploads . > "$uploads_tar"
    uploads_file_count="$(tar -tzf "$uploads_tar" | grep -cv '/$' || true)"
  elif [[ "$uploads_source" == host-dir:* ]]; then
    if [[ -d "$UPLOADS_DIR" ]]; then
      info "Archiving uploads from host directory '${UPLOADS_DIR}'..."
      tar -czf "$uploads_tar" -C "$UPLOADS_DIR" .
      uploads_file_count="$(tar -tzf "$uploads_tar" | grep -cv '/$' || true)"
    else
      warn "Uploads directory '${UPLOADS_DIR}' does not exist; creating empty uploads archive."
      mkdir -p "${WORK_DIR}/empty_uploads"
      tar -czf "$uploads_tar" -C "${WORK_DIR}/empty_uploads" .
      rmdir "${WORK_DIR}/empty_uploads"
      uploads_source="none"
    fi
  else
    info "No uploads directory or Docker volume found yet; creating empty uploads archive."
    mkdir -p "${WORK_DIR}/empty_uploads"
    tar -czf "$uploads_tar" -C "${WORK_DIR}/empty_uploads" .
    rmdir "${WORK_DIR}/empty_uploads"
  fi

  local uploads_bytes uploads_sha
  uploads_bytes="$(file_size_bytes "$uploads_tar")"
  uploads_sha="$(sha256_file "$uploads_tar")"
  success "Uploads archived (${uploads_file_count} files, ${uploads_bytes} bytes, source: ${uploads_source})."

  # 3. Optional .env inclusion
  local includes_env_bool="false"
  local env_sha=""
  local -a bundle_files=("manifest.json" "database.dump" "uploads.tar.gz")
  if [[ "$INCLUDE_ENV" == "true" ]]; then
    if [[ -f "$ENV_FILE" ]]; then
      cp "$ENV_FILE" "${WORK_DIR}/.env"
      chmod 600 "${WORK_DIR}/.env"
      env_sha="$(sha256_file "${WORK_DIR}/.env")"
      includes_env_bool="true"
      bundle_files+=(".env")
      warn "Included .env in backup archive (--include-env). Store this archive securely!"
    else
      warn "--include-env requested, but ${ENV_FILE} does not exist."
    fi
  fi

  # 4. Write manifest.json
  cat > "${WORK_DIR}/manifest.json" <<EOF
{
  "formatVersion": 1,
  "createdAtUtc": "${created_utc}",
  "gitCommit": "${git_commit}",
  "postgresDb": "${POSTGRES_DB}",
  "postgresUser": "${POSTGRES_USER}",
  "uploadsSource": "${uploads_source}",
  "uploadsFileCount": ${uploads_file_count},
  "databaseBytes": ${db_bytes},
  "uploadsBytes": ${uploads_bytes},
  "includesEnv": ${includes_env_bool},
  "sha256": {
    "database.dump": "${db_sha}",
    "uploads.tar.gz": "${uploads_sha}"$(if [[ -n "$env_sha" ]]; then printf ',\n    ".env": "%s"' "$env_sha"; fi)
  }
}
EOF

  # 5. Pack final archive atomically with restricted permissions
  local archive_name="atrium-backup-${timestamp}.tar.gz"
  local tmp_archive="${BACKUP_DIR}/.${archive_name}.tmp"
  local final_archive="${BACKUP_DIR}/${archive_name}"

  tar -czf "$tmp_archive" -C "$WORK_DIR" "${bundle_files[@]}"
  chmod 600 "$tmp_archive"
  mv -f "$tmp_archive" "$final_archive"

  local final_sha final_human_size
  final_sha="$(sha256_file "$final_archive")"
  printf '%s  %s\n' "$final_sha" "$archive_name" > "${final_archive}.sha256"
  chmod 600 "${final_archive}.sha256"
  final_human_size="$(du -h "$final_archive" | awk '{print $1}')"

  success "Backup created: ${final_archive} (${final_human_size})"

  # 6. Prune old backups only after the new one succeeded
  prune_backups_in_dir "$BACKUP_DIR" "$KEEP_COUNT"

  # 7. Optional secondary local/USB/NAS copy
  if [[ -n "$COPY_DIR" ]]; then
    if mkdir -p "$COPY_DIR" 2>/dev/null; then
      cp -f "$final_archive" "${COPY_DIR}/${archive_name}"
      cp -f "${final_archive}.sha256" "${COPY_DIR}/${archive_name}.sha256"
      chmod 600 "${COPY_DIR}/${archive_name}" "${COPY_DIR}/${archive_name}.sha256" 2>/dev/null || true
      prune_backups_in_dir "$COPY_DIR" "$KEEP_COUNT"
      success "Copied backup to secondary location: ${COPY_DIR}/${archive_name}"
    else
      warn "Could not write to secondary copy directory '${COPY_DIR}' (is the drive mounted?)."
    fi
  fi

  # 8. Optional rclone offsite copy
  if [[ -n "$RCLONE_REMOTE" ]]; then
    if command -v rclone >/dev/null 2>&1; then
      info "Syncing backup to rclone remote '${RCLONE_REMOTE}'..."
      rclone copy "$final_archive" "$RCLONE_REMOTE"
      rclone copy "${final_archive}.sha256" "$RCLONE_REMOTE"
      success "Synced backup to ${RCLONE_REMOTE}"
    else
      warn "ATRIUM_BACKUP_RCLONE_REMOTE is set, but 'rclone' is not installed."
    fi
  fi

  if [[ "$includes_env_bool" == "false" ]]; then
    info "Reminder: back up your .env (especially ATRIUM_JWT_SECRET) separately so stored AI API keys can decrypt on a new host."
  fi
}

main() {
  parse_args "$@"
  case "$ACTION" in
    backup)          run_backup ;;
    list)            list_backups ;;
    install-timer)   install_timer ;;
    uninstall-timer) uninstall_timer ;;
  esac
}

main "$@"

