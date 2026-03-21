param(
    [switch]$SkipRun
)

$arguments = @("scripts/bootstrap.py")
if ($SkipRun) {
    $arguments += "--skip-run"
}

python $arguments
