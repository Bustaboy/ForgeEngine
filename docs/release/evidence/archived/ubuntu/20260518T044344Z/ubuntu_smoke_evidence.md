# Ubuntu Smoke Evidence (AT-011)

- Test ID: AT-011
- Run ID: 20260518T044344Z
- Date (UTC): 2026-05-18T04:46:16Z
- Operator: github-actions
- Branch/commit: HEAD / 80b33735b71536d8810d289477626e41edc51ee7

## Environment snapshot

- host_platform: Linux-6.17.0-1013-azure-x86_64-with-glibc2.39
- target_os: ubuntu
- python: Python 3.11.15
- pytest: pytest 9.0.3
- git: git version 2.54.0
- gpp: g++ (Ubuntu 13.3.0-6ubuntu2~24.04.1) 13.3.0
- dotnet_present: True
- dotnet_version: 10.0.300
- bash: GNU bash, version 5.2.21(1)-release (x86_64-pc-linux-gnu)
- pwsh: PowerShell 7.4.15

## Commands run

- `./scripts/bootstrap.sh --runtime-only` (exit: 0)
- `./scripts/bootstrap.sh --launcher-smoke` (exit: 0)
- `/opt/hostedtoolcache/Python/3.11.15/x64/bin/python -m pytest -q -p no:cacheprovider tests/` (exit: 0)

## Logs

- bootstrap_runtime_only: `docs/release/evidence/runs/20260518T044344Z/bootstrap_runtime_only.log`
- bootstrap_full: `docs/release/evidence/runs/20260518T044344Z/bootstrap_full.log`
- pytest_q: `docs/release/evidence/runs/20260518T044344Z/pytest_q.log`

## Output signature checks

- [ x ] `bootstrap_runtime_only` exit code policy
- [ x ] `bootstrap_runtime_only` contains `Soul Loom bootstrap (Ubuntu/Linux)`
- [ x ] `bootstrap_runtime_only` contains `== Building Runtime Entrypoint (C++) ==`
- [ x ] `bootstrap_full` exit code policy
- [ x ] `bootstrap_full` contains `== Starting C# App Entrypoint ==`
- [ x ] `bootstrap_full` contains `Editor launcher started successfully.`
- [ x ] `pytest_q` exit code policy
- [ x ] `pytest_q` contains `passed`

## Verdict

- Final result: PASS
- Notes: All required smoke commands satisfied or were contract-skipped.
