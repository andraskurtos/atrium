#!/usr/bin/env bash
set -Eeuo pipefail

# Atrium Safe Updater — Backup -> Git Pull -> Rebuild & Restart -> Health Check
#
# Takes a verified backup before pulling updates and rebuilding Docker containers,
# so EF Core AutoMigrate never runs without a fresh pre-upgrade snapshot.
#
# Usage:
#   ./scripts/update.sh [options]
#   ./scripts/update.sh --no-pull

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

info()    { printf '%s[update]%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
success() { printf '%s[ok]%s     %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn()    { printf '%s[warn]%s   %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
error()   { printf '%s[error]%s  %s\n' "$C_RED" "$C_RESET" "$*" >&2; }

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

API_PORT="${API_PORT:-$(read_env_var API_PORT 5000)}"
DOMAIN="${ATRIUM_DOMAIN:-$(read_env_var ATRIUM_DOMAIN "")}"
SKIP_BACKUP=0
NO_PULL=0
ALLOW_DIRTY=0

usage() {
  cat <<'EOF'
Atrium Safe Updater — Backup, Pull, Rebuild & Verify

Usage:
  ./scripts/update.sh [options]

Options:
  --no-pull        Skip git pull (rebuild from current working tree)
  --skip-backup    Skip the pre-update backup (not recommended)
  --allow-dirty    Allow running even if the git working tree has uncommitted changes
  -h, --help       Show this help message
EOF
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --no-pull)
        NO_PULL=1
        shift
        ;;
      --skip-backup)
        SKIP_BACKUP=1
        shift
        ;;
      --allow-dirty)
        ALLOW_DIRTY=1
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

main() {
  parse_args "$@"

  if ! command -v docker >/dev/null 2>&1; then
    error "Docker is required to update Atrium."
    exit 1
  fi

  if [[ "$NO_PULL" -eq 0 && "$ALLOW_DIRTY" -eq 0 ]]; then
    if [[ -n "$(git -C "$REPO_ROOT" status --porcelain 2>/dev/null || true)" ]]; then
      error "Working tree has uncommitted changes. Commit/stash them, or pass --no-pull / --allow-dirty."
      exit 1
    fi
  fi

  # 1. Pre-update backup (if Postgres is running)
  if [[ "$SKIP_BACKUP" -eq 0 ]]; then
    local pg_running
    pg_running="$(docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null | grep -x 'postgres' || true)"
    if [[ -n "$pg_running" ]]; then
      info "Taking pre-update backup before pulling/migrating..."
      "${SCRIPT_DIR}/backup.sh"
    else
      warn "PostgreSQL container is not running; skipping pre-update backup."
    fi
  else
    warn "Skipping pre-update backup (--skip-backup)."
  fi

  # 2. Pull latest code
  if [[ "$NO_PULL" -eq 0 ]]; then
    info "Pulling latest changes (git pull --ff-only)..."
    git -C "$REPO_ROOT" pull --ff-only
    success "Repository updated to $(git -C "$REPO_ROOT" rev-parse --short HEAD)."
  else
    info "Skipping git pull (--no-pull)."
  fi

  # 3. Rebuild and restart containers
  local -a compose_args=(compose -f "$COMPOSE_FILE")
  local caddy_running
  caddy_running="$(docker compose -f "$COMPOSE_FILE" ps --status running --services 2>/dev/null | grep -x 'caddy' || true)"
  if [[ -n "$DOMAIN" || -n "$caddy_running" ]]; then
    compose_args+=(--profile https)
  fi

  info "Rebuilding and starting containers..."
  docker "${compose_args[@]}" up -d --build

  # 4. Verify health
  local health_url="http://127.0.0.1:${API_PORT}/api/health"
  info "Waiting for Atrium to report healthy at ${health_url}..."
  local attempt=1
  local ok=0
  while [[ "$attempt" -le 30 ]]; do
    if curl -fsS --max-time 3 "$health_url" 2>/dev/null | grep -q '"status":"ok"'; then
      ok=1
      break
    fi
    sleep 2
    attempt=$((attempt + 1))
  done

  if [[ "$ok" -ne 1 ]]; then
    error "Atrium did not report healthy within 60s. Inspect logs with: docker compose logs --tail=50 atrium"
    warn "If a migration failed, you can roll back data with: ./scripts/restore.sh --latest"
    exit 1
  fi

  success "Atrium updated and healthy (${health_url})!"
}

main "$@"

