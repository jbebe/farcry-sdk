using System.Numerics;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>
/// The Map tab's inspector: the selected entity as the engine reads it, its instance merged over its
/// archetype. An edit goes straight into the instance and is pending until the next save; a field
/// set back to the archetype's value, or reverted, stops being an override.
/// </summary>
public sealed class InspectorViewModel : Observable
{
    /// <summary>Identity and placement: shown in the heading and the Transform section, not as fields.</summary>
    private static readonly HashSet<uint> NotFields =
    [
        WorldHashes.DisEntityId, WorldHashes.HidName, WorldHashes.TplCreatureType,
        WorldHashes.HidPos, WorldHashes.HidPosPrecise, WorldHashes.HidAngles,
    ];

    private static readonly Lazy<(string Name, uint Hash)[]> Creatable = new(() =>
    [
        .. FcbDefinitionsProvider.Schema.Value.Classes
            .Where(c => c.IsComponent && c.Creatable)
            .Select(c => (c.Name, FcbClassDefinitions.Crc32Ascii(c.Name))),
    ]);

    private readonly SelectionSet _selection;
    private WorldEntity? _entity;
    private MergedNode? _merged;
    private IReadOnlyList<string> _addableComponents = [];
    private string _heading = "";
    private string _details = "";
    private bool _canShowArchetype;
    private IReadOnlyList<InspectorSection> _sections = [];
    private IReadOnlyList<InspectorField> _transform = [];

    public InspectorViewModel(SelectionSet selection)
    {
        _selection = selection;
        selection.Changed += Refresh;
    }

    /// <summary>What field edits go into; set once a world loads.</summary>
    public WorldEditSession? Session { get; set; }

    /// <summary>What the selected entity merges over; set once a world loads.</summary>
    public ArchetypeIndex? Archetypes { get; set; }

    /// <summary>Raised after any edit, with the entity; a transform edit is also a move.</summary>
    public event Action<WorldEntity, bool>? Edited;

    public bool HasEntity => _entity is not null;

    /// <summary>An unsaved addition has no fragment to open yet.</summary>
    public bool CanOpenSector => _entity is { IsNew: false };

    /// <summary>Only an entity that merges over an archetype this world declares has one to show.</summary>
    public bool CanShowArchetype
    {
        get => _canShowArchetype;
        private set => Set(ref _canShowArchetype, value);
    }

    public string Heading
    {
        get => _heading;
        private set => Set(ref _heading, value);
    }

    public string Details
    {
        get => _details;
        private set => Set(ref _details, value);
    }

    /// <summary>Position then angles, bound to the entity itself so the gizmos and these agree.</summary>
    public IReadOnlyList<InspectorField> Transform
    {
        get => _transform;
        private set => Set(ref _transform, value);
    }

    public IReadOnlyList<InspectorSection> Sections
    {
        get => _sections;
        private set => Set(ref _sections, value);
    }

    /// <summary>Creatable components the entity does not have yet.</summary>
    public IReadOnlyList<string> AddableComponents
    {
        get => _addableComponents;
        private set => Set(ref _addableComponents, value);
    }

    /// <summary>Shows the primary selection. The same entity again only updates the heading, so the
    /// sections a user opened stay open.</summary>
    public void Refresh()
    {
        WorldEntity? entity = _selection.Primary;
        string more = _selection.Count > 1 ? $"   (+{_selection.Count - 1} more selected)" : "";
        if (entity is not null && ReferenceEquals(entity, _entity))
        {
            Heading = EntityHierarchy.LabelOf(entity) + more;
            return;
        }

        _entity = entity;
        _merged = null;
        OnPropertyChanged(nameof(HasEntity));
        OnPropertyChanged(nameof(CanOpenSector));
        if (entity is null || Session is null)
        {
            Heading = Details = "";
            Transform = [];
            Sections = [];
            AddableComponents = [];
            return;
        }

        FcbObject? archetype = entity.ArchetypeName.Length > 0 ? Archetypes?.Winner(entity.ArchetypeName)?.Node : null;
        CanShowArchetype = archetype is not null;
        Heading = EntityHierarchy.LabelOf(entity) + more;
        Details = $"layer {entity.LayerPathId} · sector {entity.HomeSector.SectorId} · id {entity.Id}\n"
            + (entity.ArchetypeName.Length == 0
                ? "standalone - no archetype"
                : archetype is null
                    ? $"archetype {entity.ArchetypeName} is not in this world's library - the game will not spawn it"
                    : $"archetype {entity.ArchetypeName}");

        RefreshTransform();
        _merged = MergedNode.Of(Session.EditableNode(entity), archetype);
        BuildSections();
    }

    /// <summary>Adds an empty <paramref name="className"/> component to the instance; its registered
    /// properties show as unset until one is edited.</summary>
    public void AddComponent(string className)
    {
        if (_entity is not { } entity || _merged is not { } merged || Session is null)
        {
            return;
        }
        MergedNode components = ComponentsNode() ?? merged.AddChild(new FcbObject { TypeHash = WorldHashes.Components });
        components.AddChild(new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii(className) });
        Session.Edited(entity);
        Edited?.Invoke(entity, false);
        BuildSections();
    }

    private void RemoveComponent(MergedNode component)
    {
        if (_entity is not { } entity || Session is null)
        {
            return;
        }
        ComponentsNode()!.RemoveChild(component);
        Session.Edited(entity);
        Edited?.Invoke(entity, false);
        BuildSections();
    }

    private MergedNode? ComponentsNode() => _merged?.Children.FirstOrDefault(c => c.TypeHash == WorldHashes.Components);

    private void BuildSections()
    {
        if (_entity is not { } entity || _merged is not { } merged)
        {
            return;
        }
        var sections = new List<InspectorSection>();
        AddSections(entity, merged, FcbDefinitionsProvider.Value.Value.GetClass(merged.TypeHash), true, sections);
        Sections = sections;

        HashSet<uint> present = [.. ComponentsNode()?.Children.Select(c => c.TypeHash) ?? []];
        AddableComponents = [.. Creatable.Value.Where(c => !present.Contains(c.Hash)).Select(c => c.Name)];
    }

    /// <summary>Re-reads the entity's position and angles, after a gizmo moved it.</summary>
    public void RefreshTransform()
    {
        if (_entity is not { Position: { } position } entity)
        {
            Transform = [];
            return;
        }

        Transform =
        [
            TransformField(WorldHashes.HidPos, "position", position, (e, v) => e.Position = v),
            TransformField(WorldHashes.HidAngles, "angles (ZXY°)", entity.Angles, (e, v) => e.Angles = v),
        ];
    }

    private InspectorField TransformField(uint hash, string name, Vector3 value, Action<WorldEntity, Vector3> apply)
    {
        PropertyRow row = PropertyRow.Build(hash, name, FcbMemberType.Vector3, FcbEntityFields.Vector3Bytes(value), null);
        row.Changed += () =>
        {
            if (_entity is not { } entity || !row.IsRowValid)
            {
                return;
            }
            float[] v = (float[])row.Scalar!.Value;
            apply(entity, new Vector3(v[0], v[1], v[2]));
            Session?.Moved(entity);
            Edited?.Invoke(entity, true);
        };
        return new InspectorField(row, FieldOrigin.InstanceOnly);
    }

    /// <summary>Adds <paramref name="node"/>'s section to <paramref name="into"/> and nests everything
    /// below it inside that section, so collapsing a component hides its whole subtree. The entity's
    /// own children stay top-level, one foldout per component.</summary>
    private void AddSections(WorldEntity entity, MergedNode node, FcbClass own, bool isRoot, List<InspectorSection> into)
    {
        (Dictionary<uint, IReadOnlyList<string>> choices, HashSet<string> enumGroups) = EnumsOf(node, own);
        List<MergedField> fields = [.. node.Fields.Where(f => !isRoot || !NotFields.Contains(f.Hash))];
        bool isComponent = node.Parent?.TypeHash == WorldHashes.Components;
        if (isComponent)
        {
            fields.AddRange(node.UnsetFields(own));
        }

        InspectorSection? section = null;
        if (fields.Count > 0 || isComponent)
        {
            string label = FcbObjectNodeView.FindIdentifyingText(node.Instance ?? node.Archetype!, own) is { Length: > 0 } text
                ? $"{own.Name ?? $"{node.TypeHash:X8}"} - {text}"
                : own.Name ?? $"{node.TypeHash:X8}";
            string origin = node.Instance is null ? "  (inherited)" : node.Archetype is null && !isRoot ? "  (own)" : "";
            section = new InspectorSection(
                label + origin,
                () => [.. fields.Select(f => BuildField(entity, node, own, f, choices.GetValueOrDefault(f.Hash)))],
                isComponent && node.Archetype is null ? () => RemoveComponent(node) : null)
            {
                IsExpanded = isRoot,
            };
            into.Add(section);
        }

        List<InspectorSection> below = isRoot || section is null ? into : section.Children;
        foreach (MergedNode child in node.Children)
        {
            FcbClass childClass = own.Resolve(child.TypeHash);
            if (!enumGroups.Contains(childClass.Name ?? ""))
            {
                AddSections(entity, child, childClass, false, below);
            }
        }
    }

    /// <summary>The <c>selXxx</c> dropdowns either side declares, and the <c>enumXxx</c> groups behind
    /// them, which are shown as the dropdown rather than as sections of their own.</summary>
    private static (Dictionary<uint, IReadOnlyList<string>> Choices, HashSet<string> Groups) EnumsOf(MergedNode node, FcbClass own)
    {
        var choices = new Dictionary<uint, IReadOnlyList<string>>();
        var groups = new HashSet<string>();
        foreach (FcbObject side in new[] { node.Archetype, node.Instance }.OfType<FcbObject>())
        {
            (Dictionary<uint, IReadOnlyList<string>> found, HashSet<string> names) = FcbObjectNodeView.FindEnumChoices(side, own);
            foreach ((uint hash, IReadOnlyList<string> list) in found)
            {
                choices[hash] = list;
            }
            groups.UnionWith(names);
        }
        return (choices, groups);
    }

    private InspectorField BuildField(
        WorldEntity entity, MergedNode node, FcbClass own, MergedField field, IReadOnlyList<string>? choices)
    {
        FcbMember? member = own.FindMember(field.Hash);
        PropertyRow row = PropertyRow.Build(
            field.Hash, member?.Name, member?.Type ?? FcbMemberType.BinHex, field.Value, field.ArchetypeValue, choices);
        var inspected = new InspectorField(row, field.Origin);
        row.Changed += () =>
        {
            if (!row.IsRowValid || Session is null)
            {
                return;
            }

            // The row's baseline is the archetype's value, so "changed from vanilla" reads as "differs
            // from the archetype" here.
            if (inspected.WasInherited && field.ArchetypeValue is not null && !row.IsChangedFromVanilla)
            {
                node.Revert(field.Hash);
                inspected.Origin = FieldOrigin.Inherited;
            }
            else
            {
                node.SetValue(field.Hash, row.EncodeValue());
                inspected.Origin = field.ArchetypeValue is null ? FieldOrigin.InstanceOnly : FieldOrigin.Overridden;
            }
            Session.Edited(entity);
            Edited?.Invoke(entity, false);
        };
        inspected.RevertRequested += () =>
        {
            node.Revert(field.Hash);
            inspected.WasInherited = true;
            inspected.Origin = FieldOrigin.Inherited;
            row.RestoreOriginal();
            Session?.Edited(entity);
            Edited?.Invoke(entity, false);
        };
        return inspected;
    }
}

/// <summary>One node of the merged entity: a component, a slot, or the entity itself. Its rows are
/// built the first time it is opened - an NPC carries thousands of fields and few are ever looked at.</summary>
public sealed class InspectorSection(
    string title, Func<IReadOnlyList<InspectorField>> build, Action? remove = null) : Observable
{
    private readonly List<InspectorSection> _children = [];
    private IReadOnlyList<InspectorField>? _fields;
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

    public IReadOnlyList<InspectorField> Fields => _isExpanded ? _fields ??= build() : [];

    /// <summary>The nodes below this one, shown inside it.</summary>
    public IReadOnlyList<InspectorSection> Sections => _isExpanded ? _children : [];

    internal List<InspectorSection> Children => _children;
}

/// <summary>One field of the merged entity and which side its value comes from.</summary>
public sealed class InspectorField(PropertyRow row, FieldOrigin origin) : Observable
{
    private FieldOrigin _origin = origin;

    public PropertyRow Row { get; } = row;

    /// <summary>Inherited when the inspector opened, so setting it back to the archetype's value
    /// removes the override instead of keeping an identical copy.</summary>
    public bool WasInherited { get; set; } = origin == FieldOrigin.Inherited;

    public FieldOrigin Origin
    {
        get => _origin;
        set
        {
            if (Set(ref _origin, value))
            {
                OnPropertyChanged(nameof(CanRevert));
            }
        }
    }

    public bool CanRevert => _origin == FieldOrigin.Overridden;

    public event Action? RevertRequested;

    public void Revert() => RevertRequested?.Invoke();
}
