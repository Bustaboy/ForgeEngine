# ForgeEngine Model Management (Local-First)

This module is designed for `ai-orchestration/python/orchestrator.py` and C# shell subprocess calls.

## First-run prepare

```bash
python ai-orchestration/python/orchestrator.py --prepare-models
```

This command:
1. Loads `ai-orchestration/python/.env` if present.
2. Detects NVIDIA VRAM (`nvidia-smi`) or uses `FORGEENGINE_GPU_VRAM_GB` override.
3. Selects models that fit detected VRAM with CPU fallback support.
4. Downloads missing files into `ai-orchestration/python/models/artifacts/`.
5. Returns JSON payload containing runtime split fields (`gpu_layers`, `cpu_threads`).

## Integration example inside prototype generation flow

```python
from models import prepare_models_as_dict

model_context = prepare_models_as_dict(orchestrator_file=Path(__file__).resolve())
# feed model_context["runtime_configs"] into local inference wrapper
```

## C# shell example

```csharp
var process = Process.Start(new ProcessStartInfo {
    FileName = "python",
    Arguments = "ai-orchestration/python/orchestrator.py --prepare-models",
    RedirectStandardOutput = true,
    UseShellExecute = false
});
var modelJson = process.StandardOutput.ReadToEnd();
```
