using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;

namespace JackAll.Core.Xrefs;

/// <summary>
/// Where an <see cref="IReferenceExtractor"/> writes what it found, plus the shared lookups it needs
/// to interpret one file's bytes.
/// </summary>
/// <remarks>
/// A sink rather than an <c>IEnumerable&lt;RefEdge&gt;</c> return: extraction is naturally a
/// recursive tree walk (an `.fcb` object graph, an `.mgb` widget tree), and threading a yield-return
/// through one means either materializing a list per node or writing the whole walk as an iterator
/// state machine. Both cost more than they're worth across ~180,000 files. The sink also owns
/// per-file duplicate suppression, which a caller would otherwise have to repeat.
///
/// One instance is reused for the whole build, with <see cref="BeginFile"/> called between files -
/// the lists grow once instead of being reallocated per file.
/// </remarks>
public sealed class ReferenceSink
{
    private readonly List<RefEdge> _edges = [];
    private readonly List<RefDefinition> _definitions = [];

    /// <summary>
    /// Site-name hash → the name itself, for every site an extractor could name. Global (not cleared
    /// per file) and tiny: the vocabulary is the set of distinct member/slot/property names across
    /// the whole game, a few thousand strings, against millions of edges. This is what lets an xref
    /// row say "DiffuseTexture1" instead of "#3F2A91C4" without paying a string per edge, and it
    /// covers names no external dictionary knows - an `.mgb` <c>UserData</c> key never appears in
    /// <c>binary_classes.xml</c>, but the package that defines it spells it out.
    /// </summary>
    private readonly Dictionary<uint, string> _names = [];

    /// <summary>Exact duplicates already emitted *for the current file*. Cleared per file rather
    /// than kept global: the same texture referenced from two different `.fcb`s is two genuinely
    /// different xrefs, but the same slot emitted twice from one file is noise.</summary>
    private readonly HashSet<RefEdge> _seenInFile = [];

    private uint _sourceFile;

    public ReferenceSink(FcbClassDefinitions classes) => Classes = classes;

    /// <summary>The `.fcb` class/member dictionary, for extractors that need to know a value's
    /// declared wire type before they can tell a path from arbitrary bytes.</summary>
    public FcbClassDefinitions Classes { get; }

    public IReadOnlyList<RefEdge> Edges => _edges;
    public IReadOnlyList<RefDefinition> Definitions => _definitions;
    public IReadOnlyDictionary<uint, string> Names => _names;

    /// <summary>Every edge and definition collected so far, handed over and forgotten - used by the
    /// indexer to drain the sink between batches without reallocating it. <see cref="Names"/> is
    /// deliberately *not* drained: it's the accumulating vocabulary for the whole build.</summary>
    public (RefEdge[] Edges, RefDefinition[] Definitions) Drain()
    {
        var edges = _edges.ToArray();
        var defs = _definitions.ToArray();
        _edges.Clear();
        _definitions.Clear();
        return (edges, defs);
    }

    /// <summary>Points the sink at the file about to be extracted. Every <see cref="Add"/> until the
    /// next call attributes to <paramref name="sourceFile"/>.</summary>
    public void BeginFile(uint sourceFile)
    {
        _sourceFile = sourceFile;
        _seenInFile.Clear();
    }

    /// <summary>Records a reference from the current file to <paramref name="target"/>.</summary>
    public void Add(RefSpace space, uint target, RefKind kind, uint siteKey, int siteIndex = 0)
    {
        // A zero target is the engine's own "unset" for every space here (and 0xFFFFFFFF is the
        // explicit sentinel .spk uses) - indexing either would bury the real edges under tens of
        // thousands of links to nothing.
        if (target is 0 or 0xFFFFFFFF)
        {
            return;
        }

        var edge = new RefEdge(_sourceFile, space, target, kind, siteKey,
            (ushort)Math.Clamp(siteIndex, 0, ushort.MaxValue));
        if (_seenInFile.Add(edge))
        {
            _edges.Add(edge);
        }
    }

    /// <summary>
    /// Records a reference to a game-relative path, hashing it the way the archive index does. Does
    /// nothing when <paramref name="rawPath"/> doesn't look like a path at all (see
    /// <see cref="ReferencePaths.LooksLikeGamePath"/>) - most `.fcb` <c>String</c> values are display
    /// names, signal names and enum-ish tags, not paths.
    /// </summary>
    public void AddPath(string? rawPath, RefKind kind, uint siteKey, int siteIndex = 0)
    {
        if (!ReferencePaths.LooksLikeGamePath(rawPath))
        {
            return;
        }
        Add(RefSpace.FilePath, NameHash.Compute(rawPath!), kind, siteKey, siteIndex);
    }

    /// <summary>
    /// <see cref="Add"/>, but for a site the extractor can name (an `.xbm` texture slot, an `.mgb`
    /// property key, a resolved `.fcb` member). The name is hashed for the edge's
    /// <see cref="RefEdge.SiteKey"/> and remembered in <see cref="Names"/> so the xref list can
    /// show it back.
    /// </summary>
    public void AddNamed(RefSpace space, uint target, RefKind kind, string siteName, int siteIndex = 0)
        => Add(space, target, kind, Intern(siteName), siteIndex);

    /// <summary>Same as <see cref="AddPath"/>, with a named site.</summary>
    public void AddNamedPath(string? rawPath, RefKind kind, string siteName, int siteIndex = 0)
    {
        if (ReferencePaths.LooksLikeGamePath(rawPath))
        {
            Add(RefSpace.FilePath, NameHash.Compute(rawPath!), kind, Intern(siteName), siteIndex);
        }
    }

    /// <summary>Records <paramref name="siteName"/> in the shared vocabulary and returns its hash -
    /// for extractors that need the key before they know whether they'll emit an edge.</summary>
    public uint Intern(string siteName)
    {
        uint key = FcbClassDefinitions.Crc32Ascii(siteName);
        _names.TryAdd(key, siteName);
        return key;
    }

    /// <summary>Records that the current file is where <paramref name="id"/> is defined.</summary>
    public void Define(RefSpace space, uint id, uint siteKey)
    {
        if (id is 0 or 0xFFFFFFFF)
        {
            return;
        }
        _definitions.Add(new RefDefinition(space, id, _sourceFile, siteKey));
    }
}

/// <summary>
/// Reads one file's references out of its bytes. One implementation per format, mirroring the
/// one-case-per-format shape <c>FileHandlerCatalog.CreateView</c> already uses in the app.
/// </summary>
/// <remarks>
/// Implementations live wherever their parser does: the formats decoded in <c>JackAll.Core</c>
/// (`.fcb`, `depload.dat`, plain text) have their extractors here, and the ones decoded in
/// <c>JackAll.Tools</c> (`.mgb`, `.spk`, `.sbao`, `.xbm`, `.xbg`) have theirs there, since Core
/// can't reference Tools. <see cref="ReferenceIndexer"/> takes the assembled set as a parameter
/// rather than constructing it, so neither project has to know about the other.
/// </remarks>
public interface IReferenceExtractor
{
    /// <summary>Whether this extractor handles <paramref name="file"/>'s type. Checked before the
    /// file's content is read, so a file no extractor claims is never decompressed at all.</summary>
    bool CanHandle(VfsFile file);

    /// <summary>Writes every reference in <paramref name="content"/> to <paramref name="sink"/>.
    /// Implementations may throw on malformed input; the indexer treats that as "no references"
    /// and carries on, the same way the app's file handlers already tolerate an unparseable
    /// file.</summary>
    void Extract(VfsFile file, byte[] content, ReferenceSink sink);
}
