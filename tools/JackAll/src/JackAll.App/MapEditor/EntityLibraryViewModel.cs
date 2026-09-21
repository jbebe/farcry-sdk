using JackAll.App.Library;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>The Map tab's entity library: the archetypes the loaded world can place, as folders and
/// a searchable list to drag from.</summary>
public sealed class EntityLibraryViewModel : Observable
{
    /// <summary>The drag-and-drop format carrying an archetype's full name.</summary>
    public const string DragFormat = "JackAll.Archetype";

    /// <summary>A list longer than this is cut; a search narrows it.</summary>
    private const int MaxItems = 2000;

    private ArchetypeTreeNode? _root;
    private ArchetypeTreeNode? _folder;
    private IReadOnlyList<ArchetypeTreeNode> _items = [];
    private string _search = "";
    private string _countText = "";

    public IEnumerable<ArchetypeTreeNode> Folders => _root?.Groups ?? [];

    public ArchetypeTreeNode? SelectedFolder
    {
        get => _folder;
        set { if (Set(ref _folder, value)) Refresh(); }
    }

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) Refresh(); }
    }

    public IReadOnlyList<ArchetypeTreeNode> Items
    {
        get => _items;
        private set => Set(ref _items, value);
    }

    public string CountText
    {
        get => _countText;
        private set => Set(ref _countText, value);
    }

    public void Load(ArchetypeIndex index)
    {
        _root = ArchetypeTreeNode.Build(index);
        _folder = null;
        OnPropertyChanged(nameof(Folders));
        Refresh();
    }

    /// <summary>A search looks through every archetype; otherwise the list is the selected folder's.</summary>
    private void Refresh()
    {
        if (_root is null)
        {
            return;
        }

        IEnumerable<ArchetypeTreeNode> scope = (_search.Length > 0 ? _root : _folder ?? _root).Archetypes();
        List<ArchetypeTreeNode> matches = [.. scope.Where(a =>
            _search.Length == 0 || a.FullName!.Contains(_search, StringComparison.OrdinalIgnoreCase))];
        Items = [.. matches.Take(MaxItems)];
        CountText = matches.Count > MaxItems
            ? $"first {MaxItems:N0} of {matches.Count:N0} archetypes - search to narrow"
            : $"{matches.Count:N0} archetypes";
    }
}
