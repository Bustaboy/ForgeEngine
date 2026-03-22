using System.Text.Json;

namespace GameForge.Editor.EditorShell;

public sealed record DockedPanel(string PanelId, string DisplayName, string DockZone, int Order);

public sealed record EditorLayout(IReadOnlyList<DockedPanel> Panels)
{
    public static EditorLayout CreateDefault() => new(
    [
        new DockedPanel("hierarchy", "Hierarchy", "left", 0),
        new DockedPanel("asset-browser", "Asset Browser", "left", 1),
        new DockedPanel("viewport", "Viewport", "center", 0),
        new DockedPanel("inspector", "Inspector", "right", 0),
        new DockedPanel("chat", "AI Copilot Chat", "bottom", 0),
    ]);
}

public sealed record SceneObject
{
    public required string ObjectId { get; init; }

    public required string DisplayName { get; init; }

    public required string ObjectType { get; init; }

    public Dictionary<string, JsonElement> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record InspectorView
{
    public required string ObjectId { get; init; }

    public required string ObjectLabel { get; init; }

    public required IReadOnlyDictionary<string, string> SimpleSection { get; init; }

    public required IReadOnlyDictionary<string, string> AdvancedSection { get; init; }
}

public sealed record AiSelectionContext
{
    public required string ObjectId { get; init; }

    public required string ObjectLabel { get; init; }

    public required string ObjectType { get; init; }

    public required IReadOnlyDictionary<string, string> Properties { get; init; }
}

public sealed record EditorProjectSnapshot
{
    public required string ProjectName { get; init; }

    public required string Scope { get; init; }

    public required string Rendering { get; init; }

    public required IReadOnlyList<string> Platforms { get; init; }

    public required IReadOnlyList<SceneObject> SceneObjects { get; init; }

    public required IReadOnlyList<AssetCatalogEntry> Assets { get; init; }

    public ProjectStyleConfig Style { get; init; } = ProjectStyleConfig.CreateDefault();
}

public sealed record AssetCatalogEntry
{
    public required string AssetId { get; init; }

    public required string DisplayName { get; init; }

    public required string Category { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public required string LicenseId { get; init; }

    public required string SourceType { get; init; }

    public required string RelativePath { get; init; }

    public required string ImportedAtUtc { get; init; }
}

public sealed record AssetBrowserFilter
{
    public string Query { get; init; } = string.Empty;

    public string? Category { get; init; }

    public IReadOnlyList<string> RequiredTags { get; init; } = [];
}

public sealed record AssetBrowserView
{
    public required AssetBrowserFilter Filter { get; init; }

    public required IReadOnlyList<AssetCatalogEntry> Results { get; init; }
}

public sealed record StylePresetDefinition
{
    public required string PresetId { get; init; }

    public required string DisplayName { get; init; }

    public string? ParentPresetId { get; init; }

    public required string Source { get; init; }
}

public sealed record ProjectStyleConfig
{
    public required string ActivePresetId { get; init; }

    public required string HelperMode { get; init; }

    public required IReadOnlyList<StylePresetDefinition> Presets { get; init; }

    public static ProjectStyleConfig CreateDefault() => new()
    {
        ActivePresetId = "cozy-stylized",
        HelperMode = "match-project-style",
        Presets =
        [
            new StylePresetDefinition { PresetId = "cozy-stylized", DisplayName = "Cozy Stylized", Source = "built-in" },
            new StylePresetDefinition { PresetId = "semi-realistic", DisplayName = "Semi-Realistic", Source = "built-in" },
            new StylePresetDefinition { PresetId = "low-poly-clean", DisplayName = "Low-Poly Clean", Source = "built-in" },
            new StylePresetDefinition { PresetId = "dark-fantasy-stylized", DisplayName = "Dark Fantasy Stylized", Source = "built-in" },
        ],
    };
}

public sealed record StylePresetSelectionView
{
    public required string ActivePresetId { get; init; }

    public required string ActivePresetDisplayName { get; init; }

    public required string HelperMode { get; init; }

    public required IReadOnlyList<StylePresetDefinition> AvailablePresets { get; init; }
}

public enum AiEditImpactLevel
{
    Minor = 0,
    Major = 1,
}

public sealed record AiMutationRequest
{
    public required string MutationId { get; init; }

    public required string TargetObjectId { get; init; }

    public required string Summary { get; init; }

    public required AiEditImpactLevel ImpactLevel { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> PropertyChanges { get; init; }
}

public sealed record AiEditPreviewDiff
{
    public required string PropertyName { get; init; }

    public required string BeforeValue { get; init; }

    public required string AfterValue { get; init; }
}

public sealed record AiEditPreview
{
    public required string MutationId { get; init; }

    public required string TargetObjectId { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<AiEditPreviewDiff> Differences { get; init; }
}

public enum AiMutationApplyStatus
{
    Applied = 0,
    PreviewRequired = 1,
    TargetNotFound = 2,
    NoChanges = 3,
}

public sealed record AiMutationApplyResult
{
    public required AiMutationApplyStatus Status { get; init; }

    public required bool ChangedState { get; init; }

    public AiEditPreview? Preview { get; init; }
}

public sealed record EditTimelineEntry
{
    public required string MutationId { get; init; }

    public required string TargetObjectId { get; init; }

    public required string Summary { get; init; }

    public required DateTimeOffset AppliedAtUtc { get; init; }
}
