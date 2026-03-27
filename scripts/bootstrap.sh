#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="$REPO_ROOT/build"
RUNTIME_BIN="$BUILD_DIR/bin/forge_runtime"
EDITOR_PROJECT="$REPO_ROOT/editor/csharp/GameForge.Editor.csproj"
RUNTIME_ONLY="${1:-}"
LAUNCHER_SMOKE=0
if [[ "${1:-}" == "--launcher-smoke" ]]; then
  LAUNCHER_SMOKE=1
fi
JSON_HEADER="$REPO_ROOT/runtime/cpp/external/nlohmann/json.hpp"
JSON_URL="https://raw.githubusercontent.com/nlohmann/json/v3.11.3/single_include/nlohmann/json.hpp"

required_paths=(
  "$REPO_ROOT/app"
  "$REPO_ROOT/editor/csharp"
  "$REPO_ROOT/runtime/cpp"
  "$REPO_ROOT/ai-orchestration/python"
  "$REPO_ROOT/docs"
  "$REPO_ROOT/scripts"
)

echo "GameForge V1 bootstrap (Ubuntu/Linux)"
echo "Mode: local-first, single-player, no-code-first"

configure_runtime_build() {
  local cmake_args=(
    -S "$REPO_ROOT"
    -B "$BUILD_DIR"
    -DCMAKE_BUILD_TYPE=Release
  )

  if [[ ! -f "$BUILD_DIR/CMakeCache.txt" ]]; then
    if command -v ninja >/dev/null 2>&1; then
      cmake_args+=(-G Ninja)
    else
      cmake_args+=(-G "Unix Makefiles")
    fi
  fi

  cmake "${cmake_args[@]}"
}

echo "== Repository Structure =="
missing_paths=()
for path in "${required_paths[@]}"; do
  if [[ -d "$path" ]]; then
    echo "OK - ${path#"$REPO_ROOT/"}"
  else
    echo "MISSING - ${path#"$REPO_ROOT/"}"
    missing_paths+=("${path#"$REPO_ROOT/"}")
  fi
done
if [[ ${#missing_paths[@]} -gt 0 ]]; then
  echo "ERROR: Repository structure is incomplete. Missing directories:"
  for p in "${missing_paths[@]}"; do echo "  - $p"; done
  echo "  This usually means the repository was not cloned correctly."
  echo "  Fix: re-clone the full repository and retry."
  exit 1
fi

echo "== Runtime JSON Header =="
if [[ -f "$JSON_HEADER" ]]; then
  echo "OK - runtime/cpp/external/nlohmann/json.hpp"
else
  mkdir -p "$(dirname "$JSON_HEADER")"
  echo "Downloading nlohmann/json single header..."
  download_ok=0
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$JSON_URL" -o "$JSON_HEADER" && download_ok=1
  elif command -v wget >/dev/null 2>&1; then
    wget -qO "$JSON_HEADER" "$JSON_URL" && download_ok=1
  else
    echo "ERROR: Neither curl nor wget is available."
    echo "  Fix: sudo apt-get install curl"
    exit 1
  fi
  if [[ "$download_ok" -eq 0 ]]; then
    echo "ERROR: Failed to download nlohmann/json header."
    echo "  URL: $JSON_URL"
    echo "  Possible causes: no internet connection, firewall, or the URL has changed."
    echo "  Fix options:"
    echo "    1. Check your network and retry: ./scripts/bootstrap.sh"
    echo "    2. Download manually and place at: runtime/cpp/external/nlohmann/json.hpp"
    exit 1
  fi
  echo "Installed - runtime/cpp/external/nlohmann/json.hpp"
fi

if ! command -v g++ >/dev/null 2>&1; then
  echo "ERROR: Missing required compiler: g++"
  echo "  Fix: sudo apt-get install g++ build-essential"
  echo "  Then retry: ./scripts/bootstrap.sh"
  exit 1
fi

if ! command -v cmake >/dev/null 2>&1; then
  echo "ERROR: Missing required build tool: cmake"
  echo "  Fix: sudo apt-get install cmake"
  exit 1
fi

mkdir -p "$BUILD_DIR"

echo "== Building Runtime Entrypoint (C++) =="
configure_runtime_build
cmake --build "$BUILD_DIR" --config Release --target forge_runtime -j 4

if [[ "$RUNTIME_ONLY" == "--runtime-only" ]]; then
  if [[ -x "$RUNTIME_BIN" ]]; then
    echo "== Starting Runtime Only =="
    "$RUNTIME_BIN" "$REPO_ROOT"
  else
    echo "ERROR: Runtime binary unavailable after successful build."
    exit 1
  fi
  echo "Bootstrap completed successfully (runtime-only)."
  exit 0
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "WARNING: dotnet SDK not found; cannot start C# app entrypoint."
  echo "Run './scripts/bootstrap.sh --runtime-only' for runtime-only verification."
  exit 2
fi

echo "== Starting C# App Entrypoint =="
if [[ "$LAUNCHER_SMOKE" -eq 1 ]]; then
  dotnet run --project "$EDITOR_PROJECT" -- --launcher-smoke "$RUNTIME_BIN"
else
  dotnet run --project "$EDITOR_PROJECT" -- --editor-ui "$RUNTIME_BIN"
fi

echo "Bootstrap completed successfully."
