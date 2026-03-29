# Soul Loom Hardware Benchmark Wizard (First Run)

This package provides first-launch hardware detection + model recommendations for the C# editor shell.

## Entry points

- Python CLI: `python ai-orchestration/python/orchestrator.py --benchmark`
- Python function: `benchmark.run_benchmark_as_dict(...)`

## Behavior

1. Detect hardware (`GPU name`, `GPU VRAM`, `CPU cores`) using the shared `models/gpu.py` helpers.
2. Build recommendations from `models/registry.py::DEFAULT_MODEL_SET` (no duplicated model list).
3. On the very first run, automatically invoke `--prepare-models` behavior through `prepare_models_as_dict`.
4. Persist first-run completion sentinel to:
   - `ai-orchestration/python/models/.benchmark-first-run.json`

## C# editor shell example

Run:

```bash
dotnet run --project editor/csharp/GameForge.Editor.csproj -- --first-run-benchmark-example
```

The example calls `orchestrator.py --benchmark`, parses JSON, then renders a simple first-run modal summary in console output.

## Verification steps

- `python3 ai-orchestration/python/orchestrator.py --benchmark --benchmark-no-prepare`
- `python3 -m pytest -q tests/test_benchmark_wizard.py`
- `python3 scripts/run_smoke_and_capture_evidence.py --help`

Use the smoke evidence workflow templates in `docs/release/evidence/` to attach benchmark JSON output as part of launch validation notes.

