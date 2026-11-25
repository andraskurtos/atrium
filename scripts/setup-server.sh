#!/usr/bin/env bash
set -Eeuo pipefail

# Atrium Backend Server — One-Click Setup Script
# Sets up Docker (if needed), clones/uses the Atrium repo, generates secure
# configuration (.env), builds and starts PostgreSQL + .NET API (+ optional
# Caddy automatic HTTPS), and verifies /api/health.
#
# Usage:
#   ./scripts/setup-server.sh [options]
#   curl -fsSL https://raw.githubusercontent.com/andraskurtos/atrium/main/scripts/setup-server.sh | bash
#   curl -fsSL ... | bash -s -- --domain api.example.com --cors-origins https://andraskurtos.github.io -y

DEFAULT_REPO_URL="https://github.com/andraskurtos/atrium.git"
DEFAULT_BRANCH="main"
DEFAULT_API_PORT="5000"
DEFAULT_POSTGRES_PORT="5433"

REPO_URL="${ATRIUM_REPO_URL:-$DEFAULT_REPO_URL}"
BRANCH="${ATRIUM_BRANCH:-$DEFAULT_BRANCH}"
INSTALL_DIR="${ATRIUM_INSTALL_DIR:-}"
API_PORT="${ATRIUM_API_PORT:-}"
API_BIND_ADDRESS="${ATRIUM_API_BIND_ADDRESS:-}"
CORS_ORIGINS="${ATRIUM_CORS_ORIGINS:-}"
DOMAIN="${ATRIUM_DOMAIN:-}"
NON_INTERACTIVE=0
SKIP_DOCKER_INSTALL=0
SKIP_GIT_PULL=0

CLI_API_PORT_SET=0
CLI_API_BIND_SET=0
CLI_CORS_SET=0
CLI_DOMAIN_SET=0

# Terminal colors (disabled when stdout is not a TTY)
if [[ -t 1 ]]; then
  C_RESET=$'\033[0m'
  C_BOLD=$'\033[1m'
  C_DIM=$'\033[2m'
  C_CYAN=$'\033[36m'
  C_GREEN=$'\033[32m'
  C_YELLOW=$'\033[33m'
  C_RED=$'\033[31m'
else
  C_RESET='' C_BOLD='' C_DIM='' C_CYAN='' C_GREEN='' C_YELLOW='' C_RED=''
fi

info()    { printf '%s[atrium]%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
success() { printf '%s[ok]%s     %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn()    { printf '%s[warn]%s   %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
error()   { printf '%s[error]%s  %s\n' "$C_RED" "$C_RESET" "$*" >&2; }
step()    { printf '\n%s==> %s%s\n' "$C_BOLD" "$*" "$C_RESET"; }

trap 'error "Setup failed at line ${LINENO}. See output above for details."' ERR

usage() {
  cat <<'EOF'
Atrium Backend Server — One-Click Setup

Usage:
  setup-server.sh [options]

Options:
  --dir <path>             Install/project directory (default: current repo or ~/atrium)
  --repo <url>             Git repository URL (default: https://github.com/andraskurtos/atrium.git)
  --branch <name>          Git branch to clone (default: main)
  --domain <hostname>      Enable automatic HTTPS via Caddy for this domain (e.g. api.example.com)
  --cors-origins <list>    Comma-separated allowed frontend origins (e.g. https://user.github.io)
  --port <port>            Host port for the API container (default: 5000)
  --bind <ip>              Host bind address for API port (default: 127.0.0.1 with --domain, else 0.0.0.0)
  -y, --non-interactive    Run without prompts using CLI flags / defaults
  --skip-docker-install    Do not attempt to install Docker automatically
  --skip-git-pull          Do not pull updates when running in an existing repo
  -h, --help               Show this help message

Examples:
  # Interactive setup
  ./scripts/setup-server.sh

  # Automated setup with automatic HTTPS (Let's Encrypt via Caddy)
  ./scripts/setup-server.sh -y \
    --domain api.example.com \
    --cors-origins https://andraskurtos.github.io

  # Automated setup for LAN / Tailscale / external reverse proxy (HTTP on port 5000)
  ./scripts/setup-server.sh -y \
    --port 5000 \
    --cors-origins https://andraskurtos.github.io
EOF
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --dir)
        INSTALL_DIR="$2"
        shift 2
        ;;
      --repo)
        REPO_URL="$2"
        shift 2
        ;;
      --branch)
        BRANCH="$2"
        shift 2
        ;;
      --domain)
        DOMAIN="$2"
        CLI_DOMAIN_SET=1
        shift 2
        ;;
      --cors-origins)
        CORS_ORIGINS="$2"
        CLI_CORS_SET=1
        shift 2
        ;;
      --port)
        API_PORT="$2"
        CLI_API_PORT_SET=1
        shift 2
        ;;
      --bind)
        API_BIND_ADDRESS="$2"
        CLI_API_BIND_SET=1
        shift 2
        ;;
      -y|--non-interactive)
        NON_INTERACTIVE=1
        shift
        ;;
      --skip-docker-install)
        SKIP_DOCKER_INSTALL=1
        shift
        ;;
      --skip-git-pull)
        SKIP_GIT_PULL=1
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        error "Unknown option: $1"
        usage >&2
        exit 1
        ;;
    esac
  done
}

can_prompt() {
  [[ "$NON_INTERACTIVE" -eq 0 ]] && { [[ -t 0 ]] || [[ -r /dev/tty ]]; }
}

prompt_value() {
  local prompt_text="$1"
  local default_val="${2:-}"
  local reply=""

  if ! can_prompt; then
    printf '%s' "$default_val"
    return 0
  fi

  if [[ -n "$default_val" ]]; then
    printf '%s [%s]: ' "$prompt_text" "$default_val" >/dev/tty
  else
    printf '%s: ' "$prompt_text" >/dev/tty
  fi

  if ! IFS= read -r reply </dev/tty; then
    reply=""
  fi

  if [[ -z "$reply" ]]; then
    printf '%s' "$default_val"
  else
    printf '%s' "$reply"
  fi
}

prompt_confirm() {
  local prompt_text="$1"
  local default_yes="${2:-y}"
  local hint="Y/n"
  [[ "$default_yes" != "y" ]] && hint="y/N"

  if ! can_prompt; then
    [[ "$default_yes" == "y" ]]
    return
  fi

  local reply=""
  printf '%s [%s]: ' "$prompt_text" "$hint" >/dev/tty
  if ! IFS= read -r reply </dev/tty; then
    reply=""
  fi
  reply="${reply,,}"

  if [[ -z "$reply" ]]; then
    [[ "$default_yes" == "y" ]]
  elif [[ "$reply" == "y" || "$reply" == "yes" ]]; then
    return 0
  else
    return 1
  fi
}

run_as_root() {
  if [[ "$(id -u)" -eq 0 ]]; then
    "$@"
  elif command -v sudo >/dev/null 2>&1; then
    sudo "$@"
  else
    error "Root privileges or 'sudo' are required to run: $*"
    exit 1
  fi
}

install_package_if_missing() {
  local bin_name="$1"
  local pkg_name="${2:-$1}"
  if command -v "$bin_name" >/dev/null 2>&1; then
    return 0
  fi

  if [[ "$(uname -s)" != "Linux" ]]; then
    error "Required command '$bin_name' is not installed. Please install it and re-run."
    exit 1
  fi

  info "Installing missing prerequisite: $pkg_name..."
  if command -v apt-get >/dev/null 2>&1; then
    run_as_root apt-get update -qq
    run_as_root apt-get install -y -qq "$pkg_name"
  elif command -v dnf >/dev/null 2>&1; then
    run_as_root dnf install -y -q "$pkg_name"
  elif command -v yum >/dev/null 2>&1; then
    run_as_root yum install -y -q "$pkg_name"
  elif command -v apk >/dev/null 2>&1; then
    run_as_root apk add --no-cache "$pkg_name"
  else
    error "Could not detect a supported package manager to install '$pkg_name'. Please install it manually."
    exit 1
  fi
}

ensure_docker() {
  step "Checking Docker and Docker Compose"

  local need_install=0
  if ! command -v docker >/dev/null 2>&1; then
    need_install=1
  fi

  if [[ "$need_install" -eq 1 ]]; then
    if [[ "$SKIP_DOCKER_INSTALL" -eq 1 ]]; then
      error "Docker is not installed and --skip-docker-install was set."
      exit 1
    fi

    if [[ "$(uname -s)" != "Linux" ]]; then
      error "Docker is not installed. Please install Docker Desktop (https://www.docker.com/products/docker-desktop/) and re-run."
      exit 1
    fi

    if prompt_confirm "Docker is not installed. Install Docker Engine + Compose plugin now?" "y"; then
      install_package_if_missing curl curl
      info "Installing Docker using the official get.docker.com script..."
      curl -fsSL https://get.docker.com | run_as_root sh
      if command -v systemctl >/dev/null 2>&1; then
        run_as_root systemctl enable --now docker || true
      fi
      if [[ "$(id -u)" -ne 0 ]] && [[ -n "${USER:-}" ]]; then
        run_as_root usermod -aG docker "$USER" || true
        warn "Added '$USER' to the 'docker' group. Log out and back in for rootless docker commands."
      fi
    else
      error "Docker is required to host the Atrium backend server."
      exit 1
    fi
  fi

  # Determine whether docker requires sudo right now
  DOCKER_CMD=(docker)
  if ! docker info >/dev/null 2>&1; then
    if command -v sudo >/dev/null 2>&1 && sudo docker info >/dev/null 2>&1; then
      DOCKER_CMD=(sudo docker)
    elif [[ "$(uname -s)" == "Linux" ]] && command -v systemctl >/dev/null 2>&1; then
      info "Starting Docker daemon..."
      run_as_root systemctl start docker
      if docker info >/dev/null 2>&1; then
        DOCKER_CMD=(docker)
      elif sudo docker info >/dev/null 2>&1; then
        DOCKER_CMD=(sudo docker)
      else
        error "Docker daemon is not reachable. Check 'systemctl status docker'."
        exit 1
      fi
    else
      error "Docker daemon is not running. Start Docker and re-run this script."
      exit 1
    fi
  fi

  if ! "${DOCKER_CMD[@]}" compose version >/dev/null 2>&1; then
    error "Docker Compose v2 plugin ('docker compose') is required. Install 'docker-compose-plugin' and re-run."
    exit 1
  fi

  local docker_ver compose_ver
  docker_ver="$("${DOCKER_CMD[@]}" --version)"
  compose_ver="$("${DOCKER_CMD[@]}" compose version --short 2>/dev/null || "${DOCKER_CMD[@]}" compose version)"
  success "Found ${docker_ver} (Compose ${compose_ver})"
}

is_atrium_repo() {
  local dir="$1"
  [[ -f "${dir}/docker-compose.yml" && -f "${dir}/api/Dockerfile" ]]
}

resolve_project_dir() {
  step "Preparing Atrium repository"

  if [[ -n "$INSTALL_DIR" ]]; then
    mkdir -p "$(dirname "$INSTALL_DIR")"
  elif is_atrium_repo "$(pwd)"; then
    INSTALL_DIR="$(pwd)"
  elif [[ -n "${BASH_SOURCE[0]:-}" ]] && [[ -f "${BASH_SOURCE[0]}" ]]; then
    local script_dir repo_candidate
    script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    repo_candidate="$(cd "${script_dir}/.." && pwd)"
    if is_atrium_repo "$repo_candidate"; then
      INSTALL_DIR="$repo_candidate"
    fi
  fi

  if [[ -z "$INSTALL_DIR" ]]; then
    INSTALL_DIR="${HOME}/atrium"
  fi

  if is_atrium_repo "$INSTALL_DIR"; then
    success "Using existing Atrium directory: ${INSTALL_DIR}"
    if [[ "$SKIP_GIT_PULL" -eq 0 ]] && [[ -d "${INSTALL_DIR}/.git" ]] && command -v git >/dev/null 2>&1; then
      if [[ -z "$(git -C "$INSTALL_DIR" status --porcelain 2>/dev/null)" ]]; then
        info "Repository is clean. Skipping automatic pull to preserve your current branch."
      fi
    fi
  else
    install_package_if_missing git git
    if [[ -d "$INSTALL_DIR" ]] && [[ -n "$(ls -A "$INSTALL_DIR" 2>/dev/null)" ]]; then
      error "Directory '${INSTALL_DIR}' already exists and is not an Atrium repository. Specify another path with --dir."
      exit 1
    fi
    info "Cloning ${REPO_URL} (branch: ${BRANCH}) into ${INSTALL_DIR}..."
    git clone --branch "$BRANCH" --single-branch "$REPO_URL" "$INSTALL_DIR"
    success "Cloned repository into ${INSTALL_DIR}"
  fi
}

generate_password() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32
  else
    LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 32
  fi
}

read_env_key() {
  local file="$1"
  local key="$2"
  [[ -f "$file" ]] || return 0
  local line
  line="$(grep -E "^${key}=" "$file" | tail -n 1 || true)"
  if [[ -n "$line" ]]; then
    local val="${line#*=}"
    val="${val%\"}"
    val="${val#\"}"
    val="${val%\'}"
    val="${val#\'}"
    printf '%s' "$val"
  fi
}

guess_github_pages_origin() {
  local remote_url="$REPO_URL"
  if [[ -d "${INSTALL_DIR}/.git" ]] && command -v git >/dev/null 2>&1; then
    local origin
    origin="$(git -C "$INSTALL_DIR" remote get-url origin 2>/dev/null || true)"
    [[ -n "$origin" ]] && remote_url="$origin"
  fi

  if [[ "$remote_url" =~ github\.com[:/]([^/]+)/ ]]; then
    local owner="${BASH_REMATCH[1],,}"
    printf 'https://%s.github.io' "$owner"
  fi
}

configure_env() {
  step "Configuring server environment (.env)"

  local env_file="${INSTALL_DIR}/.env"
  local existing_db existing_user existing_pass existing_pg_port existing_api_port existing_bind existing_cors existing_domain

  existing_db="$(read_env_key "$env_file" "POSTGRES_DB")"
  existing_user="$(read_env_key "$env_file" "POSTGRES_USER")"
  existing_pass="$(read_env_key "$env_file" "POSTGRES_PASSWORD")"
  existing_pg_port="$(read_env_key "$env_file" "POSTGRES_PORT")"
  existing_api_port="$(read_env_key "$env_file" "API_PORT")"
  existing_bind="$(read_env_key "$env_file" "API_BIND_ADDRESS")"
  existing_cors="$(read_env_key "$env_file" "CORS_ORIGINS")"
  existing_domain="$(read_env_key "$env_file" "ATRIUM_DOMAIN")"

  POSTGRES_DB="${existing_db:-atrium}"
  POSTGRES_USER="${existing_user:-atrium}"
  POSTGRES_PORT="${existing_pg_port:-$DEFAULT_POSTGRES_PORT}"

  if [[ -n "$existing_pass" && "$existing_pass" != "atrium_dev_password" ]]; then
    POSTGRES_PASSWORD="$existing_pass"
    info "Preserving existing PostgreSQL password from .env."
  elif [[ -n "$existing_pass" ]]; then
    # Existing .env has the default dev password; preserve if pgdata volume already exists
    if "${DOCKER_CMD[@]}" volume ls --format '{{.Name}}' | grep -q 'atrium_pgdata$'; then
      POSTGRES_PASSWORD="$existing_pass"
      warn "Existing PostgreSQL data volume detected; keeping current POSTGRES_PASSWORD to avoid auth failure."
    else
      POSTGRES_PASSWORD="$(generate_password)"
      success "Generated strong 32-character PostgreSQL password."
    fi
  else
    POSTGRES_PASSWORD="$(generate_password)"
    success "Generated strong 32-character PostgreSQL password."
  fi

  local suggested_cors="${existing_cors:-$(guess_github_pages_origin)}"

  if [[ "$CLI_CORS_SET" -eq 0 ]]; then
    if [[ -n "$CORS_ORIGINS" ]]; then
      : # Provided via ATRIUM_CORS_ORIGINS env var
    elif can_prompt; then
      printf '\n'
      info "CORS allows your GitHub Pages frontend to talk to this API server."
      info "Use scheme + host only (no path, no trailing slash), e.g. https://username.github.io"
      CORS_ORIGINS="$(prompt_value "Allowed frontend origin(s) (comma-separated, leave blank for same-origin)" "$suggested_cors")"
    else
      CORS_ORIGINS="$suggested_cors"
    fi
  fi

  if [[ "$CLI_DOMAIN_SET" -eq 0 ]]; then
    if [[ -n "$DOMAIN" ]]; then
      : # Provided via ATRIUM_DOMAIN env var
    elif can_prompt; then
      printf '\n'
      info "Browsers require HTTPS for APIs called from GitHub Pages."
      info "If you have a domain pointing to this server's IP (ports 80 & 443 open), enter it for automatic Let's Encrypt HTTPS."
      info "Leave blank if you will use Tailscale, Cloudflare Tunnel, an existing reverse proxy, or local HTTP."
      DOMAIN="$(prompt_value "Public API domain for automatic HTTPS (optional)" "$existing_domain")"
    else
      DOMAIN="$existing_domain"
    fi
  fi

  # Strip protocol or trailing slashes if user accidentally typed https://api.example.com/
  DOMAIN="${DOMAIN#https://}"
  DOMAIN="${DOMAIN#http://}"
  DOMAIN="${DOMAIN%%/*}"

  if [[ "$CLI_API_PORT_SET" -eq 0 ]]; then
    local default_port="${existing_api_port:-$DEFAULT_API_PORT}"
    if [[ -n "$API_PORT" ]]; then
      : # Provided via ATRIUM_API_PORT env var
    elif [[ -n "$DOMAIN" ]]; then
      API_PORT="$default_port"
    elif can_prompt; then
      API_PORT="$(prompt_value "API host port" "$default_port")"
    else
      API_PORT="$default_port"
    fi
  fi

  if [[ "$CLI_API_BIND_SET" -eq 0 && -z "$API_BIND_ADDRESS" ]]; then
    if [[ -n "$DOMAIN" ]]; then
      # Keep raw HTTP port bound to loopback when Caddy terminates HTTPS
      API_BIND_ADDRESS="127.0.0.1"
    else
      API_BIND_ADDRESS="${existing_bind:-0.0.0.0}"
    fi
  fi

  if [[ -f "$env_file" ]]; then
    cp "$env_file" "${env_file}.bak"
    chmod 600 "${env_file}.bak"
  fi

  local tmp_env="${env_file}.tmp.$$"
  umask 077
  cat >"$tmp_env" <<EOF
# Generated by Atrium setup-server.sh on $(date -u +"%Y-%m-%dT%H:%M:%SZ")

# PostgreSQL
POSTGRES_DB=${POSTGRES_DB}
POSTGRES_USER=${POSTGRES_USER}
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
POSTGRES_PORT=${POSTGRES_PORT}

# Atrium API
API_PORT=${API_PORT}
API_BIND_ADDRESS=${API_BIND_ADDRESS}

# Allowed frontend origins for CORS (comma-separated)
CORS_ORIGINS=${CORS_ORIGINS}

# Optional domain for automatic HTTPS via Caddy (--profile https)
ATRIUM_DOMAIN=${DOMAIN}
EOF
  mv "$tmp_env" "$env_file"
  chmod 600 "$env_file"
  success "Wrote ${env_file} (permissions 600)"
}

start_containers() {
  step "Building and starting Atrium containers"

  local compose_args=(compose -f "${INSTALL_DIR}/docker-compose.yml")
  if [[ -n "$DOMAIN" ]]; then
    compose_args+=(--profile https)
    info "Automatic HTTPS enabled via Caddy for https://${DOMAIN}"
  fi

  "${DOCKER_CMD[@]}" "${compose_args[@]}" up -d --build
  success "Containers started."
}

verify_health() {
  step "Verifying API & PostgreSQL health"

  install_package_if_missing curl curl

  local health_url="http://127.0.0.1:${API_PORT}/api/health"
  local max_attempts=30
  local attempt=1
  local response=""

  while [[ "$attempt" -le "$max_attempts" ]]; do
    if response="$(curl -fsS --max-time 3 "$health_url" 2>/dev/null)"; then
      if [[ "$response" == *'"status":"ok"'* ]]; then
        success "Health check passed: ${response}"
        return 0
      fi
    fi
    sleep 2
    attempt=$((attempt + 1))
  done

  error "API did not report healthy at ${health_url} within $((max_attempts * 2))s."
  warn "Container status:"
  "${DOCKER_CMD[@]}" compose -f "${INSTALL_DIR}/docker-compose.yml" ps || true
  warn "Recent API & Postgres logs:"
  "${DOCKER_CMD[@]}" compose -f "${INSTALL_DIR}/docker-compose.yml" logs --tail=40 api postgres || true
  exit 1
}

detect_host_ip() {
  local ip=""
  if command -v hostname >/dev/null 2>&1; then
    ip="$(hostname -I 2>/dev/null | awk '{print $1}' || true)"
  fi
  printf '%s' "${ip:-<server-ip>}"
}

print_summary() {
  local api_url compose_prefix
  if [[ -n "$DOMAIN" ]]; then
    api_url="https://${DOMAIN}"
    compose_prefix="docker compose --profile https"
  else
    api_url="http://$(detect_host_ip):${API_PORT}"
    compose_prefix="docker compose"
  fi

  printf '\n%s============================================================%s\n' "$C_GREEN" "$C_RESET"
  printf '%s  Atrium backend server is running!%s\n' "$C_BOLD" "$C_RESET"
  printf '%s============================================================%s\n\n' "$C_GREEN" "$C_RESET"

  printf '  %sInstall directory:%s %s\n' "$C_BOLD" "$C_RESET" "$INSTALL_DIR"
  printf '  %sAPI URL:%s           %s\n' "$C_BOLD" "$C_RESET" "$api_url"
  printf '  %sHealth check:%s      %s/api/health\n' "$C_BOLD" "$C_RESET" "$api_url"
  if [[ -n "$CORS_ORIGINS" ]]; then
    printf '  %sAllowed CORS:%s      %s\n' "$C_BOLD" "$C_RESET" "$CORS_ORIGINS"
  else
    printf '  %sAllowed CORS:%s      (none — same-origin only)\n' "$C_BOLD" "$C_RESET"
  fi

  printf '\n%sConnect your GitHub Pages frontend:%s\n' "$C_BOLD" "$C_RESET"
  printf '  Option 1 (Build-time): Set GitHub Actions repository variable:\n'
  printf '           %sVITE_API_BASE_URL=%s%s\n' "$C_CYAN" "$api_url" "$C_RESET"
  printf '  Option 2 (Runtime):    Open your GitHub Pages site, click "Configure",\n'
  printf '           and enter %s%s%s\n' "$C_CYAN" "$api_url" "$C_RESET"

  if [[ -z "$DOMAIN" ]]; then
    printf '\n  %sNote:%s GitHub Pages is served over HTTPS. To avoid browser Mixed Content\n' "$C_YELLOW" "$C_RESET"
    printf '  blocking, front this API with HTTPS (re-run with --domain <host>, or use\n'
    printf '  Cloudflare Tunnel / Tailscale Serve / Nginx). See docs/deployment.md.\n'
  fi

  printf '\n%sUseful commands (run inside %s):%s\n' "$C_BOLD" "$INSTALL_DIR" "$C_RESET"
  printf '  View logs:    %s logs -f\n' "$compose_prefix"
  printf '  Restart:      %s restart\n' "$compose_prefix"
  printf '  Update:       git pull && %s up -d --build\n' "$compose_prefix"
  printf '  Stop:         %s down\n' "$compose_prefix"
  printf '  DB backup:    docker compose exec postgres pg_dump -U %s %s > backup.sql\n\n' "$POSTGRES_USER" "$POSTGRES_DB"
}

main() {
  parse_args "$@"
  printf '%sAtrium Backend Server — One-Click Setup%s\n' "$C_BOLD" "$C_RESET"
  ensure_docker
  resolve_project_dir
  configure_env
  start_containers
  verify_health
  print_summary
}

main "$@"
