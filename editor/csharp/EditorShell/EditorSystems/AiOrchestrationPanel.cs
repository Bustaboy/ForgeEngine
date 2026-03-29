using System.Diagnostics;

namespace GameForge.Editor.EditorShell.EditorSystems;

public static class AiOrchestrationPanel
{
    public static ProcessStartInfo CreateOrchestratorStartInfo(string repositoryRoot, params string[] args)
    {
        var scriptPath = Path.Combine(repositoryRoot, "ai-orchestration", "python", "orchestrator.py");
        var pythonExe = PythonEnvironment.ResolvePythonExecutable(repositoryRoot);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
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
