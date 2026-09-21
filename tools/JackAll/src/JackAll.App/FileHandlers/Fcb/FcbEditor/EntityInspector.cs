using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>
/// One entity as the engine reads it, its instance merged over its archetype: a foldout per
/// component, each field marked with where its value comes from, and components to add or remove.
/// </summary>
public sealed class EntityInspector : Observable
{
    private static readonly Lazy<(string Name, uint Hash)[]> Creatable = new(() =>
    [
        .. FcbDefinitionsProvider.Schema.Value.Classes
            .Where(c => c.IsComponent && c.Creatable)
            .Select(c => (c.Name, FcbClassDefinitions.Crc32Ascii(c.Name))),
    ]);

    private readonly MergedNode _entity;
    private readonly MergedNode? _baseline;
    private readonly FcbEditContext _context;
    private readonly IReadOnlySet<uint> _hiddenFields;
    private IReadOnlyList<NodeView> _sections = [];
    private IReadOnlyList<string> _addableComponents = [];

    /// <param name="baseline">The same entity as the document opened it, merged over the same base.</param>
    /// <param name="hiddenFields">Entity fields a host shows its own way rather than as fields.</param>
    public EntityInspector(
        MergedNode entity, MergedNode? baseline, FcbEditContext context, IReadOnlySet<uint>? hiddenFields = null)
    {
        _entity = entity;
        _baseline = baseline;
        _context = context;
        _hiddenFields = hiddenFields ?? new HashSet<uint>();
        Build();
    }

    public IReadOnlyList<NodeView> Sections
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

    /// <summary>Adds an empty <paramref name="className"/> component to the instance; its registered
    /// properties show as unset until one is edited.</summary>
    public void AddComponent(string className)
    {
        MergedNode components = ComponentsNode() ?? _entity.AddChild(new FcbObject { TypeHash = WorldHashes.Components });
        components.AddChild(new FcbObject { TypeHash = FcbClassDefinitions.Crc32Ascii(className) });
        _context.OnEdited();
        Build();
    }

    private void RemoveComponent(MergedNode component)
    {
        ComponentsNode()!.RemoveChild(component);
        _context.OnEdited();
        Build();
    }

    private MergedNode? ComponentsNode() => _entity.Children.FirstOrDefault(c => c.TypeHash == WorldHashes.Components);

    private void Build()
    {
        var sections = new List<NodeView>();
        AddSections(new ScopedNode(_entity, _baseline, _context.Definitions.GetClass(_entity.TypeHash)), true, sections);
        Sections = sections;

        HashSet<uint> present = [.. ComponentsNode()?.Children.Select(c => c.TypeHash) ?? []];
        AddableComponents = [.. Creatable.Value.Where(c => !present.Contains(c.Hash)).Select(c => c.Name)];
    }

    /// <summary>Adds <paramref name="scope"/>'s section, nesting everything below it; the entity's own
    /// children stay top-level, one foldout per component.</summary>
    private void AddSections(ScopedNode scope, bool isRoot, List<NodeView> into)
    {
        MergedNode node = scope.Node;
        bool isComponent = node.Parent?.TypeHash == WorldHashes.Components;
        bool hasSection = isComponent || (isRoot
            ? node.Fields.Any(f => !_hiddenFields.Contains(f.Hash))
            : (node.Instance?.Values.Count ?? 0) + (node.Base?.Values.Count ?? 0) > 0);

        var children = new List<NodeView>();
        if (hasSection)
        {
            string origin = node.Instance is null ? "  (inherited)" : node.Base is null && !isRoot ? "  (own)" : "";
            into.Add(new NodeView(
                FcbNodeViews.Label(node.Shown, scope.Class, _context) + origin,
                () => scope.Fields(FieldsOf(scope, isRoot, isComponent), _context),
                () => children,
                isComponent && node.Base is null ? () => RemoveComponent(node) : null)
            {
                IsExpanded = isRoot,
            });
        }

        List<NodeView> below = isRoot || !hasSection ? into : children;
        foreach (ScopedNode child in scope.Children())
        {
            AddSections(child, false, below);
        }
    }

    /// <summary>A component also lists the registered members neither side sets.</summary>
    private IEnumerable<MergedField> FieldsOf(ScopedNode scope, bool isRoot, bool isComponent)
    {
        IEnumerable<MergedField> fields = scope.Node.Fields.Where(f => !isRoot || !_hiddenFields.Contains(f.Hash));
        return isComponent ? fields.Concat(scope.Node.UnsetFields(scope.Class)) : fields;
    }
}
