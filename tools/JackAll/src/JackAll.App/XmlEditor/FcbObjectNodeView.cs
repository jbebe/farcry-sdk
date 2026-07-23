using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.XmlEditor;

/// <summary>
/// One row in the fragment editor's object tree — a mutable, WPF-bindable wrapper around one
/// <see cref="FcbObject"/>, built once when a tab opens (unlike the old text-based outline, nothing
/// here needs rebuilding on every edit: the tree's *shape* only changes if the user adds/removes a
/// child object, which this editor doesn't support in v1 - only values change, and a value edit never
/// changes which row it belongs under).
/// </summary>
public sealed class FcbObjectNodeView : INotifyPropertyChanged
{
    private static readonly uint NameFieldHash = FcbClassDefinitions.Crc32Ascii("Name");

    public FcbObject Object { get; }

    /// <summary>This same object's vanilla counterpart, or null when there's nothing to compare
    /// against (a mod-added container, or the container's shape has genuinely diverged) - see
    /// <c>GameVfs.ReadOriginalFragment</c>. Null means every value here reads as unremarkable base
    /// content, never highlighted.</summary>
    public FcbObject? Original { get; }

    public FcbClass OwnClass { get; }
    public string Label { get; }
    public ObservableCollection<FcbObjectNodeView> Children { get; } = [];

    /// <summary>Null for the root - set by <see cref="BuildNode"/>, used to bubble
    /// <see cref="ContainsChange"/> up without a separate lookup.</summary>
    public FcbObjectNodeView? Parent { get; private set; }

    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }

    private bool _isVisible = true;

    /// <summary>Drives the tree row's <c>Visibility</c> (see <see cref="ApplyFilter"/>) - unlike
    /// <see cref="IsExpanded"/>/<see cref="IsSelected"/>, this is set from code (the outline filter),
    /// not just pushed up from a TwoWay binding on user interaction, so it has to actually raise
    /// <see cref="PropertyChanged"/> for the TreeViewItem style's binding to notice a filter change.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    private int _ownChangedCount;
    private int _childrenWithChangesCount;

    /// <summary>True when this object's own values, or any descendant's, currently differ from
    /// vanilla - what lights up a branch in the tree so a change is findable without opening every
    /// node. Kept live: an edit anywhere updates this incrementally (see <see cref="OnOwnChangedDelta"/>/
    /// <see cref="OnChildContainsChangeFlipped"/>), it isn't a one-time snapshot from when the tab opened.</summary>
    public bool ContainsChange => _ownChangedCount > 0 || _childrenWithChangesCount > 0;

    /// <summary>Property rows for this node's own values - built lazily on first selection (there can
    /// be thousands of nodes in one fragment; most are never actually selected in a session) and cached
    /// forever after, since a row's validity has to keep counting toward Save-blocking even once the
    /// user has navigated away from it.</summary>
    private IReadOnlyList<PropertyRow>? _rows;
    private readonly IReadOnlyDictionary<uint, FcbXmlNameHarvest.Entry>? _extraNames;

    private FcbObjectNodeView(
        FcbObject obj, FcbObject? original, FcbClass ownClass, string label,
        IReadOnlyDictionary<uint, FcbXmlNameHarvest.Entry>? extraNames)
    {
        Object = obj;
        Original = original;
        OwnClass = ownClass;
        Label = label;
        _extraNames = extraNames;
    }

    public IReadOnlyList<PropertyRow> Rows => _rows ??= BuildRows();

    /// <summary>Whether <see cref="Rows"/> has already been accessed (and, by the same stroke, its
    /// events already wired) - the view model uses this to wire a node's rows exactly once, the first
    /// time it's selected, instead of tracking a separate "already wired" set.</summary>
    public bool RowsBuilt => _rows is not null;

    /// <summary>Writes every already-built row's (by this point always valid - Save is disabled
    /// otherwise) value back into <see cref="Object"/>'s own <see cref="FcbObject.Values"/>, in place.
    /// A node whose rows were never built was never touched, so it's already correct as-is.</summary>
    public void CommitRows()
    {
        if (_rows is null) return;
        foreach (PropertyRow row in _rows)
        {
            Object.Values[row.NameHash] = row.EncodeValue();
        }
    }

    /// <summary>Walks the whole tree committing every node that was ever selected - what a save does
    /// right before rendering, since an edit anywhere in the fragment (not just the currently-selected
    /// node) has to make it into the rendered output.</summary>
    public static void CommitAllRows(FcbObjectNodeView node)
    {
        node.CommitRows();
        foreach (FcbObjectNodeView child in node.Children)
        {
            CommitAllRows(child);
        }
    }

    private List<PropertyRow> BuildRows()
    {
        var rows = new List<PropertyRow>(Object.Values.Count);
        foreach ((uint nameHash, byte[] bytes) in Object.Values)
        {
            FcbMember? member = OwnClass.FindMember(nameHash);
            FcbXmlNameHarvest.Entry? extra = member is null && _extraNames is not null
                && _extraNames.TryGetValue(nameHash, out FcbXmlNameHarvest.Entry found) ? found : null;
            byte[]? originalBytes = null;
            Original?.Values.TryGetValue(nameHash, out originalBytes);
            PropertyRow row = PropertyRow.Build(
                nameHash, member?.Name ?? extra?.Name, member?.Type ?? extra?.ValueType ?? FcbMemberType.BinHex,
                bytes, originalBytes);
            row.ChangedFromVanillaFlagChanged += r => OnOwnChangedDelta(r.IsChangedFromVanilla ? 1 : -1);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Builds the whole tree from <paramref name="root"/>, resolving each object's class/label via
    /// <paramref name="defs"/>. A node's label is its class name (or "hash XXXXXXXX" when unresolved) -
    /// the same convention <see cref="FcbXml"/> itself uses - plus, when present, a human identifier:
    /// preferring a value literally named "Name" (the same field <c>FcbXml.TryGetFragmentIds</c> uses
    /// to name a whole fragment file), falling back to the first plain String value in the object when
    /// there's no "Name" field. That fallback matters in practice, not just in theory - real shipped
    /// data routinely distinguishes otherwise-identical sibling objects (e.g. every mesh-node entry
    /// under a CGraphicComponent) only through a differently-named String field such as
    /// <c>hidMeshName</c>, never through a literal "Name". <paramref name="original"/> is this same
    /// fragment's vanilla shape (see <c>GameVfs.ReadOriginalFragment</c>), null when there's nothing to
    /// compare against - walked in parallel with <paramref name="root"/>, by child position, to seed
    /// every node's initial <see cref="ContainsChange"/> via a cheap byte comparison, no decoding
    /// needed for that pass.
    /// </summary>
    /// <param name="extraNames">Optional fallback name/type source for whatever <paramref name="defs"/>
    /// can't resolve on its own - see <see cref="FcbXmlNameHarvest"/>. Null for the common case (a mod
    /// fragment, where nothing more than <paramref name="defs"/> ever named anything in the first
    /// place).</param>
    public static FcbObjectNodeView Build(
        FcbObject root, FcbObject? original, FcbClassDefinitions defs,
        IReadOnlyDictionary<uint, FcbXmlNameHarvest.Entry>? extraNames = null)
        => BuildNode(root, original, defs, extraNames);

    private static FcbObjectNodeView BuildNode(
        FcbObject obj, FcbObject? original, IFcbClassScope scope,
        IReadOnlyDictionary<uint, FcbXmlNameHarvest.Entry>? extraNames)
    {
        FcbClass ownClass = scope.Resolve(obj.TypeHash);
        string typeLabel = ownClass.Name
            ?? (extraNames is not null && extraNames.TryGetValue(obj.TypeHash, out var entry) ? entry.Name : null)
            ?? $"hash {obj.TypeHash:X8}";
        string label = FindIdentifyingText(obj, ownClass) is { Length: > 0 } text ? $"{typeLabel} - {text}" : typeLabel;

        var view = new FcbObjectNodeView(obj, original, ownClass, label, extraNames)
        {
            _ownChangedCount = CountOwnChanges(obj, original),
        };

        for (int i = 0; i < obj.Children.Count; i++)
        {
            FcbObject? originalChild = original is not null && i < original.Children.Count ? original.Children[i] : null;
            FcbObjectNodeView child = BuildNode(obj.Children[i], originalChild, ownClass, extraNames);
            child.Parent = view;
            view.Children.Add(child);
            if (child.ContainsChange)
            {
                view._childrenWithChangesCount++;
            }
        }
        return view;
    }

    /// <summary>A cheap, decode-free "does this object differ from vanilla" count - just how many of
    /// its own values have different bytes (or don't exist in <paramref name="original"/> at all,
    /// which shouldn't happen since this editor never adds/removes fields, but is treated as changed
    /// defensively). Null <paramref name="original"/> means nothing to compare - zero, not a guess.</summary>
    private static int CountOwnChanges(FcbObject obj, FcbObject? original)
    {
        if (original is null)
        {
            return 0;
        }

        int count = 0;
        foreach ((uint nameHash, byte[] bytes) in obj.Values)
        {
            if (!original.Values.TryGetValue(nameHash, out byte[]? originalBytes)
                || !bytes.AsSpan().SequenceEqual(originalBytes))
            {
                count++;
            }
        }
        return count;
    }

    private void OnOwnChangedDelta(int delta)
    {
        bool before = ContainsChange;
        _ownChangedCount += delta;
        NotifyIfContainsChangeFlipped(before);
    }

    private void OnChildContainsChangeFlipped(bool childNowContainsChange)
    {
        bool before = ContainsChange;
        _childrenWithChangesCount += childNowContainsChange ? 1 : -1;
        NotifyIfContainsChangeFlipped(before);
    }

    /// <summary>Only propagates to the parent (and raises PropertyChanged) when <see cref="ContainsChange"/>'s
    /// overall value actually flipped - a node that already contained a change for some other reason
    /// doesn't need to renotify its ancestors just because one more of its own fields changed too.</summary>
    private void NotifyIfContainsChangeFlipped(bool before)
    {
        bool after = ContainsChange;
        if (before == after) return;

        OnPropertyChanged(nameof(ContainsChange));
        Parent?.OnChildContainsChangeFlipped(after);
    }

    private static string? FindIdentifyingText(FcbObject obj, FcbClass ownClass)
    {
        if (obj.Values.TryGetValue(NameFieldHash, out byte[]? nameBytes)
            && FcbValueCodec.TryDecode(FcbMemberType.String, nameBytes, out object nameValue))
        {
            return (string)nameValue;
        }

        // No literal "Name" field - fall back to the first value the config actually declares as a
        // String, in file order, whatever it's called. Only ever a display hint, never authoritative:
        // the property grid always shows every field's real name once the node is selected.
        foreach ((uint nameHash, byte[] bytes) in obj.Values)
        {
            if (ownClass.FindMember(nameHash)?.Type == FcbMemberType.String
                && FcbValueCodec.TryDecode(FcbMemberType.String, bytes, out object text)
                && ((string)text).Length > 0)
            {
                return (string)text;
            }
        }

        return null;
    }

    public static bool ApplyFilter(FcbObjectNodeView node, string filter)
    {
        bool selfMatches = filter.Length == 0
            || node.Label.Contains(filter, StringComparison.OrdinalIgnoreCase);

        bool anyChildVisible = false;
        foreach (FcbObjectNodeView child in node.Children)
        {
            anyChildVisible |= ApplyFilter(child, filter);
        }

        node.IsVisible = selfMatches || anyChildVisible;
        return node.IsVisible;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
