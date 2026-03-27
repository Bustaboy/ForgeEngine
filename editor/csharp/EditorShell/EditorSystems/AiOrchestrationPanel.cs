using System.Diagnostics;

namespace GameForge.Editor.EditorShell.EditorSystems;

public static class AiOrchestrationPanel
{
    public static ProcessStartInfo CreateOrchestratorStartInfo(string repositoryRoot, params string[] args)
    {
        var scriptPath = Path.Combine(repositoryRoot, "ai-orchestration", "python", "orchestrator.py");
        var pythonExe = OperatingSystem.IsWindows() ? "python" : "python3";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        processStartInfo.ArgumentList.Add("-u");
        processStartInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
        {
            processStartInfo.ArgumentList.Add(arg);
        }

        return processStartInfo;
    }
}
