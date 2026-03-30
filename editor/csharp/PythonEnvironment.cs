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

        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in GetWindowsBootstrapPythonCandidates())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    public static string GetRepositoryVirtualEnvironmentPythonExecutable(string repositoryRoot)
    {
        var candidates = GetVirtualEnvironmentCandidates(repositoryRoot).ToList();
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates.First();
    }

    private static IEnumerable<string> GetVirtualEnvironmentCandidates(string repositoryRoot)
    {
        var venvRoot = Path.Combine(repositoryRoot, ".venv");
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(venvRoot, "Scripts", "python.exe");
            yield return Path.Combine(venvRoot, "bin", "python.exe");
            yield return Path.Combine(venvRoot, "bin", "python");
            yield break;
        }

        yield return Path.Combine(venvRoot, "bin", "python3");
        yield return Path.Combine(venvRoot, "bin", "python");
    }

    public static bool IsMsysPythonExecutable(string pythonExecutable)
    {
        if (string.IsNullOrWhiteSpace(pythonExecutable))
        {
            return false;
        }

        var normalized = pythonExecutable.Replace('/', '\\');
        return normalized.Contains("\\msys64\\", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetWindowsBootstrapPythonCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe");
            yield return Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe");
            yield return Path.Combine(localAppData, "Programs", "Python", "Python310", "python.exe");
        }
    }
}
