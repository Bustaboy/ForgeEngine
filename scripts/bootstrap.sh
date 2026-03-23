#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="$REPO_ROOT/build/runtime"
RUNTIME_SRC="$REPO_ROOT/runtime/cpp/main.cpp"
RUNTIME_BIN="$BUILD_DIR/gameforge_runtime"
EDITOR_PROJECT="$REPO_ROOT/editor/csharp/GameForge.Editor.csproj"
RUNTIME_ONLY="${1:-}"
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

echo "== Repository Structure =="
for path in "${required_paths[@]}"; do
  if [[ -d "$path" ]]; then
    echo "OK - ${path#"$REPO_ROOT/"}"
  else
    echo "MISSING - ${path#"$REPO_ROOT/"}"
    exit 1
  fi
done

echo "== Runtime JSON Header =="
if [[ -f "$JSON_HEADER" ]]; then
  echo "OK - runtime/cpp/external/nlohmann/json.hpp"
else
  mkdir -p "$(dirname "$JSON_HEADER")"
  echo "Downloading nlohmann/json single header..."
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$JSON_URL" -o "$JSON_HEADER"
  elif command -v wget >/dev/null 2>&1; then
    wget -qO "$JSON_HEADER" "$JSON_URL"
  else
    echo "Missing downloader: install curl or wget"
    exit 1
  fi
  echo "Installed - runtime/cpp/external/nlohmann/json.hpp"
fi

if ! command -v g++ >/dev/null 2>&1; then
  echo "Missing required compiler: g++"
  echo "Install g++ and rerun ./scripts/bootstrap.sh"
  exit 1
fi

mkdir -p "$BUILD_DIR"

echo "== Building Runtime Entrypoint (C++) =="
runtime_build_ok=0
if g++ -std=c++17 "$RUNTIME_SRC" -o "$RUNTIME_BIN"; then
  runtime_build_ok=1
else
  echo "WARNING: Runtime build failed (Vulkan/GLFW dependencies may be missing)."
  echo "Continuing bootstrap in degraded mode."
fi

if [[ "$RUNTIME_ONLY" == "--runtime-only" ]]; then
  if [[ "$runtime_build_ok" -eq 1 ]]; then
    echo "== Starting Runtime Only =="
    "$RUNTIME_BIN" "$REPO_ROOT"
  else
    echo "== Runtime-only launch skipped (runtime binary unavailable) =="
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
dotnet run --project "$EDITOR_PROJECT" -- "$RUNTIME_BIN"

echo "Bootstrap completed successfully."
