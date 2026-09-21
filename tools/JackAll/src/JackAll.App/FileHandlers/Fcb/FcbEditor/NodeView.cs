namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>One node of a tree being edited - a component, a slot, a group - built the first time it is shown.</summary>
public sealed class NodeView(
    string title, Func<IReadOnlyList<FieldView>> fields, Func<IReadOnlyList<NodeView>> children, Action? remove = null)
    : Observable
{
    private IReadOnlyList<FieldView>? _fields;
    private IReadOnlyList<NodeView>? _children;
    private bool _isExpanded;

    public string Title { get; } = title;

    /// <summary>Only a component the instance added can be removed.</summary>
    public bool CanRemove => remove is not null;

    public void Remove() => remove?.Invoke();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(Fields));
                OnPropertyChanged(nameof(Sections));
            }
        }
    }

    /// <summary>The fields while expanded, for a foldout.</summary>
    public IReadOnlyList<FieldView> Fields => _isExpanded ? OwnFields : [];

    /// <summary>The child nodes while expanded, shown inside this one.</summary>
    public IReadOnlyList<NodeView> Sections => _isExpanded ? OwnChildren : [];

    /// <summary>Fields then child nodes, for a tree that asks only once a row is expanded.</summary>
    public IReadOnlyList<object> Items => [.. OwnFields, .. OwnChildren];

    private IReadOnlyList<FieldView> OwnFields => _fields ??= fields();

    private IReadOnlyList<NodeView> OwnChildren => _children ??= children();
}
