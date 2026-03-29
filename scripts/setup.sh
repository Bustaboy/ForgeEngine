#!/usr/bin/env bash
set -Eeuo pipefail

# ------------------------------------------------------------
# Soul Loom Setup (Linux/macOS)
# One-command, idempotent environment bootstrap for testers.
# ------------------------------------------------------------

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENV_DIR="$REPO_ROOT/.venv"
ORCH_DIR="$REPO_ROOT/ai-orchestration/python"
ORCH_SCRIPT="$ORCH_DIR/orchestrator.py"
REQ_FILE="$ORCH_DIR/requirements.txt"
FRESH=0
CURRENT_STEP="initializing"
SKIP_VULKAN_SDK=0

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
  fail "Setup failed at line $1 during: ${CURRENT_STEP}"
  printf "%b\n" "${C_YELLOW}  → Check the error output above for details on what went wrong.${C_RESET}"
  printf "%b\n" "${C_YELLOW}  → Fix the issue, then re-run: ./scripts/setup.sh${C_RESET}"
  printf "%b\n" "${C_YELLOW}  → To start completely fresh: ./scripts/setup.sh --fresh${C_RESET}"
}
trap 'on_error $LINENO' ERR

usage() {
  cat <<USAGE
Soul Loom setup (Linux/macOS)

Usage:
  ./scripts/setup.sh [--fresh]

Options:
  --fresh   Recreate .venv and re-run model preparation + benchmark.
  -h, --help  Show this help message.
USAGE
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --fresh)
        FRESH=1
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        fail "Unknown argument: $1"
        usage
        exit 1
        ;;
    esac
    shift
  done
}

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
  local distro_id=""
  local distro_version=""
  . /etc/os-release
  distro_id="$ID"
  distro_version="$VERSION_ID"

  local deb_url=""
  case "$distro_id" in
    ubuntu) deb_url="https://packages.microsoft.com/config/ubuntu/${distro_version}/packages-microsoft-prod.deb" ;;
    debian) deb_url="https://packages.microsoft.com/config/debian/${distro_version}/packages-microsoft-prod.deb" ;;
    *)
      warn "Unsupported distro '${distro_id}' for automatic .NET repo setup. Will attempt apt package directly."
      return
      ;;
  esac

  local deb_ok=1
  wget -q "$deb_url" -O /tmp/packages-microsoft-prod.deb || deb_ok=0

  if [[ "$deb_ok" -eq 0 ]]; then
    warn "Failed to download the Microsoft package installer (packages-microsoft-prod.deb)."
    warn "  URL tried: ${deb_url}"
    warn "  dotnet-sdk-8.0 will likely fail to install. Install .NET manually from:"
    warn "  https://learn.microsoft.com/en-us/dotnet/core/install/linux"
    return
  fi

  local dpkg_ok=1
  if [[ "${EUID}" -ne 0 ]]; then
    sudo dpkg -i /tmp/packages-microsoft-prod.deb || dpkg_ok=0
  else
    dpkg -i /tmp/packages-microsoft-prod.deb || dpkg_ok=0
  fi

  if [[ "$dpkg_ok" -eq 0 ]]; then
    warn "Microsoft package installer ran but reported an error."
    warn "  dotnet-sdk-8.0 may fail to install. If it does, install .NET manually from:"
    warn "  https://learn.microsoft.com/en-us/dotnet/core/install/linux"
  fi
}

setup_apt_lunarg_repo_if_needed() {
  local candidate
  candidate="$(apt-cache policy vulkan-sdk 2>/dev/null | awk '/Candidate:/ {print $2}')"
  if [[ -n "$candidate" && "$candidate" != "(none)" ]]; then
    return
  fi

  step "Adding LunarG Vulkan SDK apt repository"
  local codename=""
  . /etc/os-release
  codename="$VERSION_CODENAME"

  if ! command -v curl >/dev/null 2>&1; then
    warn "curl not found; cannot add LunarG repository. Vulkan SDK will be skipped."
    warn "Install curl manually (sudo apt-get install curl) and re-run to include Vulkan SDK."
    SKIP_VULKAN_SDK=1
    return
  fi
  if ! command -v gpg >/dev/null 2>&1; then
    warn "gpg not found; cannot add LunarG repository. Vulkan SDK will be skipped."
    warn "Install gnupg manually (sudo apt-get install gnupg) and re-run to include Vulkan SDK."
    SKIP_VULKAN_SDK=1
    return
  fi

  local keyring_path="/usr/share/keyrings/lunarg.gpg"
  local list_path="/etc/apt/sources.list.d/lunarg-vulkan.list"

  local key_ok=1
  if [[ "${EUID}" -ne 0 ]]; then
    curl -fsSL https://packages.lunarg.com/lunarg-signing-key-pub.asc | gpg --dearmor | sudo tee "$keyring_path" >/dev/null \
      && curl -fsSL "https://packages.lunarg.com/vulkan/lunarg-vulkan-${codename}.list" | sudo tee "$list_path" >/dev/null \
      || key_ok=0
  else
    curl -fsSL https://packages.lunarg.com/lunarg-signing-key-pub.asc | gpg --dearmor > "$keyring_path" \
      && curl -fsSL "https://packages.lunarg.com/vulkan/lunarg-vulkan-${codename}.list" > "$list_path" \
      || key_ok=0
  fi

  if [[ "$key_ok" -eq 0 ]]; then
    warn "Failed to add LunarG Vulkan SDK repository (network error or unsupported distro codename: ${codename})."
    warn "Vulkan SDK will be skipped. Install it manually later from: https://vulkan.lunarg.com/sdk/home"
    SKIP_VULKAN_SDK=1
    return
  fi

  if [[ "${EUID}" -ne 0 ]]; then
    sudo sed -i "s|^deb |deb [signed-by=${keyring_path}] |" "$list_path"
  else
    sed -i "s|^deb |deb [signed-by=${keyring_path}] |" "$list_path"
  fi
}

verify_linux_tools() {
  local missing=()
  local hints=()

  command -v g++     >/dev/null 2>&1 || { missing+=(g++);           hints+=("sudo apt-get install g++"); }
  command -v cmake   >/dev/null 2>&1 || { missing+=(cmake);         hints+=("sudo apt-get install cmake"); }
  command -v ninja   >/dev/null 2>&1 || { missing+=(ninja-build);   hints+=("sudo apt-get install ninja-build"); }
  command -v python3 >/dev/null 2>&1 || { missing+=(python3);       hints+=("sudo apt-get install python3 python3-venv python3-pip"); }
  command -v dotnet  >/dev/null 2>&1 || { missing+=("dotnet-sdk-8.0"); hints+=("See https://learn.microsoft.com/en-us/dotnet/core/install/linux"); }

  if [[ ${#missing[@]} -gt 0 ]]; then
    fail "The following required tools are missing after installation:"
    local i
    for (( i=0; i<${#missing[@]}; i++ )); do
      printf "%b\n" "${C_RED}    - ${missing[$i]}${C_RESET}"
      printf "%b\n" "${C_YELLOW}      Fix: ${hints[$i]}${C_RESET}"
    done
    exit 1
  fi
  success "All required tools verified."
}

install_linux_deps() {
  step "Detected apt-based Linux (Ubuntu/Debian). Installing dependencies"

  CURRENT_STEP="updating apt package lists"
  run_apt_update

  CURRENT_STEP="installing prerequisite tools (curl, wget, gnupg)"
  run_apt_install ca-certificates curl wget gnupg software-properties-common apt-transport-https

  CURRENT_STEP="configuring .NET package repository"
  setup_apt_dotnet_repo_if_needed

  CURRENT_STEP="configuring LunarG Vulkan SDK repository"
  setup_apt_lunarg_repo_if_needed

  CURRENT_STEP="updating apt package lists (after repo additions)"
  run_apt_update

  CURRENT_STEP="installing build tools (build-essential, g++, cmake, ninja-build)"
  if ! run_apt_install build-essential g++ cmake ninja-build; then
    fail "Failed to install C++ build tools."
    printf "%b\n" "${C_YELLOW}  This is often caused by a broken apt state or a failed dependency of g++.${C_RESET}"
    printf "%b\n" "${C_YELLOW}  Try running these commands to repair, then re-run setup:${C_RESET}"
    printf "%b\n" "${C_YELLOW}    sudo apt-get install -f${C_RESET}"
    printf "%b\n" "${C_YELLOW}    sudo dpkg --configure -a${C_RESET}"
    printf "%b\n" "${C_YELLOW}    ./scripts/setup.sh${C_RESET}"
    exit 1
  fi

  CURRENT_STEP="installing graphics and windowing libraries"
  local vulkan_pkgs=(libvulkan-dev)
  [[ "$SKIP_VULKAN_SDK" -eq 0 ]] && vulkan_pkgs+=(vulkan-sdk)
  run_apt_install \
    "${vulkan_pkgs[@]}" \
    libglfw3-dev \
    libx11-dev \
    libxrandr-dev \
    libxinerama-dev \
    libxcursor-dev \
    libxi-dev

  CURRENT_STEP="installing Python tools"
  run_apt_install python3 python3-venv python3-pip

  CURRENT_STEP="installing .NET SDK 8.0"
  run_apt_install dotnet-sdk-8.0

  CURRENT_STEP="verifying installed tools"
  verify_linux_tools

  success "Linux dependencies installed."
}

install_macos_deps() {
  step "Detected macOS (Homebrew). Installing dependencies"
  require_cmd brew "Install Homebrew first: https://brew.sh"

  brew update
  brew install cmake ninja python dotnet molten-vk glfw

  if brew list --cask 2>/dev/null | grep -q '^vulkansdk$'; then
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

activate_venv() {
  # shellcheck disable=SC1091
  source "$VENV_DIR/bin/activate"
}

create_or_reuse_venv() {
  local should_prepare_models=0

  if [[ -d "$VENV_DIR" && "$FRESH" -eq 0 ]]; then
    step "Reusing existing virtual environment at ./.venv"
    activate_venv
  else
    if [[ -d "$VENV_DIR" && "$FRESH" -eq 1 ]]; then
      step "--fresh provided: removing existing ./.venv"
      rm -rf "$VENV_DIR"
    fi

    step "Creating Python virtual environment at ./.venv"
    python3 -m venv "$VENV_DIR"
    activate_venv
    should_prepare_models=1
  fi

  step "Ensuring ai-orchestration/python is always importable via forge.pth"
  local site_packages
  site_packages="$(python -c 'import site; print(next(p for p in site.getsitepackages() if p.endswith("site-packages")))')"
  printf "%s\n" "$ORCH_DIR" > "$site_packages/forge.pth"

  if [[ -f "$REQ_FILE" ]]; then
    step "Installing Python dependencies from $REQ_FILE"
    python -m pip install --upgrade pip
    python -m pip install -r "$REQ_FILE"
  else
    warn "No requirements.txt found at $REQ_FILE (skipping pip install)."
  fi

  success "Virtual environment and forge.pth configured."
  echo "$should_prepare_models"
}

run_bootstrap() {
  CURRENT_STEP="running Soul Loom bootstrap (compiling C++ runtime, verifying project structure)"
  step "Running Soul Loom bootstrap script"
  "$REPO_ROOT/scripts/bootstrap.sh"
  success "Bootstrap completed."
}

run_orchestrator_step() {
  local arg="$1"
  local label="$2"

  step "$label"
  if ! python "$ORCH_SCRIPT" "$arg"; then
    fail "Orchestrator command failed: python $ORCH_SCRIPT $arg"
    exit 1
  fi
}

run_models_if_needed() {
  local should_prepare_models="$1"
  if [[ "$should_prepare_models" -eq 1 ]]; then
    run_orchestrator_step "--prepare-models" "Preparing AI models"
    run_orchestrator_step "--benchmark" "Running orchestrator benchmark"
    success "AI model preparation and benchmark completed."
  else
    warn "Skipping model prep/benchmark because existing .venv was reused. Use --fresh to force rerun."
  fi
}

main() {
  parse_args "$@"
  step "Starting Soul Loom one-command setup"

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

  run_bootstrap
  CURRENT_STEP="setting up Python virtual environment"
  local should_prepare
  should_prepare="$(create_or_reuse_venv)"
  CURRENT_STEP="preparing AI models"
  run_models_if_needed "$should_prepare"

  success "All done! 🎉"
  printf "%b\n" "${C_GREEN}Next step:${C_RESET}"
  printf "  cd %q\n" "$REPO_ROOT"
  printf "  ./scripts/bootstrap.sh\n"
  printf "  # Or run editor directly:\n"
  printf "  pwsh -f scripts/bootstrap.ps1\n"
}

main "$@"
