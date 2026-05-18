using Avalonia.Controls;
using Soul.Editor.EditorShell.Navigation;

namespace Soul.Editor.Tests;

public sealed class WorkflowNavigationTests
{
    [Fact]
    public void WorkflowSection_EnumMatchesManifest()
    {
        var root = ResolveProjectRoot();
        var manifest = WorkflowNavigationManifest.LoadFromRepository(root);
        var enumNames = Enum.GetNames<WorkflowSection>().OrderBy(name => name).ToList();
        var manifestSections = manifest.Sections.Select(entry => entry.Section).OrderBy(name => name).ToList();
        Assert.Equal(enumNames, manifestSections);
    }

    [Fact]
    public void WorkflowNavigationService_ActivatesEachSection()
    {
        var service = WorkflowShellRegistry.CreateDefault();
        var context = new WorkflowProjectContext(
            HasOpenProject: true,
            HasPrototype: true,
            IsCreatorModeEnabled: false,
            ProjectRootPath: "/tmp/project");

        foreach (var section in Enum.GetValues<WorkflowSection>())
        {
            Assert.True(service.Navigate(section, context));
            Assert.Equal(section, service.ActiveSection);
            Assert.NotNull(service.GetActiveShell());
        }
    }

    [Fact]
    public void NavigateBack_DeactivatesCurrentShellBeforeActivatingPrevious()
    {
        var home = new RecordingWorkflowShell(WorkflowSection.ProjectHome);
        var editor = new RecordingWorkflowShell(WorkflowSection.Editor);
        var service = new WorkflowNavigationService([home, editor]);
        var context = new WorkflowProjectContext(
            HasOpenProject: true,
            HasPrototype: true,
            IsCreatorModeEnabled: false,
            ProjectRootPath: "/tmp/project");

        Assert.True(service.Navigate(WorkflowSection.ProjectHome, context));
        Assert.True(service.Navigate(WorkflowSection.Editor, context));
        Assert.Equal(WorkflowSection.Editor, service.ActiveSection);
        Assert.Empty(home.Activated);
        Assert.Equal([true], home.Deactivated);
        Assert.Equal([true], editor.Activated);
        Assert.Empty(editor.Deactivated);

        Assert.True(service.NavigateBack(context));
        Assert.Equal(WorkflowSection.ProjectHome, service.ActiveSection);
        Assert.Equal([true], home.Activated);
        Assert.Equal([true], home.Deactivated);
        Assert.Equal([true], editor.Activated);
        Assert.Equal([true], editor.Deactivated);
    }

    [Fact]
    public void ManifestLabels_AppearInShellAndMainWindowAxaml()
    {
        var root = ResolveProjectRoot();
        var manifest = WorkflowNavigationManifest.LoadFromRepository(root);
        var axamlRoots = new[]
        {
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "MainWindow.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "WorkflowNavigationBar.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "Shells", "ProjectHomeShellView.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "Shells", "AiInterviewShellView.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "Shells", "PrototypeShellView.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "Shells", "TestingShellView.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "Shells", "PublishShellView.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "Shells", "AssetLibraryShellView.axaml"),
            Path.Combine(root, "editor", "csharp", "EditorShell", "UI", "Shells", "SettingsShellView.axaml"),
        };

        var combined = string.Join('\n', axamlRoots.Where(File.Exists).Select(File.ReadAllText))
            .Replace("&amp;", "&", StringComparison.Ordinal);

        foreach (var entry in manifest.Sections)
        {
            Assert.Contains(entry.NavLabel, combined, StringComparison.Ordinal);
            Assert.Contains(entry.PrimaryCta, combined, StringComparison.Ordinal);
            foreach (var control in entry.RequiredControls)
            {
                var normalized = control.Replace("&", "&amp;", StringComparison.Ordinal);
                Assert.True(
                    combined.Contains(control, StringComparison.Ordinal)
                    || combined.Contains(normalized, StringComparison.Ordinal),
                    $"Missing required control label '{control}' for section {entry.Section}");
            }
        }
    }

    private static string ResolveProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(current, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("..", i))));
            if (File.Exists(Path.Combine(candidate, "SOUL_LOOM_V1_BLUEPRINT.md")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Unable to resolve repository root from test base directory.");
    }

    private sealed class RecordingWorkflowShell(WorkflowSection section) : IWorkflowShell
    {
        public List<bool> Activated { get; } = [];
        public List<bool> Deactivated { get; } = [];

        public WorkflowSection Section => section;

        public string Title => Section.ToString();

        public string Subtitle => string.Empty;

        public string PrimaryCtaLabel => "Test";

        public bool IsAvailable(WorkflowProjectContext context) => true;

        public Control CreateView() => new Panel();

        public void OnActivated(WorkflowProjectContext context) => Activated.Add(true);

        public void OnDeactivated() => Deactivated.Add(true);
    }
}
