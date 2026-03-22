using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using GameForge.Editor.EditorShell.Services;

namespace GameForge.Editor.EditorShell.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly OrchestratorClient _orchestratorClient = new();

    private string _chatPrompt = string.Empty;
    private string _statusMessage = "Ready: local-first, single-player, no-code-first workflow.";
    private bool _isBusy;
    private string? _lastBriefPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ChatPrompt
    {
        get => _chatPrompt;
        set => SetField(ref _chatPrompt, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public Task NewPrototypeAsync(CancellationToken cancellationToken = default)
    {
        ChatPrompt = string.Empty;
        _lastBriefPath = null;
        StatusMessage = "New prototype started. Enter a brief in chat and click Generate & Play.";
        return Task.CompletedTask;
    }

    public async Task GenerateFromBriefAsync(bool launchRuntime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ChatPrompt))
        {
            StatusMessage = "Enter a prototype brief first.";
            return;
        }

        _lastBriefPath = OrchestratorClient.CreateBriefFromChatPrompt(ChatPrompt);
        await RunPipelineForBriefAsync(_lastBriefPath, launchRuntime, cancellationToken);
    }

    public async Task PlayRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_lastBriefPath))
        {
            StatusMessage = "No generated brief yet. Run Generate from Brief first.";
            return;
        }

        await RunPipelineForBriefAsync(_lastBriefPath, launchRuntime: true, cancellationToken);
    }

    public void SetStatusMessage(string value)
    {
        StatusMessage = value;
    }

    private async Task RunPipelineForBriefAsync(string briefPath, bool launchRuntime, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Running generation pipeline...";

        try
        {
            var response = await _orchestratorClient.RunGenerationPipelineAsync(briefPath, launchRuntime, cancellationToken);
            StatusMessage = BuildStatusMessage(response);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pipeline failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildStatusMessage(PipelineRunResponse response)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Pipeline exit code: {response.ExitCode}");

        if (response.Result is null)
        {
            builder.AppendLine("Unable to parse pipeline JSON output.");
            if (!string.IsNullOrWhiteSpace(response.Stderr))
            {
                builder.AppendLine(response.Stderr.Trim());
            }

            return builder.ToString().Trim();
        }

        builder.AppendLine($"Pipeline status: {response.Result.Status}");
        builder.AppendLine($"Prototype root: {response.Result.PrototypeRoot ?? "(none)"}");
        builder.AppendLine($"Runtime launch: {response.Result.RuntimeLaunchStatus}");

        if (response.Result.RuntimeLaunchPid is int pid)
        {
            builder.AppendLine($"Runtime PID: {pid}");
        }

        if (response.Result.DeadEndBlockers.Count > 0)
        {
            builder.AppendLine("Dead-end blockers:");
            foreach (var blocker in response.Result.DeadEndBlockers)
            {
                builder.AppendLine($"- {blocker}");
            }
        }

        if (!string.IsNullOrWhiteSpace(response.Stderr))
        {
            builder.AppendLine("stderr:");
            builder.AppendLine(response.Stderr.Trim());
        }

        return builder.ToString().Trim();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
