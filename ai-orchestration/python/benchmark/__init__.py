"""Hardware benchmark and first-run recommendation helpers."""

from .performance import record_performance_snapshot, run_light_benchmark, should_run_idle_benchmark
from .wizard import run_benchmark_as_dict

__all__ = [
    "record_performance_snapshot",
    "run_benchmark_as_dict",
    "run_light_benchmark",
    "should_run_idle_benchmark",
]
