namespace Soul.Editor.EditorShell.ViewModels;

/// <summary>
/// Shared constants for Python model-manager subprocess setup and resilient reads of <c>models.json</c>.
/// Extracted from <see cref="MainWindowViewModel"/> editor-systems logic to keep retry and pip lists in one place.
/// </summary>
internal static class ModelManagerWorkflowConstants
{
    /// <summary>Pip packages installed before running model download / onboarding helpers.</summary>
    public static readonly string[] LightweightModelDownloadPackages =
    [
        "huggingface_hub>=0.25.0",
        "filelock>=3.0.0",
        "tqdm>=4.66.0",
        "python-dotenv>=1.0.0",
        "requests>=2.32.0",
        "Pillow>=10.0.0",
    ];

    public const int ModelsJsonReadRetryCount = 8;

    public static readonly TimeSpan ModelsJsonReadRetryDelay = TimeSpan.FromMilliseconds(120);
}
