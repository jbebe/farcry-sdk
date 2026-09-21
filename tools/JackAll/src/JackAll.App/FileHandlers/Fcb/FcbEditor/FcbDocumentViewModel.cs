using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>The selected outline row when it is an entity: its merge and component foldouts.</summary>
public sealed record EntityPane(string Heading, string Details, EntityInspector Inspector);

/// <summary>The selected outline row when it is not an entity: its fields and plain nodes, recursively.</summary>
public sealed record KeyValuePane(string Heading, IReadOnlyList<object> Items);

/// <summary>
/// One open FCB document - a container, a fragment, a library, a save: an outline down to its
/// entities, and the selected row's view. Edits write into the tree as they are made; what saving
/// means is up to the caller's persist delegate.
/// </summary>
public sealed class FcbDocumentViewModel : Observable
{
    private readonly FcbEditContext _context;
    private readonly Func<FcbObject, Task<string?>> _persist;
    private readonly IEntityBases _entities;
    private OutlineNode? _selectedNode;
    private string? _notice;
    private string _filterText = "";
    private bool _isDirty;
    private bool _isBusy;
    private int _invalidFieldCount;

    /// <param name="baseline">The document's vanilla content, or null when there is nothing to compare against.</param>
    /// <param name="persist">Puts the edited tree wherever it belongs; returns null or an error.</param>
    /// <param name="entities">Which nodes are entities and what they merge over; by default every
    /// <c>Entity</c>, shown unmerged.</param>
    public FcbDocumentViewModel(
        string title, FcbObject root, FcbObject? baseline, FcbEditContext context,
        Func<FcbObject, Task<string?>> persist, IEntityBases? entities = null)
    {
        Title = title;
        _context = context;
        _persist = persist;
        _entities = entities ?? new ArchetypeBases(null);
        Root = OutlineNode.Build(root, baseline, context, _entities);
        context.Edited += () =>
        {
            IsDirty = true;
            _selectedNode?.Recompare();
        };
        context.ValidityChanged += field => InvalidFieldCount += field.IsValid ? -1 : 1;
    }

    public string Title { get; }


    public OutlineNode Root { get; }

    public IEnumerable<OutlineNode> RootItems => [Root];

    /// <summary>A warning strip above the document, or null for none.</summary>
    public string? Notice
    {
        get => _notice;
        set
        {
            if (Set(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => !string.IsNullOrEmpty(_notice);

    public string FilterText
    {
        get => _filterText;
        set
        {
            Set(ref _filterText, value);
            OutlineNode.ApplyFilter(Root, value.Trim());
        }
    }

    public OutlineNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!Set(ref _selectedNode, value))
            {
                return;
            }
            if (value is { Pane: null })
            {
                LoadPane(value);
            }
            OnPropertyChanged(nameof(SelectedPane));
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public object? SelectedPane => _selectedNode?.Pane;

    public bool HasSelection => _selectedNode is not null;

    /// <summary>Selects the row carrying <paramref name="value"/> in <paramref name="field"/>.</summary>
    public bool TryReveal(uint field, string value) => Reveal(OutlineNode.Reveal(Root, field, value));

    /// <inheritdoc cref="TryReveal(uint, string)"/>
    public bool TryReveal(uint field, byte[] value) => Reveal(OutlineNode.Reveal(Root, field, value));

    private bool Reveal(OutlineNode? node)
    {
        SelectedNode = node ?? SelectedNode;
        return node is not null;
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public int InvalidFieldCount
    {
        get => _invalidFieldCount;
        private set
        {
            if (Set(ref _invalidFieldCount, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanSave => IsDirty && InvalidFieldCount == 0 && !IsBusy;

    /// <summary>Hands the tree to the persist delegate; returns null on success or the error, which
    /// leaves the document dirty.</summary>
    public async Task<string?> SaveAsync()
    {
        if (!CanSave)
        {
            return null;
        }

        IsBusy = true;
        try
        {
            string? error = await _persist(Root.Object);
            if (error is null)
            {
                IsDirty = false;
            }
            return error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Builds <paramref name="row"/>'s view. What an entity merges over can take a world
    /// index to resolve, so that happens off the UI thread behind a placeholder.</summary>
    private async void LoadPane(OutlineNode row)
    {
        if (!row.IsEntity)
        {
            HashSet<FcbObject?> rows = row.RowObjects();
            var scope = new ScopedNode(MergedNode.Of(row.Object, null), row.Original is { } o ? MergedNode.Of(o, null) : null, row.Class);
            row.Pane = new KeyValuePane(row.Label, FcbNodeViews.KeyValueItems(scope, _context, rows.Contains));
            return;
        }

        row.Pane = "Resolving what this entity merges overâ€¦";
        try
        {
            (FcbObject? @base, string details) = await Task.Run(() => _entities.BaseOf(row.Object, row.Parent?.Object));
            MergedNode? baseline = row.Original is { } original ? MergedNode.Of(original, @base) : null;
            row.Pane = new EntityPane(row.Label, details, new EntityInspector(MergedNode.Of(row.Object, @base), baseline, _context));
        }
        catch (Exception ex)
        {
            row.Pane = $"Couldn't resolve what this entity merges over: {ex.Message}";
        }
        if (ReferenceEquals(row, _selectedNode))
        {
            OnPropertyChanged(nameof(SelectedPane));
        }
    }
}