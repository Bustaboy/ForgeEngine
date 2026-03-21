#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$PROJECT_DIR"
g++ -std=c++17 runtime/main.cpp -o runtime/prototype_runtime
./runtime/prototype_runtime
