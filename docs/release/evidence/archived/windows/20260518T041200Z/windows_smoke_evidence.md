# Windows Smoke Evidence (AT-010)

- Test ID: AT-010
- Run ID: 20260518T041200Z
- Date (UTC): 2026-05-18T04:12:22Z
- Operator: local-dev-archive
- Branch/commit: main / fb8fd9315932bbcaef303f1aadffeb145f9c7428
- Archive path: `docs/release/evidence/archived/windows/20260518T041200Z/`

## Environment snapshot

- host_platform: Windows-10-10.0.26200-SP0
- target_os: windows
- python: Python 3.11.9
- pytest: pytest 9.0.3
- git: git version 2.45.1.windows.1
- gpp: g++ (Rev13, Built by MSYS2 project) 15.2.0
- dotnet_present: True
- dotnet_version: 9.0.301
- bash: GNU bash, version 5.2.21(1)-release (x86_64-pc-linux-gnu)
- pwsh: PowerShell 7.6.0

## Commands run

- `pwsh -f scripts/bootstrap.ps1 -RuntimeOnly` (exit: 0)
- `pwsh -f scripts/bootstrap.ps1 -LauncherSmoke` (exit: 0)
- `python -m pytest -q -p no:cacheprovider tests/` (exit: 0)

## Logs

- bootstrap_runtime_only: `docs/release/evidence/archived/windows/20260518T041200Z/bootstrap_runtime_only.log`
- bootstrap_full: `docs/release/evidence/archived/windows/20260518T041200Z/bootstrap_full.log`
- pytest_q: `docs/release/evidence/archived/windows/20260518T041200Z/pytest_q.log`

## Output signature checks

- [ x ] `bootstrap_runtime_only` exit code policy
- [ x ] `bootstrap_runtime_only` contains `Soul Loom bootstrap (Windows)`
- [ x ] `bootstrap_runtime_only` contains `== Building Runtime Entrypoint (C++) ==`
- [ x ] `bootstrap_full` exit code policy
- [ x ] `bootstrap_full` contains `== Starting C# App Entrypoint ==`
- [ x ] `bootstrap_full` contains `Editor launcher started successfully.`
- [ x ] `pytest_q` exit code policy
- [ x ] `pytest_q` contains `passed`

## Verdict

- Final result: PASS
- Notes: Archived from local run 20260518T041200Z after merge to main (PR #222).
