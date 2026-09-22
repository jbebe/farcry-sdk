using System.Numerics;
using JackAll.App.FileHandlers.Fcb;
using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>
/// The Map tab's inspector: the selected entity's heading and transform over an
/// <see cref="EntityInspector"/>. An edit goes straight into the instance and is pending until the
/// next save.
/// </summary>
public sealed partial class InspectorViewModel : Observable
{
    /// <summary>Identity and placement: shown in the heading and the Transform section, not as fields.</summary>
    private static readonly HashSet<uint> NotFields =
    [
        WorldHashes.DisEntityId, WorldHashes.HidName, WorldHashes.TplCreatureType,
        WorldHashes.HidPos, WorldHashes.HidPosPrecise, WorldHashes.HidAngles,
    ];

    private readonly SelectionSet _selection;
    private WorldEntity? _entity;
    private string _heading = "";
    private string _details = "";
    private bool _canShowArchetype;
    private EntityInspector? _inspector;
    private IReadOnlyList<FieldView> _transform = [];

    public InspectorViewModel(SelectionSet selection)
    {
        _selection = selection;
        selection.Changed += Refresh;
    }

    /// <summary>What field edits go into; set once a world loads.</summary>
    public WorldEditSession? Session { get; set; }

    /// <summary>What the selected entity merges over; set once a world loads.</summary>
    public ArchetypeIndex? Archetypes { get; set; }

    /// <summary>Where each edit is recorded for undo; set once a world loads.</summary>
    public EditHistory? History { get; set; }

    /// <summary>The inspected node as the last recorded step left it - the before of the next one.</summary>
    private FcbObject? _before;

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
    public IReadOnlyList<FieldView> Transform
    {
        get => _transform;
        private set => Set(ref _transform, value);
    }

    public EntityInspector? Inspector
    {
        get => _inspector;
        private set => Set(ref _inspector, value);
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
        OnPropertyChanged(nameof(HasEntity));
        OnPropertyChanged(nameof(CanOpenSector));
        if (entity is null || Session is null)
        {
            Heading = Details = "";
            Transform = [];
            Inspector = null;
            Links = [];
            return;
        }

        FcbObject? archetype = entity.ArchetypeName.Length > 0 ? Archetypes?.Winner(entity.ArchetypeName)?.Node : null;
        CanShowArchetype = archetype is not null;
        Heading = EntityHierarchy.LabelOf(entity) + more;
        Details = $"layer {entity.LayerPathId} · sector {entity.HomeSector.SectorId} · id {entity.Id}\n"
            + ArchetypeBases.Line(entity.ArchetypeName, archetype is not null);

        RefreshTransform();
        FcbObject node = Session.EditableNode(entity);
        _before = node.Clone();
        var context = new FcbEditContext();
        context.Edited += () => Commit(entity, $"Edit {entity.Name}");
        Inspector = new EntityInspector(MergedNode.Of(node, archetype), null, context, NotFields);
        RefreshLinks();
    }

    /// <summary>Records a change already written into the entity's node as one undoable step.</summary>
    private void Commit(WorldEntity entity, string label)
    {
        if (Session is not { } session || _before is null)
        {
            return;
        }
        session.Edited(entity);
        FcbObject after = session.EditableNode(entity).Clone();
        History?.Push(new NodeEditStep(session, entity, _before, after, label));
        _before = after;
        Edited?.Invoke(entity, false);
    }

    /// <summary>Rebuilds the inspector over the same entity, after an undo changed what it shows.</summary>
    public void Reload()
    {
        _entity = null;
        Refresh();
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

    private FieldView TransformField(uint hash, string name, Vector3 value, Action<WorldEntity, Vector3> apply)
    {
        PropertyRow row = PropertyRow.Build(hash, name, FcbMemberType.Vector3, FcbEntityFields.Vector3Bytes(value), null);
        row.Changed += () =>
        {
            if (_entity is not { } entity || !row.IsRowValid)
            {
                return;
            }
            float[] v = (float[])row.Scalar!.Value;
            Placement before = Placement.Of(entity);
            apply(entity, new Vector3(v[0], v[1], v[2]));
            if (Session is { } session)
            {
                session.Moved(entity);
                History?.Push(new MoveStep(session,
                    new Dictionary<WorldEntity, (Placement, Placement)> { [entity] = (before, Placement.Of(entity)) },
                    mergeKey: name));
            }
            Edited?.Invoke(entity, true);
        };
        return new FieldView(row, FieldOrigin.InstanceOnly);
    }
}
