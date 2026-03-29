namespace GameForge.Editor;

public static class PythonEnvironment
{
    public static string ResolvePythonExecutable(string repositoryRoot)
    {
        var pinned = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(pinned))
        {
            return pinned;
        }

        foreach (var candidate in GetVirtualEnvironmentCandidates(repositoryRoot))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private static IEnumerable<string> GetVirtualEnvironmentCandidates(string repositoryRoot)
    {
        var venvRoot = Path.Combine(repositoryRoot, ".venv");
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(venvRoot, "Scripts", "python.exe");
            yield break;
        }

        yield return Path.Combine(venvRoot, "bin", "python3");
        yield return Path.Combine(venvRoot, "bin", "python");
    }
}
