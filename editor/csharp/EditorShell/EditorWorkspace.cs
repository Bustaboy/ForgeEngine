using System.Text.Json;

namespace GameForge.Editor.EditorShell;

public sealed class EditorWorkspace
{
    private readonly Dictionary<string, SceneObject> _objectIndex;
    private readonly Stack<MutationSnapshot> _undoStack = new();
    private readonly Stack<MutationSnapshot> _redoStack = new();
    private readonly List<EditTimelineEntry> _timeline = new();

    private PendingMajorMutation? _pendingMajorMutation;

    public EditorWorkspace(EditorProjectSnapshot project)
    {
        Project = project;
        Layout = EditorLayout.CreateDefault();
        _objectIndex = project.SceneObjects.ToDictionary(item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
    }

    public EditorProjectSnapshot Project { get; }

    public EditorLayout Layout { get; }

    public SceneObject? SelectedObject { get; private set; }

    public InspectorView? Inspector { get; private set; }

    public AiSelectionContext? AiContext { get; private set; }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public IReadOnlyList<EditTimelineEntry> Timeline => _timeline;

    public AiEditPreview? PendingPreview => _pendingMajorMutation?.Preview;

    public bool SelectObject(string objectId)
    {
        if (!_objectIndex.TryGetValue(objectId, out var sceneObject))
        {
            return false;
        }

        SelectedObject = sceneObject;
        Inspector = BuildInspector(sceneObject);
        AiContext = BuildAiContext(sceneObject);
        return true;
    }

    public AiMutationApplyResult ApplyAiMutation(AiMutationRequest request)
    {
        if (!_objectIndex.TryGetValue(request.TargetObjectId, out var target))
        {
            return new AiMutationApplyResult { Status = AiMutationApplyStatus.TargetNotFound, ChangedState = false };
        }

        var beforeProperties = CloneProperties(target.Properties);
        var changed = ApplyPropertyChanges(target.Properties, request.PropertyChanges);
        var afterProperties = CloneProperties(target.Properties);

        if (!changed)
        {
            return new AiMutationApplyResult { Status = AiMutationApplyStatus.NoChanges, ChangedState = false };
        }

        var preview = BuildPreview(request, beforeProperties, afterProperties);

        if (request.ImpactLevel == AiEditImpactLevel.Major)
        {
            target.Properties.Clear();
            foreach (var property in beforeProperties)
            {
                target.Properties[property.Key] = property.Value;
            }

            RefreshSelectionIfNeeded(target.ObjectId);

            _pendingMajorMutation = new PendingMajorMutation
            {
                Request = request,
                BeforeProperties = beforeProperties,
                AfterProperties = afterProperties,
                Preview = preview,
            };

            return new AiMutationApplyResult
            {
                Status = AiMutationApplyStatus.PreviewRequired,
                ChangedState = false,
                Preview = preview,
            };
        }

        RecordCommittedMutation(request, target.ObjectId, beforeProperties, afterProperties);

        return new AiMutationApplyResult
        {
            Status = AiMutationApplyStatus.Applied,
            ChangedState = true,
            Preview = preview,
        };
    }

    public bool ConfirmPendingMajorMutation()
    {
        if (_pendingMajorMutation is null)
        {
            return false;
        }

        if (!_objectIndex.TryGetValue(_pendingMajorMutation.Request.TargetObjectId, out var target))
        {
            _pendingMajorMutation = null;
            return false;
        }

        if (!PropertySetsEqual(target.Properties, _pendingMajorMutation.BeforeProperties))
        {
            _pendingMajorMutation = null;
            return false;
        }

        target.Properties.Clear();
        foreach (var property in _pendingMajorMutation.AfterProperties)
        {
            target.Properties[property.Key] = property.Value;
        }

        RecordCommittedMutation(
            _pendingMajorMutation.Request,
            target.ObjectId,
            _pendingMajorMutation.BeforeProperties,
            _pendingMajorMutation.AfterProperties);

        _pendingMajorMutation = null;
        return true;
    }

    public bool DiscardPendingMajorMutation()
    {
        if (_pendingMajorMutation is null)
        {
            return false;
        }

        _pendingMajorMutation = null;
        return true;
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        var snapshot = _undoStack.Pop();
        if (!_objectIndex.TryGetValue(snapshot.TargetObjectId, out var target))
        {
            return false;
        }

        target.Properties.Clear();
        foreach (var property in snapshot.Before)
        {
            target.Properties[property.Key] = property.Value;
        }

        _redoStack.Push(snapshot);
        InvalidatePendingMajorMutation();
        RefreshSelectionIfNeeded(snapshot.TargetObjectId);
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0)
        {
            return false;
        }

        var snapshot = _redoStack.Pop();
        if (!_objectIndex.TryGetValue(snapshot.TargetObjectId, out var target))
        {
            return false;
        }

        target.Properties.Clear();
        foreach (var property in snapshot.After)
        {
            target.Properties[property.Key] = property.Value;
        }

        _undoStack.Push(snapshot);
        InvalidatePendingMajorMutation();
        RefreshSelectionIfNeeded(snapshot.TargetObjectId);
        return true;
    }

    private void RecordCommittedMutation(
        AiMutationRequest request,
        string targetObjectId,
        IReadOnlyDictionary<string, JsonElement> beforeProperties,
        IReadOnlyDictionary<string, JsonElement> afterProperties)
    {
        _undoStack.Push(new MutationSnapshot
        {
            MutationId = request.MutationId,
            TargetObjectId = targetObjectId,
            Summary = request.Summary,
            Before = CloneProperties(beforeProperties),
            After = CloneProperties(afterProperties),
        });

        _redoStack.Clear();

        _timeline.Add(new EditTimelineEntry
        {
            MutationId = request.MutationId,
            TargetObjectId = targetObjectId,
            Summary = request.Summary,
            AppliedAtUtc = DateTimeOffset.UtcNow,
        });

        InvalidatePendingMajorMutation();
        RefreshSelectionIfNeeded(targetObjectId);
    }

    private void InvalidatePendingMajorMutation()
    {
        _pendingMajorMutation = null;
    }

    private void RefreshSelectionIfNeeded(string objectId)
    {
        if (SelectedObject is null)
        {
            return;
        }

        if (!string.Equals(SelectedObject.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = SelectObject(objectId);
    }

    private static bool ApplyPropertyChanges(
        IDictionary<string, JsonElement> destination,
        IReadOnlyDictionary<string, JsonElement> updates)
    {
        var changed = false;
        foreach (var update in updates)
        {
            if (destination.TryGetValue(update.Key, out var previous) && JsonElementsEqual(previous, update.Value))
            {
                continue;
            }

            destination[update.Key] = update.Value.Clone();
            changed = true;
        }

        return changed;
    }

    private static bool JsonElementsEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }

    private static bool PropertySetsEqual(
        IReadOnlyDictionary<string, JsonElement> left,
        IReadOnlyDictionary<string, JsonElement> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var rightValue))
            {
                return false;
            }

            if (!JsonElementsEqual(entry.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static AiEditPreview BuildPreview(
        AiMutationRequest request,
        IReadOnlyDictionary<string, JsonElement> beforeProperties,
        IReadOnlyDictionary<string, JsonElement> afterProperties)
    {
        var differences = request.PropertyChanges
            .Select(change => new AiEditPreviewDiff
            {
                PropertyName = change.Key,
                BeforeValue = beforeProperties.TryGetValue(change.Key, out var before)
                    ? ToDisplayText(before)
                    : "<unset>",
                AfterValue = afterProperties.TryGetValue(change.Key, out var after)
                    ? ToDisplayText(after)
                    : "<unset>",
            })
            .ToList();

        return new AiEditPreview
        {
            MutationId = request.MutationId,
            TargetObjectId = request.TargetObjectId,
            Summary = request.Summary,
            Differences = differences,
        };
    }

    private static Dictionary<string, JsonElement> CloneProperties(IReadOnlyDictionary<string, JsonElement> source)
    {
        var clone = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in source)
        {
            clone[property.Key] = property.Value.Clone();
        }

        return clone;
    }

    private static InspectorView BuildInspector(SceneObject sceneObject)
    {
        var simple = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = sceneObject.DisplayName,
            ["Type"] = sceneObject.ObjectType,
        };

        foreach (var property in sceneObject.Properties)
        {
            if (property.Key is "x" or "y" or "z" or "mode")
            {
                simple[property.Key] = ToDisplayText(property.Value);
            }
        }

        var advanced = sceneObject.Properties.ToDictionary(
            property => property.Key,
            property => ToDisplayText(property.Value),
            StringComparer.OrdinalIgnoreCase);

        return new InspectorView
        {
            ObjectId = sceneObject.ObjectId,
            ObjectLabel = sceneObject.DisplayName,
            SimpleSection = simple,
            AdvancedSection = advanced,
        };
    }

    private static AiSelectionContext BuildAiContext(SceneObject sceneObject)
    {
        var properties = sceneObject.Properties.ToDictionary(
            property => property.Key,
            property => ToDisplayText(property.Value),
            StringComparer.OrdinalIgnoreCase);

        return new AiSelectionContext
        {
            ObjectId = sceneObject.ObjectId,
            ObjectLabel = sceneObject.DisplayName,
            ObjectType = sceneObject.ObjectType,
            Properties = properties,
        };
    }

    private static string ToDisplayText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(),
        _ => string.Empty,
    };

    private sealed record MutationSnapshot
    {
        public required string MutationId { get; init; }

        public required string TargetObjectId { get; init; }

        public required string Summary { get; init; }

        public required IReadOnlyDictionary<string, JsonElement> Before { get; init; }

        public required IReadOnlyDictionary<string, JsonElement> After { get; init; }
    }

    private sealed record PendingMajorMutation
    {
        public required AiMutationRequest Request { get; init; }

        public required IReadOnlyDictionary<string, JsonElement> BeforeProperties { get; init; }

        public required IReadOnlyDictionary<string, JsonElement> AfterProperties { get; init; }

        public required AiEditPreview Preview { get; init; }
    }
}
