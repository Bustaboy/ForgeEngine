#!/usr/bin/env bash
set -Eeuo pipefail

# ------------------------------------------------------------
# ForgeEngine Alpha Setup (Linux/macOS)
# One-command, idempotent environment bootstrap for testers.
# ------------------------------------------------------------

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENV_DIR="$REPO_ROOT/.venv"
ORCH_DIR="$REPO_ROOT/ai-orchestration/python"

# ANSI colors for beautiful progress output.
C_RESET="\033[0m"
C_BLUE="\033[1;34m"
C_GREEN="\033[1;32m"
C_YELLOW="\033[1;33m"
C_RED="\033[1;31m"

step()    { printf "%b\n" "${C_BLUE}▶ $*${C_RESET}"; }
success() { printf "%b\n" "${C_GREEN}✔ $*${C_RESET}"; }
warn()    { printf "%b\n" "${C_YELLOW}⚠ $*${C_RESET}"; }
fail()    { printf "%b\n" "${C_RED}✖ $*${C_RESET}"; }

on_error() {
  fail "Setup failed at line $1. Re-run this script after fixing the above issue."
}
trap 'on_error $LINENO' ERR

require_cmd() {
  local cmd="$1"
  local hint="$2"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    fail "Missing required command: $cmd"
    [[ -n "$hint" ]] && printf "%b\n" "${C_YELLOW}$hint${C_RESET}"
    exit 1
  fi
}

run_apt_install() {
  local packages=("$@")
  if [[ "${EUID}" -ne 0 ]]; then
    sudo apt-get install -y "${packages[@]}"
  else
    apt-get install -y "${packages[@]}"
  fi
}

run_apt_update() {
  if [[ "${EUID}" -ne 0 ]]; then
    sudo apt-get update
  else
    apt-get update
  fi
}

setup_apt_dotnet_repo_if_needed() {
  if command -v dotnet >/dev/null 2>&1; then
    return
  fi

  step "Adding Microsoft package repository for .NET 8 SDK"
  local os_release=""
  os_release="$(. /etc/os-release && echo "$ID/$VERSION_ID")"

  case "$os_release" in
    ubuntu/*)
      if [[ "${EUID}" -ne 0 ]]; then
        wget -q https://packages.microsoft.com/config/ubuntu/"${VERSION_ID}"/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
        sudo dpkg -i /tmp/packages-microsoft-prod.deb
      else
        wget -q https://packages.microsoft.com/config/ubuntu/"${VERSION_ID}"/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
        dpkg -i /tmp/packages-microsoft-prod.deb
      fi
      ;;
    debian/*)
      if [[ "${EUID}" -ne 0 ]]; then
        wget -q https://packages.microsoft.com/config/debian/"${VERSION_ID}"/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
        sudo dpkg -i /tmp/packages-microsoft-prod.deb
      else
        wget -q https://packages.microsoft.com/config/debian/"${VERSION_ID}"/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
        dpkg -i /tmp/packages-microsoft-prod.deb
      fi
      ;;
    *)
      warn "Unsupported apt distro for automatic .NET repo bootstrap. Will try apt package directly."
      ;;
  esac
}

setup_apt_lunarg_repo_if_needed() {
  if apt-cache policy vulkan-sdk 2>/dev/null | rg -q "Candidate:"; then
    if [[ "$(apt-cache policy vulkan-sdk | awk '/Candidate:/ {print $2}')" != "(none)" ]]; then
      return
    fi
  fi

  step "Adding LunarG Vulkan SDK apt repository"
  local codename
  codename="$(. /etc/os-release && echo "$VERSION_CODENAME")"

  require_cmd curl "Install curl and retry."
  require_cmd gpg "Install gnupg and retry."

  if [[ "${EUID}" -ne 0 ]]; then
    curl -fsSL https://packages.lunarg.com/lunarg-signing-key-pub.asc | gpg --dearmor | sudo tee /usr/share/keyrings/lunarg.gpg >/dev/null
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/lunarg.gpg] https://packages.lunarg.com/vulkan/lunarg-vulkan-${codename}.list" | sudo tee /etc/apt/sources.list.d/lunarg-vulkan.list >/dev/null
  else
    curl -fsSL https://packages.lunarg.com/lunarg-signing-key-pub.asc | gpg --dearmor > /usr/share/keyrings/lunarg.gpg
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/lunarg.gpg] https://packages.lunarg.com/vulkan/lunarg-vulkan-${codename}.list" > /etc/apt/sources.list.d/lunarg-vulkan.list
  fi

  # Fallback for LunarG repo format differences.
  if ! run_apt_update; then
    warn "Primary LunarG repo format failed, trying alternate list URL."
    if [[ "${EUID}" -ne 0 ]]; then
      echo "deb [arch=amd64 signed-by=/usr/share/keyrings/lunarg.gpg] https://packages.lunarg.com/vulkan/ ${codename} main" | sudo tee /etc/apt/sources.list.d/lunarg-vulkan.list >/dev/null
    else
      echo "deb [arch=amd64 signed-by=/usr/share/keyrings/lunarg.gpg] https://packages.lunarg.com/vulkan/ ${codename} main" > /etc/apt/sources.list.d/lunarg-vulkan.list
    fi
  fi
}

install_linux_deps() {
  step "Detected apt-based Linux (Ubuntu/Debian). Installing dependencies"
  run_apt_update
  run_apt_install ca-certificates curl wget gnupg software-properties-common apt-transport-https

  setup_apt_dotnet_repo_if_needed
  setup_apt_lunarg_repo_if_needed

  run_apt_update
  run_apt_install \
    build-essential \
    g++ \
    cmake \
    ninja-build \
    libvulkan-dev \
    vulkan-sdk \
    libglfw3-dev \
    libx11-dev \
    libxrandr-dev \
    libxinerama-dev \
    libxcursor-dev \
    libxi-dev \
    python3 \
    python3-venv \
    python3-pip \
    dotnet-sdk-8.0

  success "Linux dependencies installed."
}

install_macos_deps() {
  step "Detected macOS (Homebrew). Installing dependencies"
  require_cmd brew "Install Homebrew first: https://brew.sh"

  brew update
  brew install cmake ninja python dotnet molten-vk glfw

  if brew list --cask 2>/dev/null | rg -q '^vulkansdk$'; then
    success "Vulkan SDK cask already installed."
  else
    if brew info --cask vulkan-sdk >/dev/null 2>&1; then
      brew install --cask vulkan-sdk
      success "Installed Vulkan SDK cask."
    else
      warn "Homebrew Vulkan SDK cask unavailable. Installed molten-vk; for full LunarG SDK install from:"
      warn "https://vulkan.lunarg.com/sdk/home"
    fi
  fi

  success "macOS dependencies installed."
}

create_clean_venv_and_pth() {
  step "Creating clean Python virtual environment at ./.venv"
  rm -rf "$VENV_DIR"
  python3 -m venv "$VENV_DIR"

  # shellcheck disable=SC1091
  source "$VENV_DIR/bin/activate"

  step "Ensuring ai-orchestration/python is always importable via forge.pth"
  local site_packages
  site_packages="$(python -c 'import site; print(next(p for p in site.getsitepackages() if p.endswith("site-packages")))')"
  printf "%s\n" "$ORCH_DIR" > "$site_packages/forge.pth"

  success "Virtual environment and forge.pth configured."
}

run_bootstrap_and_models() {
  step "Running ForgeEngine bootstrap script"
  (cd "$REPO_ROOT" && ./scripts/bootstrap.sh)

  # shellcheck disable=SC1091
  source "$VENV_DIR/bin/activate"

  step "Preparing AI models"
  (cd "$REPO_ROOT" && python ai-orchestration/python/orchestrator.py --prepare-models)

  step "Running orchestrator benchmark"
  (cd "$REPO_ROOT" && python ai-orchestration/python/orchestrator.py --benchmark)

  success "Bootstrap + AI model setup completed."
}

main() {
  step "Starting ForgeEngine Alpha one-command setup"

  case "$(uname -s)" in
    Linux)
      require_cmd apt-get "This script supports apt-based Linux distros (Ubuntu/Debian)."
      install_linux_deps
      ;;
    Darwin)
      install_macos_deps
      ;;
    *)
      fail "Unsupported OS for setup.sh. Use scripts/Setup-Alpha.ps1 on Windows."
      exit 1
      ;;
  esac

  create_clean_venv_and_pth
  run_bootstrap_and_models

  success "All done! 🎉"
  printf "%b\n" "${C_GREEN}Next step:${C_RESET}"
  printf "  cd %q\n" "$REPO_ROOT"
  printf "  ./scripts/bootstrap.sh\n"
  printf "  # Or run editor directly:\n"
  printf "  dotnet run --project editor/csharp/GameForge.Editor.csproj\n"
}

main "$@"
