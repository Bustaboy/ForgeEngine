using Soul.Editor.EditorShell.ViewModels;

namespace Soul.Editor.EditorShell.Navigation;

public sealed class WorkflowNavigationService
{
    private readonly IReadOnlyDictionary<WorkflowSection, IWorkflowShell> _shells;
    private readonly Stack<WorkflowSection> _history = new();

    public WorkflowNavigationService(IEnumerable<IWorkflowShell> shells)
    {
        _shells = shells.ToDictionary(shell => shell.Section);
        ActiveSection = WorkflowSection.ProjectHome;
    }

    public WorkflowSection ActiveSection { get; private set; }

    public event Action<WorkflowSection>? SectionChanged;

    public IReadOnlyList<WorkflowSection> GetVisibleSections(WorkflowProjectContext context, bool creatorMode)
    {
        if (creatorMode)
        {
            return
            [
                WorkflowSection.ProjectHome,
                WorkflowSection.AiInterview,
                WorkflowSection.Prototype,
                WorkflowSection.Editor,
                WorkflowSection.Publish,
            ];
        }

        return Enum.GetValues<WorkflowSection>().Cast<WorkflowSection>().ToList();
    }

    public bool CanNavigate(WorkflowSection section, WorkflowProjectContext context)
    {
        if (!_shells.TryGetValue(section, out var shell))
        {
            return false;
        }

        return shell.IsAvailable(context);
    }

    public bool Navigate(WorkflowSection section, WorkflowProjectContext context)
    {
        if (!CanNavigate(section, context))
        {
            return false;
        }

        if (ActiveSection == section)
        {
            return true;
        }

        if (_shells.TryGetValue(ActiveSection, out var current))
        {
            current.OnDeactivated();
        }

        _history.Push(ActiveSection);
        ActiveSection = section;

        if (_shells.TryGetValue(section, out var next))
        {
            next.OnActivated(context);
        }

        SectionChanged?.Invoke(section);
        return true;
    }

    public bool NavigateBack(WorkflowProjectContext context)
    {
        if (_history.Count == 0)
        {
            return false;
        }

        if (_shells.TryGetValue(ActiveSection, out var current))
        {
            current.OnDeactivated();
        }

        var previous = _history.Pop();
        ActiveSection = previous;
        if (_shells.TryGetValue(previous, out var shell))
        {
            shell.OnActivated(context);
        }

        SectionChanged?.Invoke(previous);
        return true;
    }

    public IWorkflowShell? GetActiveShell() =>
        _shells.TryGetValue(ActiveSection, out var shell) ? shell : null;

    public IWorkflowShell GetShell(WorkflowSection section) => _shells[section];
}
