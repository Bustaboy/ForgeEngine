"""Hardware benchmark and first-run recommendation helpers."""

from .performance import record_performance_snapshot, run_light_benchmark, should_run_idle_benchmark
from .wizard import run_benchmark_as_dict
from change_log import append_change_log_entry, get_recent_changes

__all__ = [
    "append_change_log_entry",
    "get_recent_changes",
    "record_performance_snapshot",
    "run_benchmark_as_dict",
    "run_light_benchmark",
    "should_run_idle_benchmark",
]
