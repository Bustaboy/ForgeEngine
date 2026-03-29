# Cross-Platform Smoke Runbook (AT-010, AT-011)

This runbook provides executable smoke procedures for:
- **AT-010**: Windows smoke
- **AT-011**: Ubuntu smoke

Milestone: **Milestone 8 hardening**.

Use this document with:
- `docs/SETUP.md`
- `docs/release/evidence/windows_smoke_template.md`
- `docs/release/evidence/ubuntu_smoke_template.md`

---

## 1) Scope and smoke definition

Smoke = verify the repo boots, runtime compiles, core launcher path starts, and regression tests pass on the target OS.

This runbook uses only existing project commands:
- `./scripts/bootstrap.sh --runtime-only`
- `./scripts/bootstrap.sh` (when `dotnet` is installed)
- `pwsh -f scripts/bootstrap.ps1 -RuntimeOnly`
- `pwsh -f scripts/bootstrap.ps1`
- `pytest -q`
- `python3 ai-orchestration/python/orchestrator.py --benchmark --benchmark-no-prepare`

---

## 2) Evidence artifacts to produce

For every smoke execution, create one filled template:
- Windows run: `docs/release/evidence/windows_smoke_template.md`
- Ubuntu run: `docs/release/evidence/ubuntu_smoke_template.md`

Store terminal logs in an evidence folder (example):
- `docs/release/evidence/logs/windows/`
- `docs/release/evidence/logs/ubuntu/`

Recommended files per run:
- bootstrap runtime-only log
- full bootstrap log (if attempted)
- pytest log
- benchmark wizard log/json capture

---

## 3) Ubuntu smoke procedure (AT-011)

### 3.1 Prerequisites

Required:
- Ubuntu machine with `bash`
- `g++` (C++17 capable)
- Git
- Python + `pytest`

Optional but recommended:
- `.NET SDK 8+` (`dotnet`) for full bootstrap path

### 3.2 Commands (exact)

Run from repository root (`Soul Loom`):

```bash
set -o pipefail
mkdir -p docs/release/evidence/logs/ubuntu

./scripts/bootstrap.sh --runtime-only 2>&1 | tee docs/release/evidence/logs/ubuntu/bootstrap_runtime_only.log
BOOTSTRAP_RUNTIME_ONLY_EXIT=$?

dotnet --version >/dev/null 2>&1
DOTNET_PRESENT=$?
if [ "$DOTNET_PRESENT" -eq 0 ]; then
  ./scripts/bootstrap.sh 2>&1 | tee docs/release/evidence/logs/ubuntu/bootstrap_full.log
  BOOTSTRAP_FULL_EXIT=$?
else
  echo "dotnet not found; full bootstrap skipped" | tee docs/release/evidence/logs/ubuntu/bootstrap_full.log
  BOOTSTRAP_FULL_EXIT=127
fi

pytest -q 2>&1 | tee docs/release/evidence/logs/ubuntu/pytest_q.log
PYTEST_EXIT=$?

python3 ai-orchestration/python/orchestrator.py --benchmark --benchmark-no-prepare \
  2>&1 | tee docs/release/evidence/logs/ubuntu/benchmark_wizard.log
BENCHMARK_EXIT=$?

echo "bootstrap_runtime_only_exit=$BOOTSTRAP_RUNTIME_ONLY_EXIT"
echo "bootstrap_full_exit=$BOOTSTRAP_FULL_EXIT"
echo "pytest_exit=$PYTEST_EXIT"
echo "benchmark_exit=$BENCHMARK_EXIT"
```

### 3.3 Expected output signatures

Runtime-only bootstrap success indicators:
- `Soul Loom bootstrap (Ubuntu/Linux)`
- `== Building Runtime Entrypoint (C++) ==`
- `Runtime says hello from C++17` (or runtime launch confirmation)

Full bootstrap success indicators (when `dotnet` is present):
- `== Starting C# App Entrypoint ==`
- `Editor launcher started successfully.`

Pytest success indicator:
- summary line containing `passed`
- exit code `0`

### 3.4 Pass/fail criteria

Pass AT-011 smoke when all are true:
1. `bootstrap_runtime_only_exit=0`
2. `bootstrap_full_exit=0` **or** documented skip because `dotnet` missing
3. `pytest_exit=0`
4. `benchmark_exit=0`
5. Evidence template is fully filled with log paths and verdict

Fail AT-011 smoke when any are true:
- runtime-only bootstrap non-zero exit
- full bootstrap fails when `dotnet` exists
- pytest non-zero exit
- benchmark command non-zero exit
- required evidence missing/incomplete

### 3.5 Triage guidance

If runtime bootstrap fails:
- Check `g++ --version`
- Confirm `runtime/cpp/main.cpp` exists
- Re-run `./scripts/bootstrap.sh --runtime-only` and capture first failing line

If full bootstrap fails:
- Check `dotnet --info`
- Confirm `editor/csharp/Program.cs` exists
- Re-run and capture first exception/error line

If `pytest -q` fails:
- Re-run with `pytest -q -x` to isolate first failure
- Attach failing test node id(s)
- Confirm local environment differences in evidence notes

---

## 4) Windows smoke procedure (AT-010)

### 4.1 Prerequisites

Required:
- Windows with PowerShell 7+ (`pwsh`)
- MinGW-w64 `g++` with C++17 support in `PATH`
- Git
- Python + `pytest`

Optional but recommended:
- `.NET SDK 8+` (`dotnet`) for full bootstrap path

### 4.2 Commands (exact)

Run from repository root (`Soul Loom`) in `pwsh`:

```powershell
New-Item -ItemType Directory -Force -Path docs/release/evidence/logs/windows | Out-Null

pwsh -f scripts/bootstrap.ps1 -RuntimeOnly *>&1 | Tee-Object docs/release/evidence/logs/windows/bootstrap_runtime_only.log
$BOOTSTRAP_RUNTIME_ONLY_EXIT = $LASTEXITCODE

$DOTNET_PRESENT = 0
try {
  dotnet --version *> $null
  $DOTNET_PRESENT = 1
} catch {
  $DOTNET_PRESENT = 0
}

if ($DOTNET_PRESENT -eq 1) {
  pwsh -f scripts/bootstrap.ps1 *>&1 | Tee-Object docs/release/evidence/logs/windows/bootstrap_full.log
  $BOOTSTRAP_FULL_EXIT = $LASTEXITCODE
} else {
  "dotnet not found; full bootstrap skipped" | Tee-Object docs/release/evidence/logs/windows/bootstrap_full.log
  $BOOTSTRAP_FULL_EXIT = 127
}

pytest -q *>&1 | Tee-Object docs/release/evidence/logs/windows/pytest_q.log
$PYTEST_EXIT = $LASTEXITCODE

python ai-orchestration/python/orchestrator.py --benchmark --benchmark-no-prepare *>&1 | Tee-Object docs/release/evidence/logs/windows/benchmark_wizard.log
$BENCHMARK_EXIT = $LASTEXITCODE

"bootstrap_runtime_only_exit=$BOOTSTRAP_RUNTIME_ONLY_EXIT"
"bootstrap_full_exit=$BOOTSTRAP_FULL_EXIT"
"pytest_exit=$PYTEST_EXIT"
"benchmark_exit=$BENCHMARK_EXIT"
```

### 4.3 Expected output signatures

Runtime-only bootstrap success indicators:
- `Soul Loom bootstrap (Windows)`
- `== Building Runtime Entrypoint (C++) ==`
- runtime launch confirmation (for example `Runtime says hello from C++17`)

Full bootstrap success indicators (when `dotnet` is present):
- `== Starting C# App Entrypoint ==`
- `Editor launcher started successfully.`

Pytest success indicator:
- summary line containing `passed`
- exit code `0`

### 4.4 Pass/fail criteria

Pass AT-010 smoke when all are true:
1. `bootstrap_runtime_only_exit=0`
2. `bootstrap_full_exit=0` **or** documented skip because `dotnet` missing
3. `pytest_exit=0`
4. `benchmark_exit=0`
5. Evidence template is fully filled with log paths and verdict

Fail AT-010 smoke when any are true:
- runtime-only bootstrap non-zero exit
- full bootstrap fails when `dotnet` exists
- pytest non-zero exit
- benchmark command non-zero exit
- required evidence missing/incomplete

### 4.5 Triage guidance

If runtime bootstrap fails:
- Check `g++ --version` in PowerShell
- Confirm compiler is in PATH
- Re-run `pwsh -f scripts/bootstrap.ps1 -RuntimeOnly` and capture first failing line

If full bootstrap fails:
- Check `dotnet --info`
- Re-run `pwsh -f scripts/bootstrap.ps1` and capture first exception/error line

If `pytest -q` fails:
- Re-run with `pytest -q -x`
- Attach failing test node id(s)
- Capture Python and pytest versions in evidence

---

## 5) Release checklist linkage

For each smoke run, ensure the corresponding filled template includes:
- environment snapshot
- exact commands run
- outputs/log links
- final verdict + notes

Traceability references:
- AT-010 ↔ this runbook (Windows section) + `windows_smoke_template.md`
- AT-011 ↔ this runbook (Ubuntu section) + `ubuntu_smoke_template.md`

