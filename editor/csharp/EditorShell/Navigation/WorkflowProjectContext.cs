namespace Soul.Editor.EditorShell.Navigation;

public sealed record WorkflowProjectContext(
    bool HasOpenProject,
    bool HasPrototype,
    bool IsCreatorModeEnabled,
    string? ProjectRootPath);
