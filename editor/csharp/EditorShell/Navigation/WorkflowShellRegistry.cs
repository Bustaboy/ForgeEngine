namespace Soul.Editor.EditorShell.Navigation;

public static class WorkflowShellRegistry
{
    public static WorkflowNavigationService CreateDefault() =>
        new(
        [
            new ProjectHomeWorkflowShell(),
            new AiInterviewWorkflowShell(),
            new PrototypeWorkflowShell(),
            new EditorWorkflowShell(),
            new TestingWorkflowShell(),
            new PublishWorkflowShell(),
            new AssetLibraryWorkflowShell(),
            new SettingsWorkflowShell(),
        ]);
}
