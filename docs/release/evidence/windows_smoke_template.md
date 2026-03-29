# Windows Smoke Evidence Template (AT-010)

- Test ID: AT-010
- Runbook: `docs/release/CROSS_PLATFORM_SMOKE_RUNBOOK.md` (Windows section)
- Date (UTC):
- Operator:
- Branch/commit:
- Generated artifact folder convention: `docs/release/evidence/runs/<timestamp>/` (see `docs/release/evidence/SMOKE_EVIDENCE_SCHEMA.md`)

## 1) Environment snapshot

- OS version:
- PowerShell version (`$PSVersionTable.PSVersion`):
- Git version (`git --version`):
- `g++` version (`g++ --version`):
- Python version (`python --version`):
- Pytest version (`pytest --version`):
- Dotnet present? (`dotnet --version`): yes / no

## 2) Commands run

Paste exact commands executed (copy from terminal history).

```powershell
# paste commands here
```

## 3) Output and logs

- Runtime-only bootstrap log path:
- Full bootstrap log path (or skip note):
- Pytest log path:
- Additional artifacts/screenshots (optional):

### Output signature checks

- [ ] Contains `Soul Loom bootstrap (Windows)`
- [ ] Contains `== Building Runtime Entrypoint (C++) ==`
- [ ] Runtime-only exit code is `0`
- [ ] Full bootstrap exit code is `0` or documented skip due to missing dotnet
- [ ] Contains `Editor launcher started successfully.` (if full bootstrap attempted)
- [ ] `pytest -q` exit code is `0`
- [ ] `pytest -q` output contains `passed`

## 4) Verdict

- Final result: PASS / FAIL
- Blocking issue IDs (if FAIL):
- Notes:

## 5) Triage details (required on FAIL)

- First failing command:
- First failing log line/error message:
- Suspected category:
  - [ ] Toolchain missing
  - [ ] PATH/config issue
  - [ ] Runtime compile issue
  - [ ] C# bootstrap issue
  - [ ] Test regression
  - [ ] Other
- Follow-up owner:
- Next action:

