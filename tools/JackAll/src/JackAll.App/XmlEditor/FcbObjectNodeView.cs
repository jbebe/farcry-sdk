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
    private static readonly uint ValueFieldHash = FcbClassDefinitions.Crc32Ascii("Value");

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

    /// <summary>Per-nameHash dropdown choices for this node's own "selXxx" values, found by
    /// <see cref="FindEnumChoices"/> - see its remarks for the "selXxx"/"enumXxx" data convention this
    /// backs.</summary>
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<string>> _enumChoices;

    private FcbObjectNodeView(
        FcbObject obj, FcbObject? original, FcbClass ownClass, string label,
        IReadOnlyDictionary<uint, FcbXmlNameHarvest.Entry>? extraNames,
        IReadOnlyDictionary<uint, IReadOnlyList<string>> enumChoices)
    {
        Object = obj;
        Original = original;
        OwnClass = ownClass;
        Label = label;
        _extraNames = extraNames;
        _enumChoices = enumChoices;
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
            _enumChoices.TryGetValue(nameHash, out IReadOnlyList<string>? enumChoices);
            PropertyRow row = PropertyRow.Build(
                nameHash, member?.Name ?? extra?.Name, member?.Type ?? extra?.ValueType ?? FcbMemberType.BinHex,
                bytes, originalBytes, enumChoices);
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
        string? resolvedTypeName = ownClass.Name
            ?? (extraNames is not null && extraNames.TryGetValue(obj.TypeHash, out var entry) ? entry.Name : null);
        string? identifyingText = FindIdentifyingText(obj, ownClass);

        // An unresolved class has no name of its own to lead with, so the object's own identifying
        // text (its "Name" field, or the fallback FindIdentifyingText finds) takes that spot instead,
        // with the type hash alongside it in parens rather than spelled out as "hash 1A2B3C4D - Text" -
        // redundant once the parens already say it's a hash.
        string label = (resolvedTypeName, identifyingText) switch
        {
            (not null, { Length: > 0 } text) => $"{resolvedTypeName} - {text}",
            (not null, _) => resolvedTypeName,
            (null, { Length: > 0 } text) => $"{text} ({obj.TypeHash:X8})",
            (null, _) => $"hash {obj.TypeHash:X8}",
        };

        (Dictionary<uint, IReadOnlyList<string>> enumChoices, HashSet<string> hiddenChildTypeNames) = FindEnumChoices(obj, ownClass);

        var view = new FcbObjectNodeView(obj, original, ownClass, label, extraNames, enumChoices)
        {
            _ownChangedCount = CountOwnChanges(obj, original),
        };

        // Filtered independently (not "skip while walking obj.Children, reuse the same index into
        // original.Children") so the position-based obj/original pairing below stays 1:1 once some
        // children are hidden - both lists drop the same "enumXxx" groups, so the Nth *visible* child
        // of one still lines up with the Nth *visible* child of the other.
        List<FcbObject> visibleChildren = [.. obj.Children.Where(c => !IsHidden(c))];
        List<FcbObject>? visibleOriginalChildren = original is null ? null : [.. original.Children.Where(c => !IsHidden(c))];

        for (int i = 0; i < visibleChildren.Count; i++)
        {
            FcbObject? originalChild = visibleOriginalChildren is not null && i < visibleOriginalChildren.Count
                ? visibleOriginalChildren[i]
                : null;
            FcbObjectNodeView child = BuildNode(visibleChildren[i], originalChild, ownClass, extraNames);
            child.Parent = view;
            view.Children.Add(child);
            if (child.ContainsChange)
            {
                view._childrenWithChangesCount++;
            }
        }
        return view;

        bool IsHidden(FcbObject child) => hiddenChildTypeNames.Contains(ownClass.Resolve(child.TypeHash).Name ?? string.Empty);
    }

    /// <summary>
    /// Detects the base game's own "selXxx"/"enumXxx" data convention: a UInt32 value named e.g.
    /// "selType" paired with a sibling child object named "enumType", whose own children (each an
    /// "enum" object) hold one ordered String "Value" apiece - the option list the original Far Cry 2
    /// editor rendered as a dropdown, baked directly into the instance data rather than declared in
    /// binary_classes.xml (every "selXxx" member seen there is plain UInt32 - see
    /// docs/docs/modding/gotchas.md's <c>selCategory</c> note). Index i's plain integer value is i
    /// itself - <see cref="ScalarField.SelectedEnumIndex"/> relies on that.
    /// </summary>
    /// <returns>The dropdown choices per "selXxx" value's name hash, and the resolved type names of
    /// the "enumXxx" child objects that supplied one - <see cref="BuildNode"/> hides exactly those from
    /// the tree, since they're the raw form of what the returned choices already expose.</returns>
    private static (Dictionary<uint, IReadOnlyList<string>> Choices, HashSet<string> GroupTypeNames) FindEnumChoices(
        FcbObject obj, FcbClass ownClass)
    {
        var choices = new Dictionary<uint, IReadOnlyList<string>>();
        var groupTypeNames = new HashSet<string>();

        foreach ((uint nameHash, byte[] _) in obj.Values)
        {
            if (ownClass.FindMember(nameHash) is not { Name: { Length: > 3 } name, Type: FcbMemberType.UInt32 }
                || !name.StartsWith("sel", StringComparison.Ordinal) || !char.IsUpper(name[3]))
            {
                continue;
            }

            string expectedGroupName = "enum" + name[3..];
            FcbObject? group = obj.Children.FirstOrDefault(
                child => ownClass.Resolve(child.TypeHash).Name == expectedGroupName);
            if (group is null)
            {
                continue;
            }

            var names = new List<string>(group.Children.Count);
            foreach (FcbObject entry in group.Children)
            {
                if (entry.Values.TryGetValue(ValueFieldHash, out byte[]? valueBytes)
                    && FcbValueCodec.TryDecode(FcbMemberType.String, valueBytes, out object decoded))
                {
                    names.Add((string)decoded);
                }
            }

            if (names.Count == 0)
            {
                continue; // malformed/empty - fall back to the plain integer field instead of an empty dropdown
            }

            choices[nameHash] = names;
            groupTypeNames.Add(expectedGroupName);
        }

        return (choices, groupTypeNames);
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
