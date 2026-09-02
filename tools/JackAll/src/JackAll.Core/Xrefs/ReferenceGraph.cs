namespace JackAll.Core.Xrefs;

/// <summary>
/// What the UI actually queries: the persisted base-archive <see cref="ReferenceIndex"/> with this
/// session's mod/workspace references layered on top.
/// </summary>
/// <remarks>
/// The layering exists because the two halves have incompatible lifetimes. The base index is huge
/// (millions of edges) and immutable for the life of the install; the overlay is small (a mod is
/// tens to hundreds of files) and changes on every mod toggle. Rebuilding one combined index per
/// toggle would mean re-sorting millions of records to reflect a few hundred - so instead the base
/// index is never touched, and a toggle only rebuilds the overlay.
///
/// A modded file also has to *shadow* its base-archive edges rather than adding to them: if a mod
/// replaces an `.fcb` and drops a texture reference, the engine no longer follows that reference and
/// neither should the graph. <see cref="_shadowed"/> is what makes a query skip them.
/// </remarks>
public sealed class ReferenceGraph
{
    private readonly ReferenceIndex _base;
    private readonly Dictionary<uint, List<RefEdge>> _overlayBySource = [];
    private readonly Dictionary<(RefSpace Space, uint Target), List<RefEdge>> _overlayByTarget = [];
    private readonly Dictionary<(RefSpace Space, uint Id), RefDefinition> _overlayDefinitions = [];
    private readonly Dictionary<uint, string> _overlayNames;

    /// <summary>Files the overlay supplies, whose base-archive edges must therefore be ignored.</summary>
    private readonly HashSet<uint> _shadowed;

    /// <summary>An empty graph - what the app queries before the background build has finished.</summary>
    public static ReferenceGraph Empty { get; } =
        new(ReferenceIndex.Empty, new ReferenceHarvest([], [], new Dictionary<uint, string>(), [], []));

    public ReferenceGraph(ReferenceIndex baseIndex, ReferenceHarvest overlay)
    {
        _base = baseIndex;
        _overlayNames = new Dictionary<uint, string>(overlay.Names);
        _shadowed = [.. overlay.Files];

        foreach (RefEdge edge in overlay.Edges)
        {
            if (!_overlayBySource.TryGetValue(edge.SourceFile, out List<RefEdge>? bySource))
            {
                _overlayBySource[edge.SourceFile] = bySource = [];
            }
            bySource.Add(edge);

            var key = (edge.TargetSpace, edge.Target);
            if (!_overlayByTarget.TryGetValue(key, out List<RefEdge>? byTarget))
            {
                _overlayByTarget[key] = byTarget = [];
            }
            byTarget.Add(edge);
        }

        foreach (RefDefinition definition in overlay.Definitions)
        {
            _overlayDefinitions[(definition.Space, definition.Id)] = definition;
        }
    }

    public int BaseEdgeCount => _base.EdgeCount;
    public int OverlayEdgeCount => _overlayBySource.Values.Sum(list => list.Count);
    public int IndexedFileCount => _base.IndexedFileCount + _shadowed.Count;

    /// <summary>Everything that references <paramref name="target"/>, base and overlay together.</summary>
    public IReadOnlyList<RefEdge> ReferencesTo(RefSpace space, uint target)
    {
        IReadOnlyList<RefEdge> fromBase = _base.ReferencesTo(space, target);
        _overlayByTarget.TryGetValue((space, target), out List<RefEdge>? fromOverlay);

        if (fromOverlay is null && _shadowed.Count == 0)
        {
            return fromBase;
        }

        var result = new List<RefEdge>(fromBase.Count + (fromOverlay?.Count ?? 0));
        foreach (RefEdge edge in fromBase)
        {
            if (!_shadowed.Contains(edge.SourceFile))
            {
                result.Add(edge);
            }
        }
        if (fromOverlay is not null)
        {
            result.AddRange(fromOverlay);
        }
        return result;
    }

    /// <summary>Everything <paramref name="sourceFile"/> references. A file the overlay supplies is
    /// answered entirely from the overlay - its base-archive edges describe bytes no longer in
    /// play.</summary>
    public IReadOnlyList<RefEdge> ReferencesFrom(uint sourceFile)
        => _overlayBySource.TryGetValue(sourceFile, out List<RefEdge>? overlay)
            ? overlay
            : _shadowed.Contains(sourceFile) ? [] : _base.ReferencesFrom(sourceFile);

    public bool TryGetDefinition(RefSpace space, uint id, out RefDefinition definition)
        => _overlayDefinitions.TryGetValue((space, id), out definition)
        || _base.TryGetDefinition(space, id, out definition);

    /// <summary>Every edge, base and overlay together, with shadowed base sources skipped - for
    /// analyses that sweep the whole graph once instead of querying per key.</summary>
    public IEnumerable<RefEdge> AllEdges()
    {
        foreach (RefEdge edge in _base.AllEdges())
        {
            if (!_shadowed.Contains(edge.SourceFile))
            {
                yield return edge;
            }
        }
        foreach (List<RefEdge> edges in _overlayBySource.Values)
        {
            foreach (RefEdge edge in edges)
            {
                yield return edge;
            }
        }
    }

    /// <summary>Every definition, with the same shadowing rule as <see cref="AllEdges"/>.</summary>
    public IEnumerable<RefDefinition> AllDefinitions()
    {
        foreach (RefDefinition definition in _base.AllDefinitions())
        {
            if (!_shadowed.Contains(definition.DefiningFile))
            {
                yield return definition;
            }
        }
        foreach (RefDefinition definition in _overlayDefinitions.Values)
        {
            yield return definition;
        }
    }

    /// <summary>The readable name of a reference site, or null when only its hash is known.</summary>
    public string? Name(uint siteKey)
        => _overlayNames.TryGetValue(siteKey, out string? name) ? name : _base.Name(siteKey);

    /// <summary>Rendered site text for an xref row: the site's name when known, the array index
    /// appended when the member holds more than one value, and a bare <c>#XXXXXXXX</c> when the name
    /// isn't recoverable - the same convention <c>MgbNameLookup.Describe</c> already uses, so an
    /// unresolved hash reads the same way everywhere in the app.</summary>
    public string DescribeSite(RefEdge edge)
    {
        string name = Name(edge.SiteKey) ?? $"#{edge.SiteKey:X8}";
        return edge.SiteIndex == 0 ? name : $"{name}[{edge.SiteIndex}]";
    }
}
